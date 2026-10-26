using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FlClash.Windows.Core
{
    public sealed class ProxyGroup
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Selected { get; set; }
        public string[] Members { get; set; }
        public override string ToString() => Name;
    }

    public sealed class MihomoApiClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        public Uri WebSocketBase { get; private set; }
        public string Token { get; private set; }

        public MihomoApiClient(int port, string token)
        {
            Token = token ?? throw new ArgumentNullException(nameof(token));
            _http = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                BaseAddress = new Uri("http://127.0.0.1:" + port + "/"),
                Timeout = TimeSpan.FromSeconds(3)
            };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            WebSocketBase = new Uri("ws://127.0.0.1:" + port + "/");
        }

        private async Task<Dictionary<string, object>> GetAsync(string route, CancellationToken token)
        {
            using (var response = await _http.GetAsync(route, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return _json.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }

        public async Task<string> VersionAsync(CancellationToken token)
        {
            var obj = await GetAsync("version", token).ConfigureAwait(false);
            return obj.ContainsKey("version") ? Convert.ToString(obj["version"]) : "unknown";
        }

        public async Task<int> MixedPortAsync(CancellationToken token)
        {
            var obj = await GetAsync("configs", token).ConfigureAwait(false);
            return obj.ContainsKey("mixed-port") ? Convert.ToInt32(obj["mixed-port"]) : 0;
        }

        public async Task<List<ProxyGroup>> GroupsAsync(bool showAll, CancellationToken token)
        {
            var result = new List<ProxyGroup>();
            var root = await GetAsync("proxies", token).ConfigureAwait(false);
            if (!root.ContainsKey("proxies")) return result;
            var groups = root["proxies"] as Dictionary<string, object>;
            if (groups == null) return result;
            foreach (var kvp in groups)
            {
                var obj = kvp.Value as Dictionary<string, object>;
                if (obj == null || !obj.ContainsKey("type")) continue;
                var type = Convert.ToString(obj["type"]);
                bool selectable = type.Equals("Selector", StringComparison.OrdinalIgnoreCase) || type.Equals("select", StringComparison.OrdinalIgnoreCase);
                bool otherGroup = type.Equals("URLTest", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("Fallback", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("LoadBalance", StringComparison.OrdinalIgnoreCase);
                if (!selectable && !(showAll && otherGroup)) continue;
                object[] members = obj.ContainsKey("all") ? obj["all"] as object[] : null;
                if (members == null) continue;
                result.Add(new ProxyGroup
                {
                    Name = kvp.Key,
                    Type = type,
                    Selected = obj.ContainsKey("now") ? Convert.ToString(obj["now"]) : "",
                    Members = Array.ConvertAll(members, x => Convert.ToString(x))
                });
            }
            return result;
        }

        public async Task SelectAsync(string group, string member, CancellationToken token)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(member)) throw new ArgumentException("请选择策略组和节点。");
            using (var body = new StringContent(_json.Serialize(new { name = member }), Encoding.UTF8, "application/json"))
            using (var response = await _http.PutAsync("proxies/" + Uri.EscapeDataString(group), body, token).ConfigureAwait(false))
                response.EnsureSuccessStatusCode();
        }

        public void Dispose() => _http.Dispose();
    }
}
