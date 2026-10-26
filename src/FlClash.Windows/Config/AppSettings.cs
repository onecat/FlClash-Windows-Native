using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace FlClash.Windows.Config
{
    public sealed class AppSettings
    {
        public bool LaunchAtLogin { get; set; } = true;
        public bool StartCoreWithApp { get; set; } = true;
        public bool EnableSystemProxyWhenReady { get; set; } = true;
        public bool ShowAllGroups { get; set; } = false;
        public bool CheckUpdatesAtStartup { get; set; } = false;
        public bool AutoCheckUpdates { get; set; } = false;
        public string Theme { get; set; } = "system";
        public string ProfilePath { get; set; }
        public int MixedPort { get; set; } = 17890;
        public int ControllerPort { get; set; } = 19090;

        public static AppSettings Load(string file)
        {
            if (!File.Exists(file)) return new AppSettings();
            var settings = new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(file, Encoding.UTF8));
            if (settings == null || settings.MixedPort < 1024 || settings.MixedPort > 65535 ||
                settings.ControllerPort < 1024 || settings.ControllerPort > 65535 ||
                settings.MixedPort == settings.ControllerPort)
                throw new InvalidDataException("配置文件中的端口设置无效。");
            return settings;
        }

        public void Save(string file)
        {
            var temp = file + ".tmp";
            File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }
    }
}
