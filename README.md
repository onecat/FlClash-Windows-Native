# FlClash Windows Native (development prototype)

**Status: Stage 1 prototype; not a published or Windows-tested release.**

A Windows 10/11 x64 client written in C# / .NET Framework 4.8 / WinForms. Uses a separate **official, unmodified Mihomo** executable. This is an original Windows-native project and does not reuse code from the existing FlClash Flutter project.

## Implemented source (Stage 1)

- Load a local YAML profile; compose controlled loopback-only ports and a new controller secret.
- Reject invalid root YAML, strip alternate inbound/controller/TUN settings, test candidate using official `mihomo -t`, backup active config and launch an owned `mihomo.exe` process.
- Wait for `/version`, check `/configs` and TCP port readiness before declaring the core ready.
- Show manual Selector policy groups, switch selected node through REST; optional read-only automatic groups.
- Read real-time upload/download from the `/traffic` WebSocket.
- Set per-user Windows system proxy **only when Core is ready**, preserve prior settings in a lease, conditionally restore on normal exit and after a crash/relaunch.
- System tray and start-with-Windows defaults (`--background`), with no update checks at startup.

### Explicit limitations

This build has **no TUN**, subscription-URL downloads, automatic updates, connection/log panels, privileged helper, core crash auto-restart or installer. Proxy state is handled through current-user Internet Settings and WinINet refresh, not WinHTTP. Some Windows per-connection auto-detection settings are not yet managed. Do not use as a production VPN kill-switch.

`-t` can itself resolve or download required rule/geo-provider data. The test does not provide a secure sandbox for an untrusted local YAML file. Only import configurations you trust; additional configuration hardening is required before general release.

## Build (Windows only)

1. Windows 10 1607+ x64 with .NET Framework 4.8 Runtime; for development install Visual Studio 2022/.NET desktop tools and **.NET Framework 4.8 Developer Pack**.
2. Install Microsoft .NET SDK compatible with net48 Windows desktop targeting.
3. Restore and build:

```powershell
dotnet restore FlClashWindowsNative.sln
dotnet build FlClashWindowsNative.sln -c Release -p:Platform=x64 --no-restore
.\tests\FlClash.Windows.SmokeTests\bin\x64\Release\net48\FlClash.Windows.SmokeTests.exe
```

4. Run `scripts\Install-OfficialMihomo.ps1` to download the pinned official stable `v1.19.33` binary, verify the asset SHA-256 from GitHub API, and extract as `core\mihomo.exe` **beside** `FlClash.Windows.exe`. You may alternatively place an already verified official binary there manually. This is a one-time manual installation, not a runtime update check.
5. Open `FlClash.Windows.exe`, import a *trusted* local Mihomo `.yaml` or `.yml` configuration. Until an effective profile is available, system proxy is never enabled.

App data: `%LocalAppData%\FlClashWindows\` (not next to portable executable). This preserves writable profile/runtime files when installed under `Program Files`.

## Security rules

- Controller binds to `127.0.0.1`, requires a random 256-bit Bearer secret, and is recreated for each start.
- Does not use untrusted named-pipe controller; disables additional listeners and TUN in the prototype.
- No wildcard controllers. Network access to the controller still cannot protect against malicious processes running as the same Windows user.
- Stops **only** the owned Core process object, never kills all processes named `mihomo`.
- The system proxy manager restores only if current settings still match this app's managed endpoint. It does not blindly clear user- or other-app-changed settings.
- Tokens are present in generated YAML under current-user app data. They and imported profile files require a hardened ACL/DPAPI design before production release.

## CI and milestones

`.github/workflows/windows.yml` compiles on a Windows runner, executes config safety smoke tests and downloads the pinned official core with SHA-256 verification, and packages a prototype containing that binary. CI artifact is a **test build, not a public release**; binary licenses and Windows functional acceptance still require review.

The acceptance gate still requires a real Windows 10 22H2 and Windows 11 x64 run, memory/CPU baseline, API integration tests and review. The legacy `onecat/FlClash` fork is not modified by this prototype.

Planning baseline: `docs/implementation-plan.md`.
