# Delivery plan

> Dashboard status: `IN_PROGRESS`
> Updated: `2026-09-05`

## Current Goal

**GOAL-006 — Go migration и русский desktop UI**

- **State:** `IN_PROGRESS`.
- **Authorization:** explicit user instruction от `2026-09-05`: «Тогда ставь единую goal и приступай к непрерывной реализации», после согласования Go/Wails v2/Svelte-TypeScript и русского современного неперегруженного UI в Google Docs.
- **Scope:** ратификация четырёх stack/UX требований в canonical spec; перенос всех реализованных Windows-функций EPIC-01..12, B01/B02/B05/B06 с preservation contracts; Go core, Wails v2 shell, Svelte/TypeScript UI; regression/security/privacy tests, совместимость SQLite/settings, воспроизводимая локальная Windows-сборка, перенос CI/build commands при сохранении safety boundaries, PR/merge/main synchronization.
- **Non-goals:** final tag/release/WinGet, manual Windows Home/Pro walkthrough, controlled performance measurements, two-host LIVE, Linux/macOS, новые LLM protocols и функции за пределами согласованного scope. Существующие manual gates остаются отдельными.
- **Required Evidence:** SPEC/CODE/TEST/CI подтверждены для доставленных increments; локальная Windows build/smoke и regression parity для конечного artifact. DEPLOY/LIVE — N/A для Goal; product B02 LIVE по-прежнему обязателен до READY.
- **Stop condition:** все Goal AC выполнены и merged branches safely removed (`DONE`); либо конкретный неустранимый scope blocker/external gate после завершения независимой работы.
- **Safety:** CI/CD safety contract, permissions, trusted release trigger и production operations не ослабляются. Owner от `2026-09-05` явно разрешил обновить **только стек и команды** в `AGENTS.md` и Project CI/CD profile при cutover; safety rules, permissions, release gates и disabled CD остаются неизменными.

### Goal acceptance criteria

| ID | Требование | State |
|---|---|---|
| G06-01 | Canonical spec содержит 4 согласованных stack/UX AC; Goal, migration boundaries и denominator явны | DONE |
| G06-02 | Go proxy/adapters сохраняют HTTP/SSE bytes/order, cancellation, quality/provenance и privacy по regression corpus | DONE — PR #34, PR/main CI PASS |
| G06-03 | Go live state, tokens/context/timings, operations/tools и diagnostics имеют parity с реализованными contracts | IN PROGRESS — core validated in PR #35; final storage/UI integration remains |
| G06-04 | Go SQLite/history/analytics/retention/snapshot/export сохраняют ранее committed data и content allowlist | IN PROGRESS — core/migrations/recovery/analytics/export validated; final desktop wiring remains |
| G06-05 | Windows resource/GPU collectors, tray/background/autostart/notifications, performance profiles перенесены | IN PROGRESS — native probes/monitor, settings/profiles/notifications and tray validated; final Wails wiring remains |
| G06-06 | B01 lifecycle и B02 secure remote перенесены с прежними ownership/confirmation/DPAPI/loopback boundaries | IN PROGRESS — remote/DPAPI/ingress/probe and typed lifecycle/native ownership core validated; final desktop wiring remains |
| G06-07 | Русский Wails v2/Svelte-TypeScript UI предоставляет все функции через понятную навигацию и progressive disclosure | IN PROGRESS — пять разделов/typed actions работают; final parity review остаётся |
| G06-08 | Windows executable воспроизводимо собирается, проходит smoke/regression; final runtime path свободен от C#/.NET/Avalonia | IN PROGRESS — локальная Go build/smoke PASS; clean build/release/CI cutover остаётся |
| G06-09 | Increments проходят local checks, PR CI, merge и main CI; документация соответствует результату, temporary branches убраны | PENDING |

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | 2026-09-05T10:47:36Z |
| Base branch / SHA | main / `ee32a97fd63110abe7688b48994579d97f8fdb05` |
| Working branch | `codex/goal-006-go-desktop` |
| Last verified revision | `54709f897954e7fc5891803f4af4eec8ae32f0fa` — facade/composition local tests PASS; последующие host/UI изменения проверены в worktree |
| Worktree state | Clean at start; only GOAL-006 changes |
| Completed work | Tranches 1–4 merged and verified (exact IDs in archive). Tranche 5: bounded DPAPI store/token lifecycle, private ingress and network probe; typed lifecycle profiles/manager/adapters, exact model confirmation, native suspended start/job ownership, bounded CLI/HTTP, exact-process cleanup and safe external-process refusal |
| Current step | Tranche 6 — Go/Wails executable и русский Svelte UI locally validated |
| Next exact action | Commit validated UI; final parity review (runtime facts/artifact environment/operation resources), packaging/release/CI cutover и clean-build validation; .NET reference пока сохранён |
| Validation / Evidence | Go formatting/mod verify/vet/build and 131 test functions + fuzz seeds PASS; lifecycle statement coverage 81.9%, remote 93.2%, gateway 87.7% (not readiness). Native attached/detached synthetic listener ownership/cleanup, bounded command output/HTTP, model identity/active-count/confirmation/concurrency tests PASS; full lifecycle suite 3 repeats PASS. Actual synthetic .NET DPAPI interoperability and previous 20-repeat remote tests PASS. .NET locked restore/format/build/260 tests/RID publish/smoke PASS (0 warnings/errors). No local race detector (no C compiler), no installed backend/model or real credentials/settings/Tailscale changed, no two-host LIVE claim |
| PR / CI | PR #38 and exact-main CI `33958021155` SUCCESS (both jobs). Local main/origin/main synced at `ee32a97`; merged connectivity branches safely removed. Desktop branch local only; no PR yet |
| Deployment / release | N/A; no publication authorized |
| Blockers / external gates | CI/CD stack/commands reconciliation explicitly authorized; no implementation blocker. Existing manual performance/Windows/two-host gates remain separate |
| Preserved pre-existing changes | None |

CI correction validation: full Go CI-equivalent PASS; canonical-path/legacy-DPAPI tests each 3 repeats PASS. A separate local artifact-test `TempDir RemoveAll: directory not empty` failure occurred once; readback found the directory empty, focused test 10 repeats and subsequent full suite PASS. Cause remains unconfirmed (possible Windows filesystem transient); no production storage change or suppressed test. CI corrections affect test expectations/startup budget only; production ownership, crypto rules and all release gates unchanged.

## Project readiness snapshots

Tranche-6 local validation: Go internal format/mod verify/vet/build и 143 test functions + fuzz seeds PASS; root vet/2 tests PASS. Frontend locked install (48 packages), Prettier, Svelte/TS (0 errors/warnings), 3 Node tests и Vite build PASS. Go/Wails Windows executable SHA-256 `59c97e1a4936249e1a50fda74142a194aebe4a8f6f1967675142697c8b84aafa`: actual process exit `0` после WebView2/bridge/proxy/SQLite/privacy smoke. Проверены все пять разделов, synthetic request в истории и custom-profile reactivity; обзор визуально проверен. Это local worktree artifact, не CI/release и не manual Windows matrix. Zero-time collector failure закрыт regression test; npm file lock от собственного preview устранён его остановкой и явным locked reinstall. Final build должен явно выполнять npm ci, не доверять Wails checksum cache.

| Snapshot | Initial release | Full agreed roadmap | Denominator и основание |
|---|---:|---:|---|
| Current — Go verified minimum | 4/143 = 2.8% | 4/168 = 2.4% | E01-AC02/03/07/08 подтверждены final surface; остальные AC ожидают независимого финального review |
| Previous — Go migration start | 0/143 = 0% | 0/168 = 0% | Go runtime отсутствовал; C# evidence не перенесён |

Это промежуточная нижняя граница проверенных AC, не оценка объёма кода. E01 вырос с 0/8 до 4/8 (50%) благодаря working Go desktop/Russian UI; denominator прежний. До PR каждый AC будет проверен отдельно. Исторический C# snapshot перенесён в archive.

## Execution pipeline

| Tranche / PR | Содержание | Required local Evidence |
|---|---|---|
| 1 — Go foundation | contracts, bounded parser, proxy, adapters, configs | Go unit/integration/privacy tests + C# reference CI unchanged |
| 2 — Telemetry | live state, correlation/operations/tools, diagnostics | fixture parity, concurrency and error/rule boundary tests |
| 3 — Storage | SQLite compatibility, history, analytics, retention, snapshot/export | migration/restart/privacy/selection tests |
| 4 — Windows | resources, GPU, tray/background, autostart, notifications, profiles | Windows probes with safe fixtures, lifecycle tests |
| 5 — Managed connectivity | lifecycle, capabilities, secure remote and DPAPI | exact process ownership and authenticated ingress tests |
| 6 — Desktop / cutover | Russian UI, Wails binding, packaging, CI, docs | Svelte/TS check/build, Go test/vet, Windows executable smoke |

Tranches — части одной Goal. Последовательные PR открываются после завершения и локальной проверки соответствующего scope. Непройденный required check нельзя считать успехом. После каждого merge следующий tranche продолжается в этой же Goal.

## Candidate next Goals

- Manual pre-publication validation: Windows matrix, controlled E12 measurements, B02 encrypted two-host LIVE, unavailable runtime/client compatibility.
- Final release / WinGet publication после product validation и отдельного authorization.
