# FlClash Windows 原生版：第一阶段原型

**当前是开发源码，不是编译测试通过的 Windows 安装包或正式 Release。**

## 技术路线

- Windows 10 1607+ x64 / Windows 11 x64；建议重点测试 Windows 10 22H2。
- .NET Framework 4.8 + C# + WinForms，不使用 Flutter/Electron/WebView2。
- 直接运行官方预编译 `mihomo.exe`，无需自行编译 Go 内核。
- GUI 仅通过本机鉴权 REST API/WebSocket 与 Core 通信。

## 已编写的功能

- 本地 YAML 导入、校验、受控配置生成、启动失败后旧配置回滚。
- 独立 Mihomo 进程启动、就绪检查、停止，避免按进程名误杀其他 Mihomo。
- 只显示可手动选择的 Selector 代理组，允许手动显示只读自动组。
- 节点切换、WebSocket 实时上传/下载速度。
- 托盘、随 Windows 登录启动、Core 随客户端启动、Core 就绪后启用系统代理。
- 崩溃恢复的系统代理快照；只恢复本客户端仍然持有的代理设置。
- 默认关闭启动时检查更新和自动更新。

## 暂未完成

- Windows/.NET 运行环境中的实际编译和运行测试（当前环境不可用）。
- 远程订阅下载、连接/日志页面、TUN、自动更新、内存长期压测、完整安装包。
- 对所有 Windows 代理自动检测、PAC 和每连接配置的完整恢复验证。

## Windows 编译

1. 安装 Visual Studio 2022（.NET 桌面开发）、.NET Framework 4.8 Developer Pack。
2. 在解压源码根目录打开 PowerShell，执行：

```powershell
.\scripts\Test-Prototype.ps1
```

3. 编译后将官方稳定版 Mihomo 装入 `FlClash.Windows.exe` 所在目录的 `core\mihomo.exe`：

```powershell
.\scripts\Install-OfficialMihomo.ps1
```

该脚本固定使用 `v1.19.33`，通过官方 GitHub Release API 取得资产 SHA-256 并校验下载包；如果无法验证，直接停止，不安装未知二进制文件。

4. 启动 `src\FlClash.Windows\bin\x64\Release\net48\FlClash.Windows.exe`，导入**可信**本地 YAML。

## GitHub

原 `onecat/FlClash` Fork 没有修改。由于当前连接的 GitHub 操作不支持创建全新仓库，因此先交付独立工程 ZIP。创建独立仓库后可上传此源码，Windows GitHub Actions 会执行 Restore / Build / Smoke Test，并生成包含已验证官方 Core 的原型 ZIP。

完整实现计划见 `docs/implementation-plan.md`，实际进度和验收缺口见 `docs/STATUS.md`。
