# FlClash Windows 原生客户端重写实施方案

> 方案版本：1.0  |  日期：2026-10-26  |  状态：待实施
>
> 定位：Windows 10 为主要运行平台、兼容 Windows 11；C# + .NET Framework 4.8 + WinForms；官方 Mihomo 二进制独立进程。此文档是开发基线，不代表已创建仓库、已编译或已通过验收。

## 1. 目标与边界

### 1.1 目标

1. 将现有 FlClash Flutter Windows 客户端替换为轻量原生 Windows 客户端；保留日常使用习惯，但不逐像素复刻。
2. 使用官方预编译 Mihomo Core，不修改、不重新编译其 Go 源码；界面和核心分别更新与诊断。
3. 目标系统为 Windows 10/11 x64；Windows 10 22H2 为主验收环境。
4. 降低图形端内存和 CPU 占用，长期运行不出现持续增长，并保证故障可恢复。
5. 首次有效配置启动后启用系统代理；按用户已确定需求，默认开启开机启动与核心自启动，且均可手动关闭。

### 1.2 兼容边界

- 最低技术兼容：Windows 10 **1607 x64** 且已安装 .NET Framework 4.8；Windows 10 1507/1511 不纳入支持范围，因为它们不能安装 Framework 4.8。
- Windows 10 **1903+** 默认内置 .NET Framework 4.8；Windows 10 1607–1809 需依赖检查和引导安装。
- 核心验收矩阵：Windows 10 22H2 x64、Windows 11 x64；1607–1809 列为兼容测试，未实测不得声称全功能保证。
- Windows 10 已结束常规安全支持，告知用户优先安装适用的延长安全更新（ESU）或迁移到受支持系统。Windows 7/8/8.1 和 32 位系统不支持。

### 1.3 非目标

- 不再构建 Android/macOS/Linux。
- 不克隆 Flutter 的所有高级配置编辑和装饰性动画；不集成 WebView2/Electron。
- 不自行实现代理协议，不编译或 fork Mihomo Core。
- V1 不承诺 WebDAV、脚本覆写或对旧版本配置数据库进行无损双向编辑；该类功能在阶段验收后按优先级加入。

## 2. 技术选型

| 模块 | 选择 | 理由 |
|---|---|---|
| 开发语言/运行时 | C# / .NET Framework 4.8 | Windows 10 主导、较小部署依赖 |
| GUI | WinForms + 原生 GDI/GDI+ 绘制的少量自定义控件 | 避免嵌入浏览器内核；注意高 DPI/字体缩放 |
| 架构 | MVP/MVVM-lite；单一 UI 线程 + 异步后台服务 | 易于测试、避免 UI 与网络逻辑耦合 |
| Core | MetaCubeX/mihomo 官方 Windows amd64 预编译包 | 不需要 Go SDK；锁定版本和 SHA256 |
| IPC | loopback HTTP REST + WebSocket，Bearer secret | 使用官方 API，避免重新维护私有 IPC |
| JSON | Newtonsoft.Json 或等价经验证的 Framework 4.8 兼容库 | 兼容性、稳定性 |
| YAML | YamlDotNet 的兼容版本 | 用于配置组装与保留 YAML 结构 |
| 数据 | JSON 设置 + YAML 订阅/生成配置 | V1 不引入 SQLite，复杂历史/筛选数据需要时再评估 |
| 机密 | Windows DPAPI + 当前用户 ACL | 保护订阅链接、API 密钥等；明文临时 YAML 需受限权限 |
| 部署 | Portable ZIP 优先；安装包后续使用 Inno Setup 或同类工具 | 便携包无需安装器，Framework 缺失需提示 |
| CI | GitHub Actions Windows Runner + MSBuild/.NET Framework targeting pack | 编译、测试、打包、签名/校验与 Release |

## 3. 进程与模块架构

```
FlClash.WinForms.exe (普通用户权限，关闭窗口可驻留托盘)
 ├─ Presentation: Dashboard / Proxies / Profiles / Connections / Logs / Settings
 ├─ Application: StartupCoordinator / ProxySelection / ProfileOrchestrator
 ├─ Infrastructure.Mihomo: CoreProcessManager + HttpApiClient + WebSocketStreams
 ├─ Infrastructure.Windows: SystemProxyManager + StartupManager + Tray + OSInfo
 ├─ Infrastructure.Config: SubscriptionService + ConfigMerger + Validator + Backup
 └─ Diagnostics: CrashRecovery + StructuredLogging + PerformanceCounters
      ⇅ 127.0.0.1:<controllerPort> + Bearer secret
Mihomo.exe (官方预编译核心，优先非管理员权限运行)
 ├─ 代理引擎、DNS、规则、Provider、TUN 等
 └─ Core 数据目录 (%LocalAppData%/FlClashWindows/core-data/)

[后续 TUN 阶段] 受限且可审计的 Windows 辅助服务
 └─ 仅提供必要的特权网络操作，不暴露任意命令/任意路径执行
```

### 3.1 运行时约束

- 普通客户端进程**不得全程提权**。普通系统代理模式无需管理员权限。
- 单实例 Mutex + 激活现有窗口；同一时刻只能存在一个本客户端负责的 Core 实例；不得误关用户自行启动的 mihomo.exe。
- Core 生命周期必须有唯一 owner：Starting → Ready → Stopping → Stopped / Failed；重启去重，处理 PID、配置路径、API 健康检查、异常退出和停止超时。
- 主窗口关闭默认最小化到托盘；明确选择“退出”时，按配置停止托管 Core 并恢复本客户端拥有的系统代理设置。
- 保留前一份正确配置；候选配置通过解析和 Core 校验后原子替换；应用失败自动回滚。

## 4. 系统代理、启动顺序和核心管理

1. 第一次打开：创建用户数据目录，检查 Core 文件及签名/哈希，检查 Framework 与系统支持；不强迫用户导入前就开启失效代理。
2. 默认设置：`launchAtLogin=true`、`startCoreWithApp=true`、`enableSystemProxyWhenReady=true`、`theme=system`、`checkUpdatesAtStartup=false`、`autoCheckUpdates=false`。
3. 导入或选择可用配置后：生成候选 YAML，校验，无效配置不覆盖当前文件；启动 Mihomo 并等待 `/version` 等健康检查成功。
4. 核心 Ready 且监听端口确认后，再应用系统代理；Windows 代理设置由 WinINet/注册表按实际需要维护并通知系统刷新，明确与 WinHTTP 设置不同。
5. 正常退出：先解除**自己设置**的代理、停止托管 Core，然后释放锁及托盘；清理失败需记录，重启时识别残留并恢复。
6. 下次登录：后台静默启动托盘客户端 → 启动 Core → 检查 API/配置 → 按设置启用系统代理；失败时保持直连并提醒，绝不指向未启动的代理端口。
7. 用户或其他程序期间修改了系统代理时，不强行覆盖用户的新配置；仅在可确认当前状态由客户端持有时才还原，避免破坏其他 VPN/代理应用。

## 5. Mihomo Core 和安全

- 从官方 stable Release 下载 amd64 兼容构建，固定版本、文件名、来源 URL、SHA256；解包后校验。升级采用“下载到临时目录 → 哈希校验 → 备份旧版 → 退出旧进程 → 原子替换 → 启动与自检 → 失败回滚”。
- 配置下发坚持生成最终 YAML，而非让订阅内容接管客户端管理的控制面字段。对 `external-controller`、`secret`、监听地址、TUN 管理项进行最后覆写，并对危险的额外监听项提供隔离/提示。
- External Controller 只监听 `127.0.0.1`，每次实例使用高强度随机 secret；不启用无鉴权的 pipe/unix controller，不使用 `0.0.0.0`，不配置任意 CORS 放行。
- Token 受限保存；日志不记录订阅 URL 查询参数、认证头、用户/密码等。阻止同账户外访问并限制可写目录；需承认同一 Windows 用户下恶意程序仍可能读取用户进程数据。
- `HttpClient` 可复用，REST 调用配置超时和取消令牌；WebSocket 自动重连、指数退避、取消清理；关闭窗口/后台时暂停高频无必要订阅。
- `CoreProcessManager` 不以进程名作为删除或杀进程依据；持有明确 PID/句柄和启动会话标记。

## 6. 界面与功能清单

### P0：首个可日常使用版本（必须）

- 仪表盘：当前核心状态、系统代理状态、当前配置、当前模式、上传下载速率。
- 代理：手动可选策略组、节点切换、延迟检测；默认隐藏不可手动选择的自动测试/故障转移/负载均衡组，但保留它们作为可选节点、策略链路和调试信息，提供“显示全部组”开关。
- 配置：订阅 URL/本地 YAML 导入、手动更新、多个配置切换、更新失败回滚、配置预检。
- 系统：系统代理开关、登录自启、核心随客户端启动、最小化托盘、托盘快捷模式切换、浅色/深色/系统主题。
- 诊断：Core 版本、基础日志、连接列表、手动重启、错误提示、日志导出（脱敏）。
- 更新：默认不启动检查、不自动检查；手动检查客户端新仓库 Releases，Core 升级独立按官方 Release 验证。

### P1：正式稳定版本

- 完整连接统计、规则/Provider 查看更新、过滤和排序、订阅计划更新、系统网络/休眠恢复、代理冲突检测。
- TUN 模式 + 按需提权/受限辅助服务 + 驱动与路由恢复检查，失败能回退到系统代理模式。
- 旧 FlClash 配置**一次性只读导入**：映射订阅、基础代理偏好、开关；对无法迁移字段列出报告，不覆盖原数据。
- 自动化功能测试、回滚与长时间稳定性压测。

### P2：可选增强

- 高级 YAML 覆写编辑、复杂规则和脚本兼容、WebDAV 备份、更多主题细节、附加快捷键。
- 不因装饰性 UI 增加显著后台 CPU/内存。

## 7. 核心 API 映射（以官方文档及锁定的 Release 为准）

| 功能 | 官方 API |
|---|---|
| Core 版本和存活 | `GET /version` |
| 读取、热更新/重载配置 | `GET /configs`、`PATCH /configs`、`PUT /configs?force=true` |
| 查询/切换节点 | `GET /proxies`、`PUT /proxies/{name}` |
| 测试延迟 | `GET /proxies/{name}/delay` |
| 查看/更新代理提供者 | `GET /providers/proxies`、`PUT /providers/proxies/{name}` |
| 实时流量 | WebSocket `/traffic` |
| 实时连接 | WebSocket `/connections`；按 ID 关闭使用 `DELETE /connections/{id}` |
| 实时日志 | WebSocket `/logs` |
| 当前内存 | WebSocket `/memory`（Core 提供的数值，与 OS 工作集不同） |

协议的异常码、特殊名称 URL 编码、配置切换的副作用、API 版本兼容均应编写契约测试。

## 8. 实施步骤与阶段门

### 阶段 0：基线和风险核查

- 留存当前 Flutter 在同一 Win10/Win11 环境的启动、10 分钟空闲、4/8/24 小时工作集、Private Bytes、CPU、句柄和 Core 单独数据。
- 核验项目许可证、官方 Mihomo stable SHA256/依赖、当前 FlClash 的配置字段与功能列表；建立可追踪验收矩阵。
- **通过条件：** 明确 P0 范围、Core 可以在 Win10 22H2 独立启动和 API 响应。

### 阶段 1：底层原型

- 创建独立仓库、sln、CI；实现 CoreProcessManager、API Client、极简 UI 和托盘。
- 完成导入 YAML、启动 Core、切换 Selector、实时流量、开启/关闭系统代理。
- **通过条件：** Win10 22H2 和 Win11 可启动；核心、UI 进程职责清晰；首次功能演示和基础性能数据齐备。失败则先查问题，不扩大范围。

### 阶段 2：日常功能 P0

- 订阅管理/配置合并与回滚、策略组过滤、节点延迟、连接与日志、设置持久化、登录自启和主题。
- **通过条件：** P0 行为测试全部通过；无配置、网络断开、Core 崩溃、端口冲突均可恢复。

### 阶段 3：可靠性与打包

- 三种代理互操作测试（本客户端、系统手动代理、其他 VPN/代理软件）；最小化、锁屏、休眠/唤醒、网络变更、异常退出；便携 ZIP 和安装包。
- **通过条件：** CI、集成测试、8/24/72 小时压力测试通过；发布包和升级/回滚演练完成。

### 阶段 4：TUN 与高级兼容 P1

- 设计 Windows helper 权限边界；实现 TUN + 路由与 DNS 恢复；配置导入兼容；安全审查。
- **通过条件：** 管理员/普通用户、启停、异常退出和驱动缺失情况下，系统网络可恢复；不引入任意提权命令能力。

### 阶段 5：发布

- 正式 Release：`Windows-x64-Portable.zip` 和 `Windows-x64-Setup.exe`；用户手动检查更新跳转项目自有 GitHub Releases。
- 保留 Flutter Fork；独立版本号和变更日志，不沿用无法对应的 Flutter 版本编号。

## 9. 性能与稳定性验收

| 指标 | 验收预算 / 测试要求 |
|---|---|
| GUI 工作集：启动并空闲 10 分钟 | 目标 ≤120 MiB；理想 ≤80 MiB |
| GUI 工作集：日常运行 24 小时 | 目标 ≤150 MiB；不得单调持续增长 |
| GUI 空闲 CPU | 持续平均 <0.5%（在规定测试机上测量） |
| 长时间观察 | 8、24、72 小时记录 GUI/Core 分别的 WS、Private Bytes、句柄、线程和 CPU |
| Core 崩溃后 | 告知状态，按照受限重试/退避策略恢复；失败解除本客户端设置的失效系统代理 |
| 配置损坏/订阅故障 | 旧版可用配置继续工作，不使网络转入错误代理状态 |
| 启动/退出 | 无残留本客户端托管的 Core，无无意遗留系统代理设置 |
| Win10/Win11 兼容 | 主要路径在两套 OS 实测，不把 Win11 通过等同于 Win10 通过 |

上述内存数字是**目标预算，不是实测保证**，也不包含独立 Mihomo Core 的内存；关注 GUI + Core 的整体系统成本。测试须使用统一配置、节点数、连接量、窗口状态、采样口径和硬件。

## 10. Review 与测试策略

- 单元测试：配置合并与非法 YAML；API JSON 容错；策略组识别；首启默认设置；URL/令牌脱敏；异常重试；系统代理快照比对。
- 集成测试：真实官方 Core 启停、API 兼容、端口冲突、订阅更新失败和回滚、网络切换。
- 平台测试：Windows 10 22H2、Windows 11；老 Win10 环境需补齐 Framework 检查与安装体验。
- 安全测试：无密钥控制器、绑定到公网、危险配置注入、路径穿越、同名 PID 误杀、权限边界、更新包哈希验证。
- 每阶段先 Review 再构建/发布；Review 覆盖编译、空引用、异常、生命周期、权限、资源泄漏、并发/死锁、协议兼容与误删系统设置。
- 所有阻断性问题在阶段内修复并复测；验收失败不发布为正式版本。

## 11. 依赖/许可证与更新策略

- 当前 FlClash Fork `onecat/FlClash` 保留不动；重写单独建仓库，迁移/复用 FlClash GPL-3.0 代码时遵守许可证义务。
- `mihomo.exe` 的发行与第三方许可单独核验，保留相应许可与来源说明。
- 客户端：启动时不检查、不开启定期检查；用户手动检查时查询**新客户端仓库**的 Releases（仓库创建前不写死未存在的链接）。
- Core：按需手动检查官方 stable，不后台强制升级；每个版本记录源 Release、哈希、回滚点和兼容 API 测试结果。
- CLI/开发需要 .NET Framework 4.8 Developer Pack + VS Build Tools/MSBuild；构建 Windows 客户端**不需要 Go 或 Rust**；Portable 不依赖 Inno Setup。

## 12. 暂定里程碑和开发优先级

- M0：可测基线与 Core API 验证。
- M1：可运行原型：WinForms + Core + 系统代理 + 托盘。
- M2：P0 可日常使用：订阅、节点切换、流量、连接、设置、恢复。
- M3：可靠版：性能和兼容矩阵 + CI + Portable / Installer。
- M4：TUN + 旧配置迁移 + 高级能力。

单人工作量参考：M0–M1 约 1–2 周；M2–M3 累计约 3–6 周；含 M4 的完整版本可能需累计 2–4 个月。具体投入以实际代码/测试复杂度复估。

## 13. 参考资料

- Microsoft: https://learn.microsoft.com/dotnet/framework/get-started/system-requirements
- Microsoft: https://learn.microsoft.com/en-us/dotnet/framework/install/on-server-2019
- Mihomo 官方 API: https://wiki.metacubex.one/en/api/
- Mihomo 全局配置: https://wiki.metacubex.one/en/config/general/
- Mihomo TUN: https://wiki.metacubex.one/en/config/inbound/tun/
- 官方 Core Release: https://github.com/MetaCubeX/mihomo/releases
- FlClash Fork: https://github.com/onecat/FlClash
- FlClash upstream architecture: https://github.com/chen08209/FlClash/blob/main/.agents/architecture.md
