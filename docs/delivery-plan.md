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

**Current Goal verification:** `28/72 = 38.9%` selected product AC complete. EPIC-02 is `READY 15/15`: PR [#5](https://github.com/Just9120/llm-inspector/pull/5) CI `33691566607` and exact-merge `main` CI `33691761413` succeeded on `e1e1b735116b94d73fa87559da8759c5f58d243c`. EPIC-03 is authorized and started with `0/13`; no AC is credited before code/test evidence. EPIC-09 remains `13/14`; PR [#3](https://github.com/Just9120/llm-inspector/pull/3), PR CI `33685945571` and exact-merge `main` CI `33686231092` are successful, while persistent-schema `E09-AC06` remains open. Other selected epics are authorized but not started.

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
| Updated UTC | `2026-09-02T22:48:15Z` |
| Expected base branch | `main` |
| Base SHA | `e1e1b735116b94d73fa87559da8759c5f58d243c` — verified local/`origin/main` after PR #5 merge and successful exact-merge CI |
| Working branch | `codex/epic-03-live-state-quality` |
| Last verified revision | `e1e1b735116b94d73fa87559da8759c5f58d243c` — exact `main` covered by successful CI `33691761413`; containing EPIC-03 checkpoint update is intentionally not self-referenced |
| Initial worktree state | Clean EPIC-03 branch created from synchronized local/`origin/main`; merged fix branch had zero unique commits and was safely removed locally/remotely |
| Current worktree state | EPIC-03 documentation start is the only current change; product implementation not yet written |
| Completed work | EPIC-02 terminal Evidence recorded in merged PR #5 comment; `READY 15/15` recovered in canonical metadata; EPIC-03 13-AC scope and architecture constraints re-read |
| Current step | Define a pure live request state/quality contract and deterministic state-machine tests before Gateway/UI composition |
| Next exact action | Commit EPIC-03 start checkpoint, then implement domain stages, progress/ETA evidence invariants and unit tests |
| PR / CI | EPIC-02 PR #5 merged as `e1e1b735116b94d73fa87559da8759c5f58d243c`; PR run `33691566607` and `main` run `33691761413` success. EPIC-03 branch is local/unpushed; PR/CI absent |
| Deployment | N/A — Windows desktop product; no runtime deployment target |
| Blockers | No current EPIC-03 blocker. `E09-AC06` still requires the later EPIC-08 persistent schema |
| Unverified assumptions | Supported OpenAI-compatible flows have no trustworthy per-request load/queue/progress percentage signal; the state machine must represent such stages/signals without fabricating them, while current runtime uses only protocol-observed stages |
| Preserved pre-existing changes | Goal started from clean worktree; unrelated changes absent |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-02T22:48:15Z` | `28/139 = 20.1%` | `28/164 = 17.1%` | Same 28 product AC independently mapped; EPIC-02 now has terminal PR/main CI and `READY`, while `E09-AC06` remains open |
| Previous | `2026-09-02T22:15:32Z` | `28/139 = 20.1%` | `28/164 = 17.1%` | Same completed AC before EPIC-02 terminal CI; epic was not `READY` because its CI Evidence was not yet complete |

Delta: `0.0 п.п.` для initial release и full roadmap. Denominators и completed product AC не изменились; новый snapshot отражает terminal CI Evidence и переход EPIC-02 в `READY`, а не новый acceptance-criterion credit.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-02 Backends/clients/API | 🟩 READY | 15/15 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-03 Live state/quality | 🟦 IN PROGRESS | 0/13 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-04 Tokens/context/timings | ⬜ BACKLOG | 0/12 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-08 History/analytics/retention | ⬜ BACKLOG | 0/18 | 0% | ✅ | — | — | — | N/A | N/A |
| EPIC-09 Privacy/locality/pass-through | 🟦 IN PROGRESS | 13/14 | 93% | ✅ | ◐ | ◐ | ◐ | N/A | N/A |
| Other initial-release epics | ⬜ BACKLOG | 0/67 | 0% | ◐ | — | — | — | N/A | N/A |
| **Initial release** | **🟦 IN PROGRESS** | **28/139** | **20.1%** | **◐** | **◐** | **◐** | **◐** | **N/A** | **N/A** |
| Canonical backlog | ⬜ BACKLOG | 0/25 | 0% | ◐ | — | — | — | —* | —* |
| **Full roadmap** | **🟦 IN PROGRESS** | **28/164** | **17.1%** | **◐** | **◐** | **◐** | **◐** | **—*** | **—*** |

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
