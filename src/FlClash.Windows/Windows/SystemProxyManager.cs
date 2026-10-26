using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using FlClash.Windows.Config;
using Microsoft.Win32;

namespace FlClash.Windows.Windows
{
    // A lease allows us to restore only an unchanged proxy setting that WE wrote.
    // The previous per-user values are persisted before applying changes, so a
    // crash followed by a new launch can recover without blindly clearing proxies.
    public sealed class SystemProxyManager
    {
        private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const int InternetOptionSettingsChanged = 39;
        private const int InternetOptionRefresh = 37;
        private readonly string _leaseFile;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public sealed class Snapshot
        {
            public bool HasEnabled { get; set; }
            public int Enabled { get; set; }
            public bool HasServer { get; set; }
            public string Server { get; set; }
            public bool HasAutoConfig { get; set; }
            public string AutoConfig { get; set; }
        }
        public sealed class Lease
        {
            public string ManagedServer { get; set; }
            public Snapshot Before { get; set; }
        }

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int option, IntPtr buffer, int length);

        public SystemProxyManager(AppPaths paths) => _leaseFile = paths.ProxyLeaseFile;

        public bool OwnsCurrent
        {
            get
            {
                var lease = LoadLease();
                return lease != null && IsManaged(ReadSnapshot(), lease.ManagedServer);
            }
        }

        public void Enable(int port)
        {
            var endpoint = "127.0.0.1:" + port;
            if (OwnsCurrent) return;
            var existing = LoadLease();
            if (existing != null)
                throw new InvalidOperationException("存在系统代理恢复记录且当前设置已被其他程序改变。请先检查系统代理设置。");
            var before = ReadSnapshot();
            SaveLease(new Lease { Before = before, ManagedServer = endpoint });
            using (var key = OpenSettings(true))
            {
                key.SetValue("ProxyServer", endpoint, RegistryValueKind.String);
                key.DeleteValue("AutoConfigURL", false);
                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            }
            NotifyChanged();
        }

        public bool RestoreIfOwned()
        {
            var lease = LoadLease();
            if (lease == null) return false;
            var current = ReadSnapshot();
            if (!IsManaged(current, lease.ManagedServer))
            {
                // Another app/user now owns system proxy settings. Never overwrite it.
                return false;
            }
            using (var key = OpenSettings(true))
            {
                RestoreValue(key, "ProxyEnable", lease.Before.HasEnabled, lease.Before.Enabled, RegistryValueKind.DWord);
                RestoreValue(key, "ProxyServer", lease.Before.HasServer, lease.Before.Server, RegistryValueKind.String);
                RestoreValue(key, "AutoConfigURL", lease.Before.HasAutoConfig, lease.Before.AutoConfig, RegistryValueKind.String);
            }
            NotifyChanged();
            File.Delete(_leaseFile);
            return true;
        }

        public void ForgetForeignLease()
        {
            if (OwnsCurrent) throw new InvalidOperationException("仍在使用本客户端的系统代理，不能丢弃恢复记录。");
            if (File.Exists(_leaseFile)) File.Delete(_leaseFile);
        }

        private static bool IsManaged(Snapshot snapshot, string endpoint)
        {
            return snapshot.HasEnabled && snapshot.Enabled == 1 &&
                   snapshot.HasServer && string.Equals(snapshot.Server, endpoint, StringComparison.OrdinalIgnoreCase) &&
                   !snapshot.HasAutoConfig;
        }

        private static void RestoreValue(RegistryKey key, string name, bool existed, object value, RegistryValueKind kind)
        {
            if (existed) key.SetValue(name, value, kind);
            else key.DeleteValue(name, false);
        }

        private static RegistryKey OpenSettings(bool writable)
        {
            var key = Registry.CurrentUser.OpenSubKey(SettingsKey, writable);
            if (key == null) throw new InvalidOperationException("无法读取 Windows 系统代理设置。");
            return key;
        }

        private static Snapshot ReadSnapshot()
        {
            using (var key = OpenSettings(false))
            {
                var enabled = key.GetValue("ProxyEnable", null);
                var server = key.GetValue("ProxyServer", null);
                var pac = key.GetValue("AutoConfigURL", null);
                return new Snapshot
                {
                    HasEnabled = enabled != null,
                    Enabled = enabled == null ? 0 : Convert.ToInt32(enabled),
                    HasServer = server != null,
                    Server = server as string,
                    HasAutoConfig = pac != null,
                    AutoConfig = pac as string
                };
            }
        }

        private Lease LoadLease()
        {
            if (!File.Exists(_leaseFile)) return null;
            var lease = _json.Deserialize<Lease>(File.ReadAllText(_leaseFile, Encoding.UTF8));
            if (lease == null || lease.Before == null || string.IsNullOrWhiteSpace(lease.ManagedServer))
                throw new InvalidDataException("系统代理恢复记录损坏，请手动检查代理设置。");
            return lease;
        }

        private void SaveLease(Lease lease)
        {
            string pending = _leaseFile + ".tmp";
            File.WriteAllText(pending, _json.Serialize(lease), new UTF8Encoding(false));
            if (File.Exists(_leaseFile)) File.Replace(pending, _leaseFile, null);
            else File.Move(pending, _leaseFile);
        }

        private static void NotifyChanged()
        {
            InternetSetOption(IntPtr.Zero, InternetOptionSettingsChanged, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, InternetOptionRefresh, IntPtr.Zero, 0);
        }
    }
}
