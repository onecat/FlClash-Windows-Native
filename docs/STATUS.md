# Implementation status — 2026-10-26

## Completed (Stage 1 Windows CI build + smoke tests)

- [x] Independent Windows-native .NET Framework 4.8 / WinForms project, solution, test harness and Windows CI workflow.
- [x] Core process lifecycle: owned process only; `-t` validation; REST `/version` + `/configs` + TCP readiness; rollback of active config on startup failure; clean shutdown.
- [x] Local YAML import, safe-mode managed controller and proxy ports, strip extra listeners/TUN/DNS binding, new API secret each start.
- [x] REST proxy Selector groups, manual node selection, optional display of read-only automatic groups.
- [x] WebSocket traffic streaming UI.
- [x] WinINet system proxy lease with on-disk crash recovery and conservative restore semantics.
- [x] System tray, default launch-at-login, core auto-start after valid profile, system proxy only after ready, update checks disabled.
- [x] Official pinned stable core installer script (GitHub asset SHA-256 verification) and CI packaging recipe.
- [x] Config safety and default-setting smoke tests passed on Windows GitHub Actions.
- [x] Windows x64 Release compiled, Mihomo v1.19.33 SHA-256 verified and 23 MB test artifact packaged (CI run 40298695136).

## Not completed / not yet verified

- [x] GitHub independent repository: source committed to `onecat/FlClash-Windows-Native` (`main`) without modifying `onecat/FlClash`.
- [ ] Official Core runtime integration on Windows 10 22H2 / Windows 11: no Windows runner or test machine available in this session.
- [ ] Memory baseline and 8/24/72h validation: user machine measurements required.
- [ ] System proxy per-connection settings, PAC/AutoDetect interoperability, edge-case OS policies: Windows validation pending.
- [ ] Production security review: local profile trust, file ACL, Core update policy, privileged helper, signed installer, secure logs.
- [ ] Remote subscription download, profiles UX, connection/log pages, crash-restart policy, TUN and WebDAV deferred.

## Next gate (before calling this M1 verified)

1. [x] Create independent GitHub repository and import source without touching original FlClash main branch.
2. [x] Run `Windows Native CI` on Windows runner, fix compiler/restore issues, and execute smoke harness.
3. [x] Build test package with `v1.19.33` pinned official core and SHA-256 verification.
4. Test on Win10 22H2 and Win11 with a trusted YAML profile; observe Core process, selector, traffic WS, tray and system proxy restore.
5. Measure GUI+Core memory/CPU and compare to the existing Flutter Windows application with the same profile.

## Review notes

- System proxy manager deliberately refuses to overwrite settings no longer matching its managed endpoint. It does not yet manage every WinINet per-connection blob; treat as development-only.
- Core binary is launched as an unprivileged child; no TUN and no helper are shipped in this stage.
- The token is in the generated YAML under LocalAppData. Same-user process compromise remains out of scope; production needs stricter ACL and diagnostics redaction.
- Current CLI uses fixed local proxy port 17890 and API port 19090, configurable via settings.json but not via UI in stage 1.
