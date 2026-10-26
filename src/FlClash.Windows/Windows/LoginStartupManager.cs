using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FlClash.Windows.Windows
{
    public static class LoginStartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppKey = "FlClashWindowsNative";

        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) throw new InvalidOperationException("无法访问当前用户开机启动注册表项。");
                if (enabled) key.SetValue(AppKey, "\"" + Application.ExecutablePath + "\" --background", RegistryValueKind.String);
                else key.DeleteValue(AppKey, false);
            }
        }
    }
}
