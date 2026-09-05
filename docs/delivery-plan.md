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
| G06-06 | B01 lifecycle и B02 secure remote перенесены с прежними ownership/confirmation/DPAPI/loopback boundaries | PENDING |
| G06-07 | Русский Wails v2/Svelte-TypeScript UI предоставляет все функции через понятную навигацию и progressive disclosure | PENDING |
| G06-08 | Windows executable воспроизводимо собирается, проходит smoke/regression; final runtime path свободен от C#/.NET/Avalonia | PENDING |
| G06-09 | Increments проходят local checks, PR CI, merge и main CI; документация соответствует результату, temporary branches убраны | PENDING |

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | 2026-09-05T08:02:23Z |
| Base branch / SHA | main / `3fdf3caaae4f982f6e5db393cd6c493a24a644ba` |
| Working branch | `codex/goal-006-go-windows` |
| Last verified revision | `8870913045bcda83c449fb255b3dfdb733c72d17` — settings/profiles/background policy committed/validated; final native tray and resource-storage integration increment locally validated |
| Worktree state | Clean at start; only GOAL-006 changes |
| Completed work | Tranches 1–2 merged (exact IDs in archive). Tranche 3: legacy SQLite v1–v5 compatibility, atomic request/graph writes, resource history, readonly consistent slices, filters, safe bounded retention/confirmed clear, statistics/trends/comparisons/runtime correlation, offline v1 snapshot/export and bounded writer. Final review added native proxy-to-history error enum mapping, cross-client correlation identity guards and export/truncation boundaries |
| Current step | Tranche 4 — complete local Windows validation and PR delivery |
| Next exact action | Commit final verified Windows integration, perform one initial push and create tranche-4 PR; await both required jobs before merge |
| Validation / Evidence | Go formatting/mod verify/vet/build and 98 test functions + fuzz seeds PASS; performance coverage 100%, background 75.9%, resources 86.7%, history 84.1% (not readiness). Native hidden HWND/message loop/ABI/Russian UTF-16 and failure cleanup tested with substituted Shell_NotifyIcon; 50 background repeats PASS after correcting OS-thread WM_QUIT reuse. Resource/background integration 10 repeats PASS; proxy→resource→SQLite preserves FK/content allowlist, 2048-row timeline splits/drains correctly. Settings migration/rollback tested without user registration changes. Full reference .NET locked restore/format/build/260 tests/RID publish/smoke PASS on this tranche. No local race detector (no C compiler), no Go desktop runtime/LIVE claim |
| PR / CI | PR #36 merged at `3fdf3caaae4f982f6e5db393cd6c493a24a644ba`; PR CI `33952081663` and main CI `33952214437`: both required jobs SUCCESS. Main synced, local/remote storage branches safely removed. Windows increment: local only. No speculative reruns |
| Deployment / release | N/A; no publication authorized |
| Blockers / external gates | CI/CD stack/commands reconciliation explicitly authorized; no implementation blocker. Existing manual performance/Windows/two-host gates remain separate |
| Preserved pre-existing changes | None |

## Project readiness snapshots

| Snapshot | Initial release | Full agreed roadmap | Denominator и основание |
|---|---:|---:|---|
| Current — Go target | 0/143 = 0% | 0/168 = 0% | 139 original initial AC + 4 approved stack/UX AC; 25 backlog AC. No Go product runtime exists at migration start, so no old C# runtime Evidence is transferred |
| Previous — C# reference | 132/139 = 95.0% | 152/164 = 92.7% | Historical assessment in pre-migration spec/GOAL-005; comparison only, not evidence for Go |

Расхождение больше 10 п.п. объясняется сменой оцениваемой production implementation: предыдущая оценка относится к C#, новая — к Go. Требования и реализованное поведение используются как migration reference, но тестировать Go нужно заново. Product AC credit требует end-to-end implementation, а не только наличия Go structs.

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
