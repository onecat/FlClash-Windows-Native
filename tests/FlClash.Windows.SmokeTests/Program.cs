using System;
using System.IO;
using System.Linq;
using FlClash.Windows.Config;
using YamlDotNet.RepresentationModel;

namespace FlClash.Windows.SmokeTests
{
    internal static class Program
    {
        private static int _passed;
        private static void Check(bool test, string message)
        {
            if (!test) throw new Exception("FAILED: " + message);
            Console.WriteLine("PASS: " + message);
            _passed++;
        }

        private static YamlMappingNode Root(string yaml)
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            return (YamlMappingNode)stream.Documents.Single().RootNode;
        }

        private static string Value(YamlMappingNode root, string key)
        {
            YamlNode node;
            return ConfigPreparer.TryGet(root, key, out node) ? ((YamlScalarNode)node).Value : null;
        }

        private static int Main()
        {
            try
            {
                const string input = @"mixed-port: 9999
allow-lan: true
external-controller: 0.0.0.0:9090
external-controller-pipe: '\\.\pipe\mihomo'
secret: attacker
listeners:
  - name: test
    type: mixed
    port: 4567
tun:
  enable: true
dns:
  enable: true
  listen: 0.0.0.0:53
ntp:
  enable: true
  write-to-system: true
proxy-groups:
  - name: Group
    type: select
    proxies: [DIRECT]
rules:
  - MATCH,DIRECT
";
                var result = ConfigPreparer.Compose(input, 17890, 19090, new string('a', 64));
                var root = Root(result);
                Check(Value(root, "mixed-port") == "17890", "proxy port forced");
                Check(Value(root, "external-controller") == "127.0.0.1:19090", "controller loopback enforced");
                Check(Value(root, "allow-lan") == "false", "LAN exposure disabled");
                Check(Value(root, "secret") == new string('a', 64), "controller token enforced");
                Check(!ConfigPreparer.TryGet(root, "listeners", out _), "extra listeners removed");
                Check(!ConfigPreparer.TryGet(root, "external-controller-pipe", out _), "unauthenticated pipe removed");
                YamlNode tun; ConfigPreparer.TryGet(root, "tun", out tun);
                Check(Value((YamlMappingNode)tun, "enable") == "false", "TUN disabled for prototype");
                YamlNode dns; ConfigPreparer.TryGet(root, "dns", out dns);
                Check(!ConfigPreparer.TryGet((YamlMappingNode)dns, "listen", out _), "DNS listener disabled");
                YamlNode ntp; ConfigPreparer.TryGet(root, "ntp", out ntp);
                Check(Value((YamlMappingNode)ntp, "write-to-system") == "false", "clock changes disabled");
                Check(result.Contains("proxy-groups"), "proxy groups retained");

                var failed = false;
                try { ConfigPreparer.Compose("- not a map", 17890, 19090, new string('a', 64)); }
                catch (InvalidDataException) { failed = true; }
                Check(failed, "invalid root rejected");
                var defaults = new AppSettings();
                Check(defaults.LaunchAtLogin && defaults.StartCoreWithApp && defaults.EnableSystemProxyWhenReady, "startup defaults");
                Check(!defaults.CheckUpdatesAtStartup && !defaults.AutoCheckUpdates && defaults.Theme == "system", "update and theme defaults");
                Console.WriteLine("Smoke tests passed: " + _passed);
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
