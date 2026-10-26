using System;
using System.IO;

namespace FlClash.Windows.Config
{
    public sealed class AppPaths
    {
        public string Root { get; private set; }
        public string Runtime { get; private set; }
        public string Profiles { get; private set; }
        public string CoreExecutable { get; private set; }
        public string SettingsFile { get { return Path.Combine(Root, "settings.json"); } }
        public string ProxyLeaseFile { get { return Path.Combine(Root, "proxy-lease.json"); } }
        public string ActiveConfiguration { get { return Path.Combine(Runtime, "active.yaml"); } }
        public string PreviousConfiguration { get { return Path.Combine(Runtime, "previous.yaml"); } }

        public AppPaths(string root, string executableDirectory)
        {
            Root = Path.GetFullPath(root);
            Runtime = Path.Combine(Root, "core-data");
            Profiles = Path.Combine(Root, "profiles");
            CoreExecutable = Path.Combine(executableDirectory, "core", "mihomo.exe");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Runtime);
            Directory.CreateDirectory(Profiles);
        }

        public static AppPaths ForCurrentUser()
        {
            return new AppPaths(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlClashWindows"),
                AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
