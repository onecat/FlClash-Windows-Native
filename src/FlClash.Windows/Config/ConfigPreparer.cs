using System;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.RepresentationModel;

namespace FlClash.Windows.Config
{
    public static class ConfigPreparer
    {
        // Stage 1 intentionally disables privileged listeners and TUN. Never pass a raw
        // subscription to a privileged core or permit it to choose the controller address.
        private static readonly string[] ForbiddenRoot = {
            "external-controller-unix", "external-controller-pipe", "external-controller-tls",
            "external-controller-cors", "external-ui", "external-ui-name", "external-ui-url", "external-doh-server", "listeners", "tunnels", "ss-config",
            "vmess-config", "tuic-server", "iptables", "tun", "redir-port",
            "tproxy-port", "port", "socks-port", "mixed-port", "external-controller",
            "secret", "allow-lan", "bind-address"
        };

        public static string Compose(string yaml, int proxyPort, int apiPort, string token)
        {
            if (string.IsNullOrWhiteSpace(yaml)) throw new InvalidDataException("YAML 配置为空。");
            if (Encoding.UTF8.GetByteCount(yaml) > 8 * 1024 * 1024)
                throw new InvalidDataException("原型版只支持不超过 8 MiB 的配置。");
            if (string.IsNullOrEmpty(token) || token.Length < 32)
                throw new ArgumentException("控制密钥长度不足。", nameof(token));
            if (proxyPort < 1024 || proxyPort > 65535 || apiPort < 1024 || apiPort > 65535 || proxyPort == apiPort)
                throw new ArgumentOutOfRangeException(nameof(proxyPort), "代理端口和控制端口必须不同，且处于 1024–65535。");

            var stream = new YamlStream();
            using (var reader = new StringReader(yaml)) stream.Load(reader);
            if (stream.Documents.Count != 1 || !(stream.Documents[0].RootNode is YamlMappingNode))
                throw new InvalidDataException("YAML 必须包含一个顶层 Mapping。");

            var root = (YamlMappingNode)stream.Documents[0].RootNode;
            foreach (var key in ForbiddenRoot) Remove(root, key);
            Set(root, "mixed-port", proxyPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set(root, "allow-lan", "false");
            Set(root, "bind-address", "127.0.0.1");
            Set(root, "external-controller", "127.0.0.1:" + apiPort);
            Set(root, "secret", token);
            Set(root, "port", "0");
            Set(root, "socks-port", "0");
            Set(root, "redir-port", "0");
            Set(root, "tproxy-port", "0");
            Set(root, "tun", new YamlMappingNode { { "enable", "false" } });

            YamlNode dns;
            if (TryGet(root, "dns", out dns) && dns is YamlMappingNode)
            {
                Remove((YamlMappingNode)dns, "listen");
            }
            YamlNode ntp;
            if (TryGet(root, "ntp", out ntp) && ntp is YamlMappingNode)
            {
                Set((YamlMappingNode)ntp, "write-to-system", "false");
            }

            using (var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture))
            {
                stream.Save(output, false);
                return output.ToString();
            }
        }

        public static void Set(YamlMappingNode root, string key, string value) => Set(root, key, new YamlScalarNode(value));
        public static void Set(YamlMappingNode root, string key, YamlNode value)
        {
            Remove(root, key);
            root.Add(new YamlScalarNode(key), value);
        }

        public static bool TryGet(YamlMappingNode root, string key, out YamlNode value)
        {
            foreach (var pair in root.Children)
            {
                var scalar = pair.Key as YamlScalarNode;
                if (scalar != null && string.Equals(scalar.Value, key, StringComparison.Ordinal))
                {
                    value = pair.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public static void Remove(YamlMappingNode root, string key)
        {
            foreach (var matching in root.Children.Keys.OfType<YamlScalarNode>()
                .Where(k => string.Equals(k.Value, key, StringComparison.Ordinal)).ToArray())
                root.Children.Remove(matching);
        }
    }
}
