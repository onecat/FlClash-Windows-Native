using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlClash.Windows.Config;
using FlClash.Windows.Core;
using FlClash.Windows.Windows;

namespace FlClash.Windows.UI
{
    public sealed class MainForm : Form
    {
        private readonly AppPaths _paths;
        private readonly AppSettings _settings;
        private readonly CoreProcessManager _core;
        private readonly SystemProxyManager _proxy;
        private readonly TrafficMonitor _traffic = new TrafficMonitor();
        private readonly bool _background;
        private readonly NotifyIcon _tray;
        private readonly Label _state = new Label();
        private readonly Label _profile = new Label();
        private readonly Label _rate = new Label();
        private readonly Label _proxyState = new Label();
        private readonly ComboBox _groups = new ComboBox();
        private readonly ListBox _nodes = new ListBox();
        private readonly Button _start = new Button();
        private readonly Button _stop = new Button();
        private readonly Button _toggleProxy = new Button();
        private readonly CheckBox _launch = new CheckBox();
        private readonly CheckBox _startWithApp = new CheckBox();
        private readonly CheckBox _proxyOnReady = new CheckBox();
        private readonly CheckBox _showAll = new CheckBox();
        private List<ProxyGroup> _availableGroups = new List<ProxyGroup>();
        private bool _exitRequested;
        private bool _busy;

        public MainForm(bool background)
        {
            _background = background;
            Text = "FlClash Windows";
            MinimumSize = new Size(690, 510);
            Size = new Size(790, 590);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            Icon = SystemIcons.Application;
            _paths = AppPaths.ForCurrentUser();
            try { _settings = AppSettings.Load(_paths.SettingsFile); }
            catch (Exception e)
            {
                _settings = new AppSettings();
                MessageBox.Show("设置加载失败，已采用默认值：" + e.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _core = new CoreProcessManager(_paths, _settings);
            _proxy = new SystemProxyManager(_paths);
            _core.Changed += OnCoreChanged;
            _traffic.Received += sample => SafeUi(() => _rate.Text =
                "上传 " + Speed(sample.Up) + "  ·  下载 " + Speed(sample.Down));

            _tray = new NotifyIcon { Text = "FlClash Windows", Icon = SystemIcons.Application, Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (s, e) => ShowWindow());
            menu.Items.Add("启动核心", null, async (s, e) => await StartCoreAction());
            menu.Items.Add("停止核心", null, async (s, e) => await StopCoreAction());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, async (s, e) => await ExitAction());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => ShowWindow();

            BuildUi();
            FormClosing += OnFormClosing;
            Shown += async (s, e) => await InitializeAsync();
        }

        private void BuildUi()
        {
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var overview = new TabPage("概览") { Padding = new Padding(24) };
            var proxies = new TabPage("代理组") { Padding = new Padding(24) };
            var settings = new TabPage("设置") { Padding = new Padding(24) };
            tabs.TabPages.AddRange(new[] { overview, proxies, settings });
            Controls.Add(tabs);

            var header = new Label
            {
                Text = "FlClash  ·  Windows 原生原型",
                Font = new Font(Font.FontFamily, 17, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 52
            };
            overview.Controls.Add(header);
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 62, 0, 0)
            };
            overview.Controls.Add(panel);
            _state.Text = "核心：未启动";
            _profile.Text = "配置：未选择";
            _rate.Text = "上传 0 B/s  ·  下载 0 B/s";
            _proxyState.Text = "系统代理：未接管";
            foreach (var label in new[] { _state, _profile, _rate, _proxyState })
            {
                label.AutoSize = false;
                label.Width = 680;
                label.Height = 37;
                label.TextAlign = ContentAlignment.MiddleLeft;
                panel.Controls.Add(label);
            }
            var actions = new FlowLayoutPanel { Width = 680, Height = 48 };
            var import = NewButton("导入本地 YAML", 145, async (s, e) => await ImportAction());
            _start.Text = "启动核心";
            _start.Width = 115;
            _start.Height = 34;
            _start.Click += async (s, e) => await StartCoreAction();
            _stop.Text = "停止核心";
            _stop.Width = 115;
            _stop.Height = 34;
            _stop.Click += async (s, e) => await StopCoreAction();
            _toggleProxy.Text = "切换系统代理";
            _toggleProxy.Width = 145;
            _toggleProxy.Height = 34;
            _toggleProxy.Click += async (s, e) => await ToggleProxyAction();
            actions.Controls.AddRange(new Control[] { import, _start, _stop, _toggleProxy });
            panel.Controls.Add(actions);
            panel.Controls.Add(new Label
            {
                Width = 660,
                Height = 80,
                Text = "第一阶段功能：官方 Core、YAML、代理选择、实时流量与系统代理。" + Environment.NewLine +
                       "TUN、远程订阅和自动更新将在后续阶段实现。",
                ForeColor = Color.DimGray
            });

            var groupLabel = new Label { Text = "策略组", Dock = DockStyle.Top, Height = 28 };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48 };
            _groups.Width = 360;
            _groups.DropDownStyle = ComboBoxStyle.DropDownList;
            _groups.SelectedIndexChanged += (s, e) => FillMembers();
            bar.Controls.Add(_groups);
            bar.Controls.Add(NewButton("刷新", 90, async (s, e) => await RefreshGroupsAsync()));
            _nodes.Dock = DockStyle.Fill;
            _nodes.IntegralHeight = false;
            _nodes.Font = new Font(Font.FontFamily, 10);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 53 };
            bottom.Controls.Add(NewButton("应用所选节点", 145, async (s, e) => await ApplyNodeAction()));
            proxies.Controls.Add(_nodes);
            proxies.Controls.Add(bottom);
            proxies.Controls.Add(bar);
            proxies.Controls.Add(groupLabel);

            var options = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 8, 0, 0)
            };
            settings.Controls.Add(options);
            _launch.Text = "登录 Windows 后启动客户端（后台托盘）";
            _startWithApp.Text = "客户端启动时自动启动 Mihomo Core";
            _proxyOnReady.Text = "Core 就绪后自动设置系统代理";
            _showAll.Text = "显示所有策略组（自动组只读）";
            _launch.Checked = _settings.LaunchAtLogin;
            _startWithApp.Checked = _settings.StartCoreWithApp;
            _proxyOnReady.Checked = _settings.EnableSystemProxyWhenReady;
            _showAll.Checked = _settings.ShowAllGroups;
            foreach (var option in new[] { _launch, _startWithApp, _proxyOnReady, _showAll })
            {
                option.AutoSize = false;
                option.Width = 550;
                option.Height = 42;
                options.Controls.Add(option);
            }
            _launch.CheckedChanged += (s, e) =>
            {
                try { LoginStartupManager.Set(_launch.Checked); _settings.LaunchAtLogin = _launch.Checked; SaveSettings(); }
                catch (Exception ex) { ShowFailure(ex); }
            };
            _startWithApp.CheckedChanged += (s, e) => { _settings.StartCoreWithApp = _startWithApp.Checked; SaveSettings(); };
            _proxyOnReady.CheckedChanged += (s, e) => { _settings.EnableSystemProxyWhenReady = _proxyOnReady.Checked; SaveSettings(); };
            _showAll.CheckedChanged += async (s, e) =>
            {
                _settings.ShowAllGroups = _showAll.Checked;
                SaveSettings();
                await RefreshGroupsAsync();
            };
            options.Controls.Add(new Label { Width = 600, Height = 80,
                Text = "更新策略：不在启动时检查，也不自动检查。" + Environment.NewLine +
                       "运行环境：.NET Framework 4.8，Windows 10/11 x64。", ForeColor = Color.DimGray });
            options.Controls.Add(NewButton("清除失效的代理恢复记录", 210, (s, e) =>
            {
                try { _proxy.ForgetForeignLease(); UpdateProxyStatus(); }
                catch (Exception ex) { ShowFailure(ex); }
            }));
        }

        private static Button NewButton(string title, int width, EventHandler onClick)
        {
            var button = new Button { Text = title, Width = width, Height = 34, Margin = new Padding(0, 0, 10, 0) };
            button.Click += onClick;
            return button;
        }

        private async Task InitializeAsync()
        {
            try
            {
                if (_background) { ShowInTaskbar = false; Hide(); }
                LoginStartupManager.Set(_settings.LaunchAtLogin);
                if (_proxy.RestoreIfOwned()) _state.Text = "已恢复上次异常关闭时的系统代理。";
                UpdateProxyStatus();
                UpdateProfileLabel();
                if (_settings.StartCoreWithApp && File.Exists(_settings.ProfilePath ?? ""))
                    await StartCoreAction();
            }
            catch (Exception e) { ShowFailure(e); }
        }

        private async Task ImportAction()
        {
            using (var dialog = new OpenFileDialog { Filter = "Mihomo YAML (*.yaml;*.yml)|*.yaml;*.yml|所有文件 (*.*)|*.*", Title = "选择配置文件" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string name = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".yaml";
                    string stored = Path.Combine(_paths.Profiles, name);
                    File.Copy(dialog.FileName, stored);
                    // Preserve old profile until the new candidate has passed core validation.
                    string previous = _settings.ProfilePath;
                    bool wasRunning = _core.State == CoreState.Ready;
                    if (wasRunning) await StopCoreAction();
                    if (wasRunning && _core.State != CoreState.Stopped)
                        throw new InvalidOperationException("旧核心未成功停止，不能切换配置。");
                    _settings.ProfilePath = stored;
                    await StartCoreAction();
                    if (_core.State != CoreState.Ready)
                    {
                        _settings.ProfilePath = previous;
                        if (wasRunning && File.Exists(previous ?? "")) await StartCoreAction();
                        throw new InvalidOperationException("新配置未能启动。已恢复旧配置路径。");
                    }
                    SaveSettings();
                    UpdateProfileLabel();
                }
                catch (Exception e) { ShowFailure(e); }
            }
        }

        private async Task StartCoreAction()
        {
            if (_busy) return;
            _busy = true;
            UpdateButtons();
            try
            {
                await _core.StartAsync(_settings.ProfilePath, CancellationToken.None);
                _traffic.Start(_core.Api);
                if (_settings.EnableSystemProxyWhenReady)
                {
                    try { _proxy.Enable(_settings.MixedPort); }
                    catch (Exception e) { ShowFailure(e); }
                }
                UpdateProxyStatus();
                await RefreshGroupsAsync();
            }
            catch (Exception e)
            {
                _traffic.Stop();
                ShowFailure(e);
            }
            finally { _busy = false; UpdateButtons(); }
        }

        private async Task StopCoreAction()
        {
            if (_busy) return;
            _busy = true;
            UpdateButtons();
            try
            {
                _traffic.Stop();
                _proxy.RestoreIfOwned();
                await _core.StopAsync();
                _rate.Text = "上传 0 B/s  ·  下载 0 B/s";
                UpdateProxyStatus();
                _nodes.Items.Clear();
                _groups.Items.Clear();
            }
            catch (Exception e) { ShowFailure(e); }
            finally { _busy = false; UpdateButtons(); }
        }

        private async Task ToggleProxyAction()
        {
            try
            {
                if (_proxy.OwnsCurrent) _proxy.RestoreIfOwned();
                else
                {
                    if (_core.State != CoreState.Ready) throw new InvalidOperationException("核心未就绪，不能设置系统代理。");
                    _proxy.Enable(_settings.MixedPort);
                }
                UpdateProxyStatus();
            }
            catch (Exception e) { ShowFailure(e); }
            await Task.CompletedTask;
        }

        private async Task RefreshGroupsAsync()
        {
            if (_core.State != CoreState.Ready || _core.Api == null) return;
            try
            {
                var groups = await _core.Api.GroupsAsync(_settings.ShowAllGroups, CancellationToken.None);
                _availableGroups = groups;
                _groups.BeginUpdate();
                _groups.Items.Clear();
                foreach (var group in groups) _groups.Items.Add(group);
                _groups.EndUpdate();
                if (_groups.Items.Count > 0) _groups.SelectedIndex = 0;
            }
            catch (Exception e) { ShowFailure(e); }
        }

        private void FillMembers()
        {
            _nodes.Items.Clear();
            var group = _groups.SelectedItem as ProxyGroup;
            if (group == null) return;
            foreach (var member in group.Members) _nodes.Items.Add(member);
            int chosen = Array.IndexOf(group.Members, group.Selected);
            if (chosen >= 0) _nodes.SelectedIndex = chosen;
        }

        private async Task ApplyNodeAction()
        {
            var group = _groups.SelectedItem as ProxyGroup;
            var node = _nodes.SelectedItem as string;
            if (group == null || node == null) return;
            if (!group.Type.Equals("Selector", StringComparison.OrdinalIgnoreCase) &&
                !group.Type.Equals("select", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("此策略组由内核自动选择，不能手动切换。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                await _core.Api.SelectAsync(group.Name, node, CancellationToken.None);
                await RefreshGroupsAsync();
            }
            catch (Exception e) { ShowFailure(e); }
        }

        private void OnCoreChanged(CoreState state, string message)
        {
            SafeUi(() =>
            {
                _state.Text = "核心：" + state + "  ·  " + message;
                if (state == CoreState.Failed)
                {
                    _traffic.Stop();
                    try { _proxy.RestoreIfOwned(); } catch (Exception e) { ShowFailure(e); }
                    UpdateProxyStatus();
                }
                UpdateButtons();
            });
        }

        private void SafeUi(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)(() => { if (!IsDisposed) action(); })); }
            catch (InvalidOperationException) { }
            catch (ObjectDisposedException) { }
        }

        private void UpdateButtons()
        {
            _start.Enabled = !_busy && _core.State != CoreState.Ready;
            _stop.Enabled = !_busy && _core.State == CoreState.Ready;
            _toggleProxy.Enabled = !_busy;
        }

        private void UpdateProxyStatus()
        {
            _proxyState.Text = _proxy.OwnsCurrent ? "系统代理：已由本客户端启用" : "系统代理：未由本客户端接管";
            _toggleProxy.Text = _proxy.OwnsCurrent ? "恢复系统代理" : "启用系统代理";
        }

        private void UpdateProfileLabel() => _profile.Text = "配置：" +
            (string.IsNullOrEmpty(_settings.ProfilePath) ? "未选择" : Path.GetFileName(_settings.ProfilePath));
        private void SaveSettings() => _settings.Save(_paths.SettingsFile);
        private void ShowFailure(Exception e) =>
            MessageBox.Show(e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        private static string Speed(long value) => value >= 1024 * 1024 ?
            (value / 1048576d).ToString("0.0") + " MiB/s" :
            value >= 1024 ? (value / 1024d).ToString("0.0") + " KiB/s" : value + " B/s";

        private void ShowWindow()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_exitRequested) return;
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
        }

        private async Task ExitAction()
        {
            if (_exitRequested) return;
            _exitRequested = true;
            try
            {
                _traffic.Stop();
                _proxy.RestoreIfOwned();
                await _core.StopAsync();
            }
            catch (Exception e)
            {
                _exitRequested = false;
                ShowFailure(e);
                return;
            }
            _tray.Visible = false;
            _tray.Dispose();
            _traffic.Dispose();
            _core.Dispose();
            Close();
        }
    }
}
