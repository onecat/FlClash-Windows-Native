using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FlClash.Windows.Config;

namespace FlClash.Windows.Core
{
    public enum CoreState { Stopped, Starting, Ready, Stopping, Failed }

    public sealed class CoreProcessManager : IDisposable
    {
        private readonly AppPaths _paths;
        private readonly AppSettings _settings;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private Process _process;
        private MihomoApiClient _api;
        private volatile bool _stopping;
        private volatile CoreState _state = CoreState.Stopped;
        public event Action<CoreState, string> Changed;
        public CoreState State => _state;
        public MihomoApiClient Api => _state == CoreState.Ready ? _api : null;
        public string Version { get; private set; } = "-";

        public CoreProcessManager(AppPaths paths, AppSettings settings)
        {
            _paths = paths;
            _settings = settings;
        }

        private void Publish(CoreState state, string detail)
        {
            _state = state;
            Changed?.Invoke(state, detail);
        }

        public async Task StartAsync(string profile, CancellationToken cancellation)
        {
            await _gate.WaitAsync(cancellation).ConfigureAwait(false);
            bool promoted = false;
            bool existedBefore = false;
            try
            {
                if (_state == CoreState.Ready) return;
                if (_process != null || _api != null) StopOwnedProcess();
                if (string.IsNullOrEmpty(profile) || !File.Exists(profile))
                    throw new FileNotFoundException("请先导入有效的 YAML 配置。", profile);
                if (!File.Exists(_paths.CoreExecutable))
                    throw new FileNotFoundException("找不到官方 Mihomo Core，请放入 core\\mihomo.exe。", _paths.CoreExecutable);

                Publish(CoreState.Starting, "正在校验配置…");
                _stopping = false;
                var secret = NewSecret();
                var yaml = ConfigPreparer.Compose(File.ReadAllText(profile, Encoding.UTF8),
                    _settings.MixedPort, _settings.ControllerPort, secret);
                var candidate = Path.Combine(_paths.Runtime, "candidate.yaml");
                File.WriteAllText(candidate, yaml, new UTF8Encoding(false));
                await ValidateAsync(candidate, cancellation).ConfigureAwait(false);
                var active = _paths.ActiveConfiguration;
                existedBefore = File.Exists(active);
                if (existedBefore) File.Replace(candidate, active, _paths.PreviousConfiguration);
                else File.Move(candidate, active);
                promoted = true;

                _api = new MihomoApiClient(_settings.ControllerPort, secret);
                _process = MakeProcess("-d " + Quote(_paths.Runtime) + " -f " + Quote(active));
                _process.EnableRaisingEvents = true;
                _process.Exited += ProcessExited;
                if (!_process.Start()) throw new InvalidOperationException("核心进程启动失败。");

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    Exception lastError = null;
                    while (!timeout.IsCancellationRequested)
                    {
                        if (_process.HasExited)
                            throw new InvalidOperationException("Mihomo 启动后意外退出，退出码：" + _process.ExitCode);
                        try
                        {
                            Version = await _api.VersionAsync(timeout.Token).ConfigureAwait(false);
                            int port = await _api.MixedPortAsync(timeout.Token).ConfigureAwait(false);
                            if (port != _settings.MixedPort)
                                throw new InvalidDataException("API 返回的监听端口与配置不一致。");
                            using (var tcp = new TcpClient())
                            {
                                var connecting = tcp.ConnectAsync("127.0.0.1", port);
                                if (await Task.WhenAny(connecting, Task.Delay(900, timeout.Token)).ConfigureAwait(false) != connecting)
                                    throw new TimeoutException("代理端口尚未准备就绪。");
                                await connecting.ConfigureAwait(false);
                            }
                            Publish(CoreState.Ready, "Mihomo " + Version);
                            return;
                        }
                        catch (Exception e) when (e is HttpRequestException || e is SocketException ||
                                                  e is TaskCanceledException || e is TimeoutException || e is InvalidDataException)
                        {
                            lastError = e;
                        }
                        await Task.Delay(300, timeout.Token).ConfigureAwait(false);
                    }
                    throw new TimeoutException("核心未在 15 秒内就绪：" + lastError?.Message);
                }
            }
            catch
            {
                StopOwnedProcess();
                if (promoted)
                {
                    // A validated candidate can still fail at runtime (ports, permissions,
                    // or external provider errors). Keep the last known-good active file.
                    try
                    {
                        if (existedBefore && File.Exists(_paths.PreviousConfiguration))
                            File.Copy(_paths.PreviousConfiguration, _paths.ActiveConfiguration, true);
                        else if (!existedBefore && File.Exists(_paths.ActiveConfiguration))
                            File.Delete(_paths.ActiveConfiguration);
                    }
                    catch (IOException) { /* preserve original startup error */ }
                    catch (UnauthorizedAccessException) { /* preserve original startup error */ }
                }
                Publish(CoreState.Failed, "核心启动失败。请检查配置和日志。");
                throw;
            }
            finally { _gate.Release(); }
        }

        private async Task ValidateAsync(string candidate, CancellationToken cancellation)
        {
            using (var p = MakeProcess("-t -d " + Quote(_paths.Runtime) + " -f " + Quote(candidate)))
            {
                p.Start();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    try
                    {
                        while (!p.WaitForExit(150)) timeout.Token.ThrowIfCancellationRequested();
                    }
                    catch { if (!p.HasExited) p.Kill(); throw; }
                }
                if (p.ExitCode != 0)
                    throw new InvalidDataException("Mihomo 配置校验失败（退出码 " + p.ExitCode + "）。保留上次有效配置。");
            }
        }

        public async Task StopAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                _stopping = true;
                Publish(CoreState.Stopping, "正在停止核心…");
                StopOwnedProcess();
                Publish(CoreState.Stopped, "核心已停止");
            }
            finally { _gate.Release(); }
        }

        private void ProcessExited(object sender, EventArgs e)
        {
            if (!_stopping && _state == CoreState.Ready)
                Publish(CoreState.Failed, "核心进程异常退出");
        }

        private void StopOwnedProcess()
        {
            _stopping = true;
            _api?.Dispose();
            _api = null;
            if (_process != null)
            {
                try
                {
                    _process.Exited -= ProcessExited;
                    if (!_process.HasExited)
                    {
                        // Only the Process object started by this instance may be terminated.
                        _process.Kill();
                        _process.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException) { }
                finally { _process.Dispose(); _process = null; }
            }
        }

        private Process MakeProcess(string args)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _paths.CoreExecutable,
                    Arguments = args,
                    WorkingDirectory = _paths.Runtime,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
        }

        private static string Quote(string path) => "\"" + path.Replace("\"", "") + "\"";
        private static string NewSecret()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        public void Dispose()
        {
            StopOwnedProcess();
            _gate.Dispose();
        }
    }
}
