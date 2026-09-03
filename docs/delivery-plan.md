# Delivery plan

> Dashboard status: `GOAL-005 IN_PROGRESS`
> Updated: `2026-09-03T08:46:10Z`

## Current Goal

### `GOAL-005 — Реализовать все оставшиеся canonical product AC`

- **State:** `IN_PROGRESS`.
- **Authorization source:** explicit user instruction от `2026-09-03`: «Теперь бери оставшиеся AC и реализуй в рамках Goal» после указания вести эпики разными PR, не останавливать безопасный partial PR из-за product blocker и закрывать gaps последующими fix PR.
- **Verified base:** `origin/main` / local `main` = `ea8498efae98adda684dd88d92e52d3401872a5c`; exact-main CI [33732075018](https://github.com/Just9120/llm-inspector/actions/runs/33732075018) succeeded.
- **Exact product denominator:** `92` оставшихся atomic AC: initial release `67` (`EPIC-01 4 + EPIC-05 8 + EPIC-06 8 + EPIC-07 16 + EPIC-10 8 + EPIC-11 10 + EPIC-12 13`) и backlog `25` (`BACKLOG-01 5 + BACKLOG-02 9 + BACKLOG-03 3 + BACKLOG-04 2 + BACKLOG-05 3 + BACKLOG-06 3`).

**Scope**

- Реализовать все ещё не выполненные canonical AC из `docs/project-spec.md` reviewable epic/fix PR.
- Для каждого increment выполнять focused checks, полный local CI-equivalent перед единственным initial push, required PR CI, merge, exact-main CI, sync/cleanup и независимый readiness recalculation.
- При product/SPEC/external blocker доставлять только безопасную подтверждаемую часть; невыполненные AC не кредитовать и продолжать fix increment внутри этой Goal, когда gate доступен.
- Синхронизировать operational fields `project-spec`, architecture facts и delivery metadata без изменения durable requirements.

**Non-goals**

- Изменение canonical scope, business rules, atomic AC, support matrix или CI/CD safety contract.
- Выдумывание remote topology/authentication, target OS matrix, performance budgets, signing identity, distribution channel, credentials или runtime deployment target.
- Обход privacy, CI, release, security или repository protection gates.
- Product behavior вне 92 перечисленных AC и unrelated refactoring.

**Goal acceptance criteria**

1. Каждый из 92 AC либо выполнен с traceable Evidence, либо явно оставлен невыполненным с точным blocker/gate и без readiness credit.
2. Каждый bounded epic/fix increment проходит required local validation и exact-revision GitHub CI до merge.
3. После каждого merge exact-main CI terminal success подтверждён до начала следующего branch; local `main` синхронизирован, merged branch безопасно удалён.
4. Initial/full readiness каждый раз считается только как `completed atomic AC / 139` и `completed atomic AC / 164`; предыдущие оценки не используются как доказательство.
5. Goal достигает `DONE` только при `164/164` и required Evidence либо останавливается в `BLOCKED` / `PENDING_EXTERNAL_GATE`, если после всех безопасных increments остаётся внешний gate.

**Required Evidence:** для feature epics `SPEC`, `CODE`, `TEST`, `CI` согласно их DoD; `DEPLOY`/`LIVE` — `N/A`, кроме ещё не определённой applicability `BACKLOG-02`. Exact status не повышается без фактического Evidence.

**Known blockers/dependencies**

- `EPIC-01/E01-AC01`: release Evidence на Windows 11 25H2 Home/Pro требует будущего release Goal, signing/distribution decision и clean install/upgrade/runtime matrix.
- `EPIC-12`: numeric performance budgets и frozen hardware/workload fixtures отсутствуют; `SPEC: ◐` блокирует READY, но не measurement infrastructure и reliability hardening.
- `BACKLOG-02`: remote topology, identity/authentication и DEPLOY/LIVE applicability не определены; `SPEC: ◐` блокирует implementation, затрагивающую security boundary.
- `BACKLOG-03`: supported Linux distributions/macOS versions не определены после demand gate; `SPEC: ◐` блокирует platform-support claim.

**Stop condition:** после `164/164` с required Evidence либо после исчерпания безопасных increments при подтверждённом `BLOCKED` / `PENDING_EXTERNAL_GATE`; к новой Goal без explicit authorization не переходить.

## PR execution sequence

1. `EPIC-01` — Windows application boundary и доступные product surfaces; release-matrix gate остаётся честно отделён.
2. `EPIC-05` — agent operations, tools и concurrency.
3. `EPIC-06` — Windows resource telemetry.
4. `EPIC-07` — explainable diagnostics и errors.
5. `EPIC-10` — background/tray/settings/notifications.
6. `EPIC-11` — anonymized diagnostic snapshot.
7. `EPIC-12` — reliability/measurement hardening до границы approved numeric budgets.
8. `BACKLOG-01`, `BACKLOG-04`, `BACKLOG-05`, `BACKLOG-06` — lifecycle, protocols, multi-GPU и export отдельными PR.
9. `BACKLOG-02`, `BACKLOG-03` — только после закрытия canonical SPEC/external gates; до этого допускаются лишь нерасширяющие boundary seams/tests/docs.

Sequence — рабочий pipeline внутри одной authorized Goal; точный порядок fix PR может меняться по фактическим dependencies, denominator не меняется.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-03T08:46:10Z` |
| Expected base branch | `main` |
| Base SHA | `ea8498efae98adda684dd88d92e52d3401872a5c` — verified local/`origin/main`; exact-main CI `33732075018` success |
| Working branch | `codex/goal-005-epic-10` |
| Last verified revision | `817a434` — complete EPIC-10 candidate tree covered by the full local CI-equivalent; this final checkpoint update is docs-only |
| Initial worktree state | Clean branch created from verified `origin/main` after EPIC-07 exact-main success and safe local/remote branch cleanup; no unrelated changes |
| Current worktree state | Clean at full-validation boundary except this expected final docs-only checkpoint update; no unrelated changes |
| Completed work | EPIC-07 terminal: PR #13 and exact-main CI succeeded. `7273967` adds close-to-tray lifecycle, Windows tray commands, HKCU per-user autostart, atomic versioned settings, four independently gated content-free notification rules, silent delivery, bounded observation queue and versioned anti-spam policy; `817a434` maps architecture/Evidence/readiness. Full exact-SDK pipeline: locked normal/RID restores; format unchanged; Release build `0` warnings/errors; `186/186` tests, zero skips; clean self-contained `win-x64` publish; smoke `exit 0` |
| Current step | Record the final local checkpoint, then perform the one initial push and create the EPIC-10 PR |
| Next exact action | Commit this docs-only checkpoint, push `codex/goal-005-epic-10` once, create PR against `main`, then wait for exact-head required CI |
| PR / CI | EPIC-07 terminal: PR #13, head `5b41246b59b447ee1b70beb44d8e101c25388e4b`, PR CI `33731824812`, merge `ea8498efae98adda684dd88d92e52d3401872a5c`, exact-main CI `33732075018`, all success. EPIC-10 has not been pushed; exact-revision CI pending |
| Deployment | N/A for current Windows desktop feature DoD; server/runtime CD remains disabled |
| Blockers | None for bounded EPIC-10 implementation; `E01-AC01` remains an independent release-matrix blocker and is not credited |
| Unverified assumptions | Native tray interaction is compiled on Windows and the routing/lifetime/policy boundaries are automated, but clean-install interactive tray behavior remains part of blocked `E01-AC01` release-matrix Evidence |
| Preserved pre-existing changes | Goal started from clean synchronized worktree; unrelated changes absent |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-03T08:41:55Z` | `115/139 = 82.7%` | `115/164 = 70.1%` | Independent AC-by-AC calculation credits E10-AC01..08 from close-to-tray, persistent-history, autostart, typed notification and anti-spam CODE plus focused TEST Evidence; exact-revision CI remains open |
| Previous | `2026-09-03T08:14:33Z` | `107/139 = 77.0%` | `107/164 = 65.2%` | EPIC-07 `16/16`; terminal PR #13 and exact-main CI `33732075018` confirmed required CI Evidence |

Delta: `+5.7 п.п.` initial release и `+4.9 п.п.` full roadmap. Denominators `139`/`164` не менялись; increment выполнил `8` atomic EPIC-10 criteria.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-01 | 🟦 IN PROGRESS ⛔ | 3/4 | 75% | ✅ | ◐ | ◐ | ◐ | N/A | N/A |
| EPIC-02 | 🟩 READY | 15/15 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-03 | 🟩 READY | 13/13 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-04 | 🟩 READY | 12/12 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-05 | 🟩 READY | 8/8 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-06 | 🟩 READY | 8/8 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-07 | 🟩 READY | 16/16 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-08 | 🟩 READY | 18/18 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-09 | 🟩 READY | 14/14 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-10 | 🟦 IN PROGRESS | 8/8 | 100% | ✅ | ✅ | ✅ | ◐ | N/A | N/A |
| EPIC-11 | ⬜ BACKLOG | 0/10 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-12 | ⬜ BACKLOG ⛔ | 0/13 | 0% | ◐ | — | — | — | N/A | N/A |
| **Initial release** | **🟦 IN PROGRESS** | **115/139** | **82.7%** | **◐** | **◐** | **◐** | **◐** | **N/A** | **N/A** |
| BACKLOG-01 | ⬜ BACKLOG | 0/5 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-02 | ⬜ BACKLOG ⛔ | 0/9 | 0% | ◐ | — | — | — | —* | —* |
| BACKLOG-03 | ⬜ BACKLOG ⛔ | 0/3 | 0% | ◐ | — | — | — | N/A | N/A |
| BACKLOG-04 | ⬜ BACKLOG | 0/2 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-05 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-06 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| **Full roadmap** | **🟦 IN PROGRESS** | **115/164** | **70.1%** | **◐** | **◐** | **◐** | **◐** | **—*** | **—*** |

`*` Applicability `BACKLOG-02` DEPLOY/LIVE остаётся canonical SPEC gap.

### EPIC-01 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E01-AC01` | Не выполнен: self-contained `win-x64` validation publish не заменяет clean install/upgrade/launch/tray/proxy/SQLite recovery suite на Windows 11 25H2 Home/Pro; signing/distribution path не утверждён |
| `E01-AC02` | MainWindow имеет доступные `Live requests`, `History and analytics` и `Diagnostics` surfaces; `DesktopProductBoundaryTests` проверяет headings и named output controls |
| `E01-AC03` | `AppLaunchConfiguration` принимает только literal IPv4/IPv6 loopback backend URLs; defaults Ollama/llama.cpp/LM Studio и derived gateway options покрыты focused Windows tests |
| `E01-AC04` | Versioned launch parser отвергает start/stop/restart/model-load/runtime-mutation commands как unknown; UI не содержит lifecycle controls |

### EPIC-05 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E05-AC01` | `AgentOperationTracker` объединяет explicit request/turn/tool/final lifecycle в один ordered `TechnicalOperationGraph`; SQLite v3 сохраняет request membership, turns и tool events |
| `E05-AC02` | Operation detail упорядочен по explicit turn sequence и tool index; lifecycle хранит только technical state, без user/assistant/tool/final content |
| `E05-AC03` | Bounded request projection считает top-level `tools`; turn metric хранит value, quality, source, version и derivation |
| `E05-AC04` | JSON/SSE response projection считает `tool_calls` и объединяет fragmented function name только по tool index |
| `E05-AC05` | Tool event хранит normalized name, status/error category и calculated wall duration до exact next tool-result turn; arguments/results не входят в schema |
| `E05-AC06` | Operation ID и request ID разделяют одновременные requests; parallel integration fixture подтверждает восемь независимых operations |
| `E05-AC07` | Tracker требует совпадение operation/session/client/backend и rejects cross-session/cross-client continuation; parallel tests подтверждают отсутствие смешения |
| `E05-AC08` | Missing/malformed/duplicate/gap/out-of-order/inconsistent correlation остаётся ungrouped или `unavailable`; time proximity не используется |

### EPIC-06 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E06-AC01` | Per-request sampler stores NVIDIA GPU utilization, VRAM used/total, temperature and power with device ID; missing executable/device/field is typed `unavailable` |
| `E06-AC02` | Windows `GetSystemTimes` deltas and `GlobalMemoryStatusEx` provide versioned host CPU utilization plus RAM percent/used bytes |
| `E06-AC03` | Every sample carries UTC timestamp, exact request ID, optional operation ID and current versioned request stage |
| `E06-AC04` | Backend process is attributed only from one exact literal-loopback TCP listener owner, PID and start time/image identity; zero/multiple/unreadable owners remain unavailable |
| `E06-AC05` | Proven process uses Windows cumulative CPU, working set and I/O counters; no process metric is inferred when association/source is absent |
| `E06-AC06` | Read/write transfer deltas from the proven backend process are stored on the same request/stage timeline with calculated provenance |
| `E06-AC07` | Gateway counts actually relayed request/response bytes on the exact request timeline without buffering or blocking relay |
| `E06-AC08` | Live/history UI renders exact request/stage correlation, metric quality/source and explicit unavailable process/GPU state; samples are bounded to 2048 with persisted gap count |

### EPIC-07 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E07-AC01` | `DiagnosticRuleset` version `diagnostic-rules-v1` matches large prompt at explicit `8192`-token boundary; exact and below-boundary tests prevent implicit threshold drift |
| `E07-AC02` | Versioned slow-generation rule matches exact rate `<=10 tokens/s`; missing and just-above-boundary cases do not produce a false fact |
| `E07-AC03` | Exact request-correlated high process CPU/low GPU evidence produces only a CPU-offload `HYPOTHESIS`, explicitly avoiding unsupported layer-placement causality |
| `E07-AC04` | Request-correlated VRAM used/total derives a versioned ratio and detects pressure at `>=90%`; missing, mismatched, estimated and inconsistent evidence fail closed |
| `E07-AC05` | Cold model-load disposition plus measured duration identifies versioned load-latency contribution; missing duration remains hypothesis/insufficient rather than fabricated timing |
| `E07-AC06` | Exact backend queue metric is compared with a versioned `1000 ms` threshold; unavailable evidence is explicit |
| `E07-AC07` | Context used/limit derives a quality-preserving ratio and detects `>=90%`; unavailable or inconsistent numerator/denominator does not assert high usage |
| `E07-AC08` | Gateway transport classification and diagnostics distinguish connection refused, timeout, backend disconnect/unavailable and relay failure without retaining exception text |
| `E07-AC09` | Active lifecycle is reported as ongoing work; elapsed `>=30 s` without a request-matched typed backend signal is `INSUFFICIENT_DATA`, while only explicit `Stalled` signal yields confirmed fact |
| `E07-AC10` | Every ruleset conclusion carries a bounded human-readable explanation rendered by `DiagnosticsSummaryTextPresenter` |
| `E07-AC11` | Conclusions carry ruleset version and typed metric/stage/error/activity Evidence with quality/source/version; zero-evidence conclusions render explicit `unavailable` |
| `E07-AC12` | Estimated threshold matches and inferred CPU offload are hypotheses; missing/mismatched inputs are insufficient; boundary tests assert neither is upgraded to fact |
| `E07-AC13` | `ProxyErrorType`/`HistoryErrorType` distinguish connection refused, model loading/503, HTTP/API, timeout, context overflow, cancellation and backend crash/disconnect; gateway/store tests cover capture and persistence |
| `E07-AC14` | Analytics groups errors only by explicit operation/session metadata and includes first/last UTC time; a nearby error without metadata remains uncorrelated and arbitrary body content is excluded |
| `E07-AC15` | Query window count and UI label distinguish `single failure` from `recurring xN` at the versioned minimum of two occurrences |
| `E07-AC16` | Error-rate period comparison reports baseline/candidate per-type counts, each period's all-request denominator, rates and percentage-point delta for groups recurring in either period |

### EPIC-10 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E10-AC01` | `BackgroundLifetimeController` turns ordinary close into hide-and-continue while a tray is available; only explicit tray Exit requests process shutdown, and the hidden-window refresh timer is suspended independently of monitoring |
| `E10-AC02` | Gateway, buffered SQLite sink and history store remain composition-root owned until process exit; a real SQLite test records and reads a new observation after the close action becomes background hide |
| `E10-AC03` | Native per-user Win32 tray exposes Open, Notification settings, Pause/Resume notifications and Exit; typed router tests verify every command without depending on an interactive desktop fixture |
| `E10-AC04` | Settings UI enables per-user Windows autostart through exact HKCU Run registration with a quoted executable and `--background` launch mode |
| `E10-AC05` | The same settings surface disables the exact registration without creating a missing Run key; service tests cover enable/disable and rollback on settings-write failure |
| `E10-AC06` | Four separate persisted toggles gate backend unavailable, long completion, recurring typed error and exact/calculated high-context candidates; typed rules and independent-toggle tests cover all events |
| `E10-AC07` | Silent mode is persisted and maps to `NIIF_NOSOUND`; automated dispatch tests confirm every published notification carries the silent flag |
| `E10-AC08` | UI documents `notification-policy-v1`: same event key suppressed for 15 minutes and maximum three published notifications per rolling 10 minutes; exact-boundary tests cover suppression, expiry and rate-window release |

## Current blockers и decisions

1. Partial safe PR разрешён Goal policy, но incomplete AC остаётся открытым и не получает credit.
2. Required CI failure/skip, privacy/security regression или release safety failure блокирует merge до исправления.
3. External gates не подменяются code/config assumptions: они остаются explicit blockers до owner Evidence/decision.
4. Репозиторий не имеет approved post-merge metadata writer; terminal Evidence фиксируется в merged PR comment и восстанавливается в следующий substantive PR без metadata-only loop.

## Candidate next Goals

Новые Goals не предлагаются, пока `GOAL-005` активна. Implementation authorization относится только к этой Goal; после её terminal state агент останавливается.
