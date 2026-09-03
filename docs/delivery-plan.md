# Delivery plan

> Dashboard status: `GOAL-004 IN_PROGRESS`
> Updated: `2026-09-03T01:05:08Z`

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

**Current Goal verification:** `70/72 = 97.2%` selected product AC complete locally. EPIC-02 is `READY 15/15`; EPIC-03 is `READY 13/13`. EPIC-04 was delivered as an honest `10/12` partial increment: PR [#7](https://github.com/Just9120/llm-inspector/pull/7) and exact merge commit `b29d80a788942938ad4246ab4cd358e7b41a71a8` passed CI runs `33696539694` and `33696722298`; `E04-AC03`/`E04-AC12` remain open without trustworthy cross-turn/cold-warm evidence. EPIC-08 locally reaches `18/18`, and its real-schema/runtime-canary tests locally close `E09-AC06`, making EPIC-09 `14/14`; both changes still require PR/main CI before terminal Evidence.

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
| Updated UTC | `2026-09-03T01:05:08Z` |
| Expected base branch | `main` |
| Base SHA | `b29d80a788942938ad4246ab4cd358e7b41a71a8` — verified local/`origin/main` after PR #7 merge and successful exact-merge CI `33696722298` |
| Working branch | `codex/epic-08-history-analytics-retention` |
| Last verified revision | `6b4ec003fb879ffd56a61c85ef00fc2f31e82450` — complete EPIC-08 code candidate covered by the full local CI-equivalent pipeline; final documentation commit is intentionally not self-referenced |
| Initial worktree state | Clean EPIC-08 branch created from synchronized local/`origin/main`; merged EPIC-04 branch had zero unique commits and was safely removed locally/remotely |
| Current worktree state | Six reviewable EPIC-08 commits before this documentation sync; worktree contained only expected documentation updates after the full local pipeline; unrelated changes absent |
| Completed work | Added Microsoft.Data.Sqlite `10.0.11` locked graphs; WAL/foreign-key/privacy-allowlisted schema; request/session/operation/turn/tool/resource stores; seven history filters; ordered detail; UTC trends; mean/median/nearest-rank P95; four comparison dimensions; exact four-option retention with persisted setting and short oldest-first batches; explicit preview/confirm clear; bounded non-blocking writer; degraded history runtime; user-facing controls. Full local pipeline passed exact SDK `10.0.400`, locked restores, format, Release build `0` warnings/errors, `112/112` tests, self-contained publish and smoke |
| Current step | Synchronize recalculated readiness/Evidence and prepare the single initial EPIC-08 push/PR |
| Next exact action | Commit final EPIC-08 documentation/checkpoint, verify clean diff/base, push once, open PR against `main`, then inspect exact-head CI without speculative rerun |
| PR / CI | EPIC-08 PR not created; remote branch does not exist and no EPIC-08 CI run exists |
| Deployment | N/A — Windows desktop product; no runtime deployment target |
| Blockers | No EPIC-08 delivery gate blocker. Goal closure remains blocked at `70/72` by EPIC-04 `E04-AC03`/`E04-AC12`: supported flows expose no trustworthy session/agent-turn identity or cold/warm model-load evidence, and time proximity is forbidden as proof. Per increment policy this does not block the EPIC-08 PR/merge; a bounded fix/reconciliation PR follows |
| Unverified assumptions | GitHub PR/main CI have not run for this branch. Runtime resource trends accept typed resource samples, but real collectors remain outside this Goal; no resource values are fabricated. Real client session IDs and cold/warm load events remain absent in supported OpenAI-compatible flows |
| Preserved pre-existing changes | Goal started from clean worktree; unrelated changes absent |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-03T01:05:08Z` | `70/139 = 50.4%` | `70/164 = 42.7%` | Independent AC map credits EPIC-08 `18/18` plus E09-AC06 after full local pipeline; GitHub CI remains pending |
| Previous | `2026-09-03T00:03:30Z` | `51/139 = 36.7%` | `51/164 = 31.1%` | Exact state after merged EPIC-04: ten EPIC-04 AC complete; E04-AC03/E04-AC12 and E09-AC06 open |

Delta: `+13.7 п.п.` initial release and `+11.6 п.п.` full roadmap. Изменение больше 10 п.п. объясняется одним independently validated tranche из 19 AC: EPIC-08 `18/18` и закрытый real-schema privacy criterion `E09-AC06`; denominators `139`/`164` не менялись.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-02 Backends/clients/API | 🟩 READY | 15/15 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-03 Live state/quality | 🟩 READY | 13/13 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-04 Tokens/context/timings | 🟦 IN PROGRESS | 10/12 | 83.3% | ✅ | ◐ | ◐ | ◐ | N/A | N/A |
| EPIC-08 History/analytics/retention | 🟦 IN PROGRESS | 18/18 | 100% | ✅ | ✅ | ✅ | — | N/A | N/A |
| EPIC-09 Privacy/locality/pass-through | 🟦 IN PROGRESS | 14/14 | 100% | ✅ | ✅ | ✅ | ◐ | N/A | N/A |
| Other initial-release epics | ⬜ BACKLOG | 0/67 | 0% | ◐ | — | — | — | N/A | N/A |
| **Initial release** | **🟦 IN PROGRESS** | **70/139** | **50.4%** | **◐** | **◐** | **◐** | **◐** | **N/A** | **N/A** |
| Canonical backlog | ⬜ BACKLOG | 0/25 | 0% | ◐ | — | — | — | —* | —* |
| **Full roadmap** | **🟦 IN PROGRESS** | **70/164** | **42.7%** | **◐** | **◐** | **◐** | **◐** | **—*** | **—*** |

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

The deterministic handshake fix passed follow-up PR CI `33694308639`; merge commit `ba63d0b219e61527d3d81994638dee39a11c14bf` passed exact-merge `main` CI `33694559218`. EPIC-03 is `READY`.

### EPIC-04 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E04-AC01` | Versioned OpenAI Chat fixture v2 and contract/UI tests expose input, output and cached input separately when exact counters exist |
| `E04-AC02` | Request detail shows exact current context usage from `prompt_tokens`; typed context limit renders exact when supplied and `unavailable` in current adapters without a source |
| `E04-AC03` | **Open:** current OpenAI-compatible flow has no trustworthy session/agent-turn correlation; time proximity is intentionally not used |
| `E04-AC04` | Typed history/tools/cache breakdown renders exact supplied components and explicit unavailable values; current fixture proves cache contribution only |
| `E04-AC05` | Only the whole-number `reasoning_tokens` technical counter is allowlisted; fixture sentinel and privacy projection test prove reasoning content is absent, and parser does not decode non-telemetry response strings |
| `E04-AC06..07` | llama.cpp fixture maps exact `prompt_per_second`/`predicted_per_second` to common prompt/generation rates while retaining native metrics |
| `E04-AC08` | Contract/integration tests emit calculated monotonic TTFT only after first non-empty streaming content delta; role-only, tool-only and non-streaming flows stay unavailable |
| `E04-AC09..10` | Typed model-load/queue metrics render exact supplied values; current supported adapters expose no trustworthy source and therefore render unavailable instead of zero |
| `E04-AC11` | Every terminal proxy observation receives monotonic total duration and the latest-request detail renders it with `calculated` quality |
| `E04-AC12` | **Open:** analytics can compare exact model-load metrics, but current supported flows provide no trustworthy cold/warm model-load/session evidence |

EPIC-04 terminal Evidence: PR #7 run `33696539694` and exact-merge `main` run `33696722298` succeeded. Open AC remain uncredited; Evidence for the partial epic is CODE/TEST/CI `◐`.

### EPIC-08 AC evidence map

| Atomic AC | Current evidence |
|---|---|
| `E08-AC01..03` | Transactional schema v1 and `SqliteTechnicalHistoryStoreTests` persist typed request/session/operation records plus linked turns/tools/resources; schema has no content carrier |
| `E08-AC04` | Typed `HistoryFilter`, SQL predicates, UI parser/controls and integration/Windows tests cover period, client, backend, model, session, status and error type |
| `E08-AC05` | `GetOperationDetailAsync` and presenter return turns/tools in sequence with timings, typed errors and provenance-preserving CPU/memory samples |
| `E08-AC06` | UTC-day read model exposes input/output tokens, TTFT, prompt/generation speed, context usage, CPU, memory and error-rate trends without fabricating missing resource samples |
| `E08-AC07..10` | `HistoryStatisticsTests` prove arithmetic mean, median, nearest-rank P95 and `n >= 3` sufficiency boundary; method/policy are documented in architecture and visible in UI |
| `E08-AC11..15` | Typed comparison filters and UI cover period/model/backend/client; direction-aware degradation requires sufficient baseline and candidate samples and a selected metric |
| `E08-AC16` | Persistent setting and UI expose exactly `7 days`, `30 days`, `90 days`, `indefinite`; default is `30 days` |
| `E08-AC17` | Startup/save cleanup uses cutoff boundaries, oldest-first batches of 500 and separate short transactions; integration tests cover 7/30/90, indefinite, exact boundary and multi-batch deletion |
| `E08-AC18` | Clear API/UI requires an explicit all/range scope, exact preview and separate confirmation; preview invalidation/staleness are fail-closed and tested |

Cross-epic Evidence: `SqliteHistoryPrivacyTests` asserts exact table-column allowlists and relays runtime-generated prompt/response/reasoning/tool canaries through the gateway before scanning DB/WAL files. This locally completes `E09-AC06`. Full local validation: exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `112/112` tests without skips, self-contained `win-x64` publish and smoke. CI is absent until the one initial PR push.

## Current blockers и decisions

1. `GOAL-004`: exact target is 72 atomic AC, not a subjective percentage.
2. `DELIVERY`: epic PR may merge partial safe Evidence; incomplete AC remains open and receives no credit.
3. `SECURITY`: listener and backend target remain loopback-only; no generic hosting bind override or redirect escape.
4. `PRIVACY`: raw content can exist transiently only in relay memory; it cannot enter metadata types, logs, persistence or analytics.
5. `EPIC-12`: numeric performance budgets remain outside this Goal and do not block selected epics.
6. `EPIC-09`: `E09-AC06` is locally complete on the real EPIC-08 schema/runtime canary corpus; EPIC-09 remains non-READY only until the new PR/main CI Evidence is terminal.
7. `EPIC-04`: exactly `E04-AC03` and `E04-AC12` remain outside current evidence because supported flows provide neither exact session/agent-turn identity nor trustworthy cold/warm model-load signal; no time-proximity inference is allowed.

## Roadmap after current Goal

Roadmap beyond `GOAL-004` is informational only and has no implementation authorization.

1. Remaining initial epics: agent operations, resources, diagnostics, Windows background UX, snapshots and reliability/performance.
2. Windows release Goal: signed package/distribution validation without server CD.
3. Canonical backlog epics only after separate explicit authorization.
