using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace FlClash.Windows.Core
{
    public sealed class TrafficSample
    {
        public long Up { get; set; }
        public long Down { get; set; }
    }

    public sealed class TrafficMonitor : IDisposable
    {
        private CancellationTokenSource _stop;
        private Task _worker;
        public event Action<TrafficSample> Received;

        public void Start(MihomoApiClient api)
        {
            Stop();
            _stop = new CancellationTokenSource();
            _worker = ListenAsync(api, _stop.Token);
        }

        private async Task ListenAsync(MihomoApiClient api, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using (var socket = new ClientWebSocket())
                    {
                        socket.Options.SetRequestHeader("Authorization", "Bearer " + api.Token);
                        await socket.ConnectAsync(new Uri(api.WebSocketBase, "traffic"), cancellation).ConfigureAwait(false);
                        var bytes = new byte[8192];
                        var json = new JavaScriptSerializer();
                        while (socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
                        {
                            using (var buffer = new MemoryStream())
                            {
                                WebSocketReceiveResult read;
                                do
                                {
                                    read = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), cancellation).ConfigureAwait(false);
                                    if (read.MessageType == WebSocketMessageType.Close) return;
                                    if (buffer.Length + read.Count > 64 * 1024) throw new InvalidDataException("流量消息过大。");
                                    buffer.Write(bytes, 0, read.Count);
                                } while (!read.EndOfMessage);
                                var obj = json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(buffer.ToArray()));
                                if (obj != null)
                                    Received?.Invoke(new TrafficSample
                                    {
                                        Up = obj.ContainsKey("up") ? Convert.ToInt64(obj["up"]) : 0,
                                        Down = obj.ContainsKey("down") ? Convert.ToInt64(obj["down"]) : 0
                                    });
                            }
                        }
                    }
                }
                catch (Exception e) when (e is WebSocketException || e is IOException || e is InvalidDataException || e is TaskCanceledException || e is ObjectDisposedException)
                {
                    if (cancellation.IsCancellationRequested) break;
                }
                try { await Task.Delay(1500, cancellation).ConfigureAwait(false); }
                catch (TaskCanceledException) { break; }
            }
        }

        public void Stop()
        {
            if (_stop == null) return;
            var previous = _stop;
            var worker = _worker;
            _stop = null;
            _worker = null;
            previous.Cancel();
            // Do not synchronously wait for networking on the UI thread.
            if (worker == null) previous.Dispose();
            else worker.ContinueWith(t => previous.Dispose(), TaskScheduler.Default);
        }

        public void Dispose() => Stop();
    }
}
