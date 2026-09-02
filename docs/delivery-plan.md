# Delivery plan

> Dashboard status: `GOAL-004 IN_PROGRESS`
> Updated: `2026-09-02T23:41:31Z`

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

**Current Goal verification:** `51/72 = 70.8%` selected product AC complete locally. EPIC-02 is `READY 15/15`. EPIC-03 is `READY 13/13`: PR [#6](https://github.com/Just9120/llm-inspector/pull/6) follow-up CI `33694308639` and exact-merge `main` CI `33694559218` succeeded. EPIC-04 candidate is `10/12`: `E04-AC01..02` and `E04-AC04..11` have local CODE/TEST Evidence, while `E04-AC03`/`E04-AC12` remain open without trustworthy cross-turn/cold-warm evidence and CI is pending. EPIC-09 remains `13/14`; persistent-schema `E09-AC06` stays open for EPIC-08. EPIC-08 is authorized but not started.

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
| Base SHA | `ba63d0b219e61527d3d81994638dee39a11c14bf` — verified local/`origin/main` after PR #6 merge and successful exact-merge CI |
| Working branch | `codex/epic-04-tokens-context-timings` |
| Last verified revision | `e1c7423cee718cf923dc058990449ba2e3339bfb` — all EPIC-04 code commits covered by the full local CI-equivalent pipeline; containing readiness/checkpoint update is intentionally not self-referenced |
| Initial worktree state | Clean EPIC-04 branch created from synchronized local/`origin/main`; merged EPIC-03 branch had zero unique commits and was safely removed locally/remotely |
| Current worktree state | Six reviewable EPIC-04 commits plus the current readiness/documentation update; generated `artifacts/` is ignored; unrelated changes absent |
| Completed work | Added OpenAI Chat fixture v2 nested cached/reasoning counters, normalized llama.cpp cache/prompt/generation rates, typed conditional context/load/queue values, latest-request UI with quality, privacy-safe streaming TTFT, monotonic total duration and content non-decoding. Full pipeline passed: exact SDK `10.0.400`, normal/RID locked restores, format, Release build `0` warnings/errors, `86/86` tests with zero skips, self-contained `win-x64` publish and smoke |
| Current step | Finalize EPIC-04 partial readiness/evidence metadata before the one initial push |
| Next exact action | Commit the evidence map, rerun the complete pipeline on the final local head, then perform one initial push and open the EPIC-04 PR |
| PR / CI | EPIC-04 PR not created; no remote branch or CI run exists yet |
| Deployment | N/A — Windows desktop product; no runtime deployment target |
| Blockers | No delivery gate blocker. `E09-AC06` still requires the later EPIC-08 persistent schema. EPIC-04 `E04-AC03` and `E04-AC12` lack trustworthy session/model-load correlation; per increment policy they remain open but do not block a safe partial epic PR/merge |
| Unverified assumptions | Supported responses may expose nested cached/reasoning token counters, but coverage must be established by versioned fixtures. Current supported flows provide no trustworthy context limit, history/tools attribution, per-request model-load duration or queue duration; these values must remain `unavailable` unless exact source evidence is added |
| Preserved pre-existing changes | Goal started from clean worktree; unrelated changes absent |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-02T23:41:31Z` | `51/139 = 36.7%` | `51/164 = 31.1%` | Ten EPIC-04 AC independently mapped to local code/tests; cross-turn context delta and cold/warm analytics remain uncredited; CI pending |
| Previous | `2026-09-02T23:25:41Z` | `41/139 = 29.5%` | `41/164 = 25.0%` | Same 41 completed AC after starting EPIC-04 and before its implementation |

Delta: `+7.2 п.п.` initial release and `+6.1 п.п.` full roadmap from ten newly completed EPIC-04 AC; denominators are unchanged.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-02 Backends/clients/API | 🟩 READY | 15/15 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-03 Live state/quality | 🟩 READY | 13/13 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-04 Tokens/context/timings | 🟦 IN PROGRESS | 10/12 | 83.3% | ✅ | ◐ | ◐ | — | N/A | N/A |
| EPIC-08 History/analytics/retention | ⬜ BACKLOG | 0/18 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-09 Privacy/locality/pass-through | 🟦 IN PROGRESS | 13/14 | 93% | ✅ | ◐ | ◐ | ◐ | N/A | N/A |
| Other initial-release epics | ⬜ BACKLOG | 0/67 | 0% | ◐ | — | — | — | N/A | N/A |
| **Initial release** | **🟦 IN PROGRESS** | **51/139** | **36.7%** | **◐** | **◐** | **◐** | **◐** | **N/A** | **N/A** |
| Canonical backlog | ⬜ BACKLOG | 0/25 | 0% | ◐ | — | — | — | —* | —* |
| **Full roadmap** | **🟦 IN PROGRESS** | **51/164** | **31.1%** | **◐** | **◐** | **◐** | **◐** | **—*** | **—*** |

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
| `E04-AC12` | **Open:** neither current request detail nor unimplemented analytics can distinguish cold/warm without model-load/session evidence |

Local Evidence: exact SDK and locked normal/RID restore, format, Release build `0` warnings/errors, `86/86` tests without skips, self-contained `win-x64` publish and smoke all passed. CI remains absent until the initial PR push.

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
