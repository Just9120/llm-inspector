# Delivery plan

> Dashboard status: `GOAL-005 IN_PROGRESS`
> Updated: `2026-09-03T21:44:21Z`

## Current Goal

### `GOAL-005 — Завершить согласованный Windows delivery scope`

- **State:** `IN_PROGRESS` (EPIC-12 implementation increment).
- **Authorization source:** explicit user instructions от `2026-09-03`–`2026-09-04`: реализовывать оставшиеся AC в одной Goal, разносить эпики по отдельным PR, не удерживать safe implementation PR из-за внешнего manual gate, затем выполнить manual validation; все performance, release, lifecycle, remote и version-policy decisions согласованы последовательно.
- **Verified base:** `origin/main` / local `main` = `97866b3c69d60105bfe57fec94f4dbb6b9981ad7`; decision PR [#19](https://github.com/Just9120/llm-inspector/pull/19) and exact-main CI [33807628059](https://github.com/Just9120/llm-inspector/actions/runs/33807628059) succeeded.
- **Canonical denominator:** initial release `139`, full roadmap `164`; completed на base — `132/139` и `138/164`.
- **Active implementation denominator:** `21` ещё не выполненный и авторизованный AC: `E01-AC01` (`1`) + `E12-AC01..06` (`6`) + `B01-AC01..05` (`5`) + `B02-AC01..09` (`9`).

**Scope**

- Ратифицировать уже согласованные durable decisions без readiness credit за documentation-only work.
- Реализовать каждый из четырёх затронутых эпиков отдельным reviewable PR: E12, E01/release, B01, B02.
- До появления lifecycle controls зафиксировать immutable observation-only `v1.0.0-rc.1` candidate exact SHA/artifact; B01 входит только в линию `v1.1`.
- После code increments выполнить общую manual-validation phase по exact artifacts/configuration; каждый недоступный внешний target оставить `PENDING_EXTERNAL_GATE`, не подменяя Evidence.
- После каждого merge подтвердить exact-main CI, синхронизировать local `main`, безопасно удалить merged branch и только затем начать следующий increment.

**Non-goals**

- `BACKLOG-03` Linux/macOS (`3` AC): demand не подтверждён, implementation не требуется сейчас.
- `BACKLOG-04` additional protocols (`2` AC): OpenCode, Hermes и Open WebUI используют существующий OpenAI-compatible contract; `/v1/responses`, Anthropic Messages и Ollama native generation не добавляются.
- Microsoft Store/MSIX/trusted signing/automatic Store updates; backend/model download/install/update; automatic backend restart after crash.
- Arbitrary command/args/env execution, privileged service management, public listener/Funnel/direct backend Internet exposure.
- Изменение остальных canonical AC, privacy/fault-isolation rules или CI/CD safety gates.

**Goal acceptance criteria**

1. Все `21/21` active AC выполнены либо Goal остановлена в `PENDING_EXTERNAL_GATE` с exact отсутствующим Evidence; частичный AC не получает credit.
2. Каждый epic increment проходит focused tests, полный local CI-equivalent, PR exact-head CI, merge и exact-main CI.
3. `v1.0.0-rc.1` observation-only candidate фиксируется до merge B01; `v1.1` lifecycle code не попадает в candidate artifact.
4. Manual phase проверяет exact frozen artifacts/configuration и не смешивает v1.0/v1.1 Evidence.
5. Readiness пересчитывается только как completed canonical AC / `139` и / `164`; завершение active scope даёт максимум `139/139` initial и `159/164` full, потому что B03/B04 явно остаются backlog.
6. Terminal documentation содержит exact commits, PR/CI, artifact hashes, manual results и unresolved gates без metadata-only PR loop.

**Required Evidence**

- `E12-AC01..06`: SPEC/CODE/TEST/CI `✅` плюс controlled reference benchmark results для каждого built-in profile.
- `E01-AC01`: SPEC/CODE/TEST/CI `✅`, exact artifact SHA-256/SBOM/provenance и manual Windows 11 `25H2` Home/Pro run Evidence; DEPLOY/LIVE `N/A`.
- `B01-AC01..05`: SPEC/CODE/TEST/CI `✅`; Ollama `0.33.2` verified, llama.cpp `b10516` и LM Studio/lms target versions требуют отдельного actual-runtime Evidence до compatibility claim.
- `B02-AC01..09`: security review и SPEC/CODE/TEST/CI `✅`, DEPLOY `N/A`, LIVE `✅` только по фактическому encrypted Windows↔VPS/second-PC test.

**Known external gates**

- Windows Home `25H2` exact-artifact run (ожидается nested VM); Windows Pro доступен локально.
- llama.cpp `b10516` и LM Studio/lms target runtime ещё не установлены/проверены.
- Tailscale second PC/VPS encrypted E2E пока недоступен.
- OpenCode, Hermes и Open WebUI manual E2E могут завершаться по одному; непроверенный client остаётся `PENDING_EXTERNAL_GATE`.

**Stop condition:** `DONE` после `21/21` с required Evidence; `PENDING_EXTERNAL_GATE`, если safe implementation завершена, но обязательный manual/LIVE target недоступен; `BLOCKED` только при impasse, не сводящемся к ожидаемому внешнему gate. После terminal state не переходить к следующей Goal без explicit authorization.

## Working pipeline

| Order | Increment | Exit gate |
|---:|---|---|
| 0 | Decision ratification | Documentation PR merged; exact-main CI success; denominator unchanged |
| 1 | EPIC-12 performance profiles/harness | Built-in profiles и immutable fixture implemented/tested; controlled measurements deferred only as explicit external gate |
| 2 | EPIC-01 portable release | Reproducible single-file artifact, SHA-256, SBOM/provenance and release validation automation |
| 3 | Freeze v1.0 candidate | Immutable observation-only `v1.0.0-rc.1` exact SHA/artifact before B01 merge |
| 4 | BACKLOG-01 lifecycle | Inspector-owned process controls, allowlisted parameters, compatibility matrix; no v1.0 contamination |
| 5 | BACKLOG-02 secure remote | Loopback Inspector + Tailscale Serve guidance/token boundary, remote backend semantics and tests |
| 6 | Manual validation | E12 controlled profiles; Windows Home/Pro; Ollama/llama.cpp/LM Studio; Tailscale; OpenCode/Hermes/Open WebUI |
| 7 | Terminal reconciliation | Final readiness/Evidence, Goal `DONE` or `PENDING_EXTERNAL_GATE`, local main sync/cleanup |

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-03T21:44:21Z` |
| Expected base branch | `main` |
| Base SHA | `97866b3c69d60105bfe57fec94f4dbb6b9981ad7` — verified local/`origin/main`; exact-main CI `33807628059` success |
| Working branch | `codex/goal-005-epic-12-performance` |
| Last verified revision | `ccf6a5a` — local candidate: solution Release build succeeded; focused UnitTests `71/71`, WindowsTests `64/64`, PerformanceTests `6/6`, zero skips; full CI-equivalent pending |
| Initial worktree state | Clean branch created from verified `origin/main`; unrelated changes absent |
| Completed work | Decision ratification terminal via PR #19: head `f82ec878986ddbc19b7e93f218f4ebe440683ad8`, PR CI `33807351764`, merge `97866b3c69d60105bfe57fec94f4dbb6b9981ad7`, exact-main CI `33807628059`, Evidence [comment](https://github.com/Just9120/llm-inspector/pull/19#issuecomment-5532345500). EPIC-12 local commits: `556b1bd` profiles/settings/UI/runtime; `ccf6a5a` fail-closed evaluator and immutable fixture SHA-256 `1c38874fb393cfe094bf1d44a281859c2a6e340b9acb46b86df3b458a41f3aca` |
| Current step | Synchronize documentation, run full local CI-equivalent and review the complete EPIC-12 diff |
| Next exact action | Execute locked restore → format → Release build → full tests → RID restore → publish → smoke on exact local head |
| PR / CI | EPIC-12 PR not yet created; initial push and exact-head CI pending |
| Deployment | N/A; server/runtime CD remains disabled |
| Validation | Exact SDK `10.0.400`; local candidate Release build `0` warnings/errors; focused UnitTests `71/71`, WindowsTests `64/64`, PerformanceTests `6/6`, zero skips; format verification passed. Full tests/publish/smoke pending |
| Blockers | None for safe EPIC-12 PR. Controlled measurements for all three built-in profiles remain the explicit external gate and therefore no `E12-AC01..06` credit is claimed |
| Unverified assumptions | No current external-runtime claim beyond locally verified Ollama/model/hardware facts; actual llama.cpp, LM Studio, Tailscale and client results remain unverified |
| Preserved pre-existing changes | None; branch began clean from verified base |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-03T21:44:21Z` | `132/139 = 95.0%` | `138/164 = 84.1%` | Independent AC-by-AC calculation on local EPIC-12 candidate `ccf6a5a`; implementation mechanics exist, but all six active performance AC require missing controlled measurements |
| Previous | `2026-09-03T20:59:53Z` | `132/139 = 95.0%` | `138/164 = 84.1%` | Independent calculation on pre-ratification exact merged `ecdb542…`; PR #19 later closed SPEC decisions without product AC credit |

Delta: `0.0 п.п.` initial и full. Denominators unchanged; profiles/evaluator/fixture are supporting CODE/TEST evidence, while `E12-AC01..06` each remain binary-fail until their controlled measurement gate passes.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-01 | 🟦 IN PROGRESS | 3/4 | 75% | ✅ | ◐ | ◐ | ◐ | N/A | N/A |
| EPIC-02 | 🟩 READY | 15/15 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-03 | 🟩 READY | 13/13 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-04 | 🟩 READY | 12/12 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-05 | 🟩 READY | 8/8 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-06 | 🟩 READY | 8/8 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-07 | 🟩 READY | 16/16 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-08 | 🟩 READY | 18/18 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-09 | 🟩 READY | 14/14 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-10 | 🟩 READY | 8/8 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-11 | 🟩 READY | 10/10 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| EPIC-12 | 🟦 IN PROGRESS | 7/13 | 53.8% | ✅ | ◐ | ◐ | ◐ | N/A | N/A |
| **Initial release** | **🟦 IN PROGRESS** | **132/139** | **95.0%** | **✅** | **◐** | **◐** | **◐** | **N/A** | **N/A** |
| BACKLOG-01 | ⬜ BACKLOG | 0/5 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-02 | ⬜ BACKLOG | 0/9 | 0% | ✅ | — | — | — | N/A | — |
| BACKLOG-03 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-04 | ⬜ BACKLOG | 0/2 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-05 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| BACKLOG-06 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| **Full roadmap** | **🟦 IN PROGRESS** | **138/164** | **84.1%** | **✅** | **◐** | **◐** | **◐** | **N/A** | **—** |

## Active AC and gate map

| AC | Current fact / completion gate |
|---|---|
| `E01-AC01` | Not complete: portable release contract approved; exact artifact/automation and Windows Home/Pro manual Evidence absent |
| `E12-AC01..06` | Not complete: profiles, fail-closed evaluator and frozen fixture implemented locally; full PR CI and controlled results for every built-in profile absent |
| `B01-AC01..05` | Not complete: lifecycle/version contract approved; implementation and runtime Evidence absent |
| `B02-AC01..09` | Not complete: remote security contract approved; implementation and required LIVE Evidence absent |
| `B03-AC01..03` | Conditional backlog; no current demand/authorization |
| `B04-AC01..02` | Conditional backlog; current named clients require no new protocol |

## Candidate next Goals

- Store/MSIX/trusted signing/automatic Store updates — release backlog proposal only.
- Linux/macOS support — only after explicit product demand.
- Additional protocol — only after a supported client demonstrably requires one.

Эти candidates не авторизуют implementation. Пока `GOAL-005` active, следующая Goal не выбирается.
