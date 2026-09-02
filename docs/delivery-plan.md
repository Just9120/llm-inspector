# Delivery plan

> Dashboard status: `GOAL-004 IN_PROGRESS`
> Updated: `2026-09-02T22:15:32Z`

## Current Goal

### `GOAL-004 — Реализовать 72-AC core product tranche отдельными epic PR`

- **State:** `IN_PROGRESS`.
- **Authorization source:** explicit user instruction от `2026-09-03`: «Бери как единую goal и начинай постепенную реализацию проекта. За ориентир возьми скажем 70 AC. Разноси по разным ПРам эпики. В случае блокера это не должно мешать ПРу, коммит и мердж. Потом фиксы».
- **Exact product denominator:** `72` atomic AC: `EPIC-09 14 + EPIC-02 15 + EPIC-03 13 + EPIC-04 12 + EPIC-08 18`.

**Scope**

- Реализовать privacy/locality/pass-through foundation и OpenAI-compatible gateway.
- Реализовать Ollama, llama.cpp и LM Studio adapters, known-client attribution и supported OpenAI-compatible flows.
- Реализовать live request state/quality model.
- Реализовать token/context/timing projections.
- Реализовать SQLite technical history, analytics и retention.
- Проводить delivery отдельными reviewable PR по эпикам; cross-epic gaps закрывать последующими fix-PR внутри этой Goal.
- После каждого PR независимо пересчитывать atomic AC и Evidence; partial implementation не получает полный AC credit.

**Non-goals**

- `EPIC-01`, `EPIC-05`–`EPIC-07`, `EPIC-10`–`EPIC-12` за пределами минимальных seams, необходимых выбранным эпикам.
- Resource collectors, diagnostic rules, tray/autostart/notifications и diagnostic snapshots.
- Backend/model lifecycle management, remote/LAN listening, additional protocols, multi-GPU и analytics export.
- MSIX, signing, release publication, automatic update и любой server/runtime CD.
- Изменение ratified product requirements, business rules или atomic AC.

**Goal acceptance criteria**

1. `EPIC-09` достигает `14/14` с required privacy/pass-through Evidence.
2. `EPIC-02` достигает `15/15` на versioned fixtures/stub E2E Evidence.
3. `EPIC-03` достигает `13/13` с state/quality/UI Evidence.
4. `EPIC-04` достигает `12/12` с token/context/timing/UI Evidence.
5. `EPIC-08` достигает `18/18` с SQLite/analytics/retention/UI Evidence.
6. Каждый bounded epic/fix PR проходит local CI-equivalent validation и green exact-revision GitHub CI до merge; delivery state синхронизируется без fabricated claims.

**Required Evidence:** для каждого выбранного эпика `SPEC: ✅`; `CODE: ✅`; `TEST: ✅`; `CI: ✅`; `DEPLOY: N/A`; `LIVE: N/A`.

**Increment policy**

- Функциональный blocker не запрещает commit/PR/merge безопасного инкремента, если incomplete AC и Evidence явно остаются partial/absent.
- Failed CI, privacy/security regression или обязательный safety gate не обходятся и блокируют merge до исправления.
- После partial epic PR работа продолжается fix-PR внутри `GOAL-004`; переход к новой Goal не происходит.

**Current Goal verification:** `41/72 = 56.9%` selected product AC complete. EPIC-02 is `READY 15/15`. EPIC-03 maps `13/13` to local code/tests, but PR [#6](https://github.com/Just9120/llm-inspector/pull/6) CI `33693671440` failed an inherited abort-fixture race; a deterministic handshake fix is local, so epic remains `IN PROGRESS` with `CI: ❌`. EPIC-09 remains `13/14`; PR [#3](https://github.com/Just9120/llm-inspector/pull/3), PR CI `33685945571` and exact-merge `main` CI `33686231092` are successful, while persistent-schema `E09-AC06` remains open. Other selected epics are authorized but not started.

**Stop condition:** остановиться после `72/72` и required Evidence либо при подтверждённом `BLOCKED` / `PENDING_EXTERNAL_GATE`; к остальным эпикам или новой Goal не переходить.

## PR execution sequence

1. **PR-09:** `EPIC-09` privacy/locality/transparent proxying core.
2. **PR-02:** `EPIC-02` backend/client adapters и полный supported OpenAI-compatible subset.
3. **PR-03:** `EPIC-03` live state и metric quality UI.
4. **PR-04:** `EPIC-04` tokens/context/timings.
5. **PR-08:** `EPIC-08` SQLite history/analytics/retention.
6. **Fix PRs:** только подтверждённые cross-epic gaps до `72/72`.

Sequence задаёт delivery order внутри одной authorized Goal. Следующий PR начинается после merge/cleanup предыдущего и sync нового `origin/main`.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-02T23:15:15Z` |
| Expected base branch | `main` |
| Base SHA | `e1e1b735116b94d73fa87559da8759c5f58d243c` — verified local/`origin/main` after PR #5 merge and successful exact-merge CI |
| Working branch | `codex/epic-03-live-state-quality` |
| Last verified revision | `56d602fd7ffce9285c7853d4135ddf7975708771` — deterministic fix and failed-CI metadata covered by the full local CI-equivalent pipeline; containing checkpoint update is intentionally not self-referenced |
| Initial worktree state | Clean EPIC-03 branch created from synchronized local/`origin/main`; merged fix branch had zero unique commits and was safely removed locally/remotely |
| Current worktree state | Deterministic fix and failed-CI metadata committed; worktree clean before this checkpoint update; branch is two commits ahead of the initial PR head |
| Completed work | EPIC-03 initial candidate passed full local validation and opened PR #6; CI `33693671440` passed restore/format/build but failed `BackendBodyAbortKeepsOriginalStatusAndRecordsRelayFailure`, skipping downstream publish/smoke; failed log proved backend could abort before proxy observed headers; fixture now waits until client has received exact `200` and partial body before abort; focused test `21/21`; grouped candidate passed locked restore, format, parallel Release build with `0` warnings/errors, exact parallel CI test command `80/80` with `0` failed/skipped, locked win-x64 restore, self-contained publish and smoke |
| Current step | Perform the single grouped follow-up push authorized by the confirmed CI failure |
| Next exact action | Push the validated commits to PR #6 once, then inspect the new exact-head CI without speculative rerun |
| PR / CI | EPIC-03 PR #6 open; initial head `d1e8f313b6652cfe1656f1de0c4fac99bd852103`; run `33693671440` failed at tests. One grouped follow-up push is authorized after complete local validation |
| Deployment | N/A — Windows desktop product; no runtime deployment target |
| Blockers | Failed PR CI is an active merge gate and cannot be bypassed; local deterministic fix is not remote Evidence yet. `E09-AC06` still requires the later EPIC-08 persistent schema |
| Unverified assumptions | GitHub-hosted behavior of the handshake-fixed test remains pending. Supported OpenAI-compatible flows have no trustworthy per-request load/queue/progress percentage signal; current runtime therefore uses protocol-observed stages without percentage unless a future typed backend-reported signal exists |
| Preserved pre-existing changes | Goal started from clean worktree; unrelated changes absent |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-02T23:12:59Z` | `41/139 = 29.5%` | `41/164 = 25.0%` | Same 41 product AC independently mapped; EPIC-03 PR CI failed an inherited fixture race and deterministic local fix is pending remote Evidence |
| Previous | `2026-09-02T23:02:05Z` | `41/139 = 29.5%` | `41/164 = 25.0%` | Same completed AC before initial PR #6 CI; EPIC-03 CI was absent rather than failed |

Delta: `0.0 п.п.` for initial release and full roadmap. Product denominators and completed AC did not change; this snapshot records failed CI Evidence and its local deterministic fix.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-02 Backends/clients/API | 🟩 READY | 15/15 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-03 Live state/quality | 🟦 IN PROGRESS | 13/13 | 100% | ✅ | ✅ | ✅ | ❌ | N/A | N/A |
| EPIC-04 Tokens/context/timings | ⬜ BACKLOG | 0/12 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-08 History/analytics/retention | ⬜ BACKLOG | 0/18 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-09 Privacy/locality/pass-through | 🟦 IN PROGRESS | 13/14 | 93% | ✅ | ◐ | ◐ | ◐ | N/A | N/A |
| Other initial-release epics | ⬜ BACKLOG | 0/67 | 0% | ◐ | — | — | — | N/A | N/A |
| **Initial release** | **🟦 IN PROGRESS** | **41/139** | **29.5%** | **◐** | **◐** | **◐** | **◐** | **N/A** | **N/A** |
| Canonical backlog | ⬜ BACKLOG | 0/25 | 0% | ◐ | — | — | — | —* | —* |
| **Full roadmap** | **🟦 IN PROGRESS** | **41/164** | **25.0%** | **◐** | **◐** | **◐** | **◐** | **—*** | **—*** |

`*` Remote/LAN backlog DEPLOY/LIVE applicability remains undecided and is outside this Goal.

### EPIC-02 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E02-AC01..03` | `BackendTelemetryAdapters` profiles plus versioned Ollama/llama.cpp/LM Studio JSON+SSE fixtures; `NonStreamingFixturesProduceCommonTelemetryWithIdenticalSemantics`, `StreamingFixturesExtractFinalUsageAcrossArbitraryByteBoundaries` and Gateway E2E projection |
| `E02-AC04` | One `MetricValue`/`MetricUnit` contract for all adapters; token counts are whole `TokenCount` values with identical quality/provenance semantics |
| `E02-AC05` | llama.cpp native `cache_n`, `prompt_n`, `predicted_n`, `*_ms` and `*_per_second` remain namespaced backend metrics with exact native names and units |
| `E02-AC06` | Constructor invariants plus missing/malformed/oversized/deep/fractional negative corpus; absent or ambiguous values have `Value = null`, `Quality = Unavailable` |
| `E02-AC07..10` | Dedicated loopback base paths for OpenCode Desktop, Hermes Desktop, Cline and Open WebUI; route attribution is tested and shown in UI/README |
| `E02-AC11` | Generic `/v1` path is always `GenericUnknown` while retaining the same request telemetry |
| `E02-AC12` | Standard configurable base URLs plus transparent `GET /models` handshake for generic/known paths; primary client configuration sources recorded with fixture set |
| `E02-AC13..15` | Non-streaming, fragmented SSE and fragmented tool-call flows are relayed byte-for-byte/order-preserving through stub-backend integration tests; telemetry/parser failure isolation is tested |

EPIC-02 terminal Evidence: PR #5 run `33691566607` and exact-merge `main` run `33691761413` succeeded; all required dimensions are complete.

### EPIC-03 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E03-AC01` | `LiveRequestTracker` keeps one atomic stage and monotonic calculated elapsed per active request; concurrent isolation unit test and Gateway/UI integration |
| `E03-AC02..06` | Domain enum/state contract and presenter labels distinguish model loading, queue/waiting, prompt processing, reasoning/generation and tool wait; richer stages require typed backend-reported evidence |
| `E03-AC07..09` | Proxy outcomes deterministically map to completed, cancelled or error terminal stage; unit tests plus success/cancellation/backend-failure integration paths |
| `E03-AC10` | Bounded ETA estimator requires at least three increasing same-source backend samples spanning at least five percentage points; UI renders only qualified `estimated` ETA |
| `E03-AC11..12` | Percentage originates only from `BackendProgressSignal` as exact backend metric; absent/regressed/changed-source evidence yields stage plus `unavailable` without percentage |
| `E03-AC13` | Presenter emits `[exact]`, `[calculated]`, `[estimated]` or `[unavailable]` on every displayed numeric metric; Windows tests cover all branches |

PR CI `33693671440` failed the inherited abort fixture; the deterministic handshake fix must pass PR and post-merge CI before EPIC-03 can become `READY`.

## Current blockers и decisions

1. `GOAL-004`: exact target is 72 atomic AC, not a subjective percentage.
2. `DELIVERY`: epic PR may merge partial safe Evidence; incomplete AC remains open and receives no credit.
3. `SECURITY`: listener and backend target remain loopback-only; no generic hosting bind override or redirect escape.
4. `PRIVACY`: raw content can exist transiently only in relay memory; it cannot enter metadata types, logs, persistence or analytics.
5. `EPIC-12`: numeric performance budgets remain outside this Goal and do not block selected epics.
6. `EPIC-09`: `E09-AC06` remains open until real EPIC-08 schema/migration Evidence exists; a placeholder schema is not acceptable evidence.

## Roadmap after current Goal

Roadmap beyond `GOAL-004` is informational only and has no implementation authorization.

1. Remaining initial epics: agent operations, resources, diagnostics, Windows background UX, snapshots and reliability/performance.
2. Windows release Goal: signed package/distribution validation without server CD.
3. Canonical backlog epics only after separate explicit authorization.
