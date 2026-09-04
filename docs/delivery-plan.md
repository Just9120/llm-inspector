# Delivery plan

> Dashboard status: `GOAL-005 IN_PROGRESS`
> Updated: `2026-09-04T00:34:40Z`

## Current Goal

### `GOAL-005 — Завершить согласованный Windows delivery scope`

- **State:** `IN_PROGRESS` (BACKLOG-02 secure-remote increment).
- **Authorization source:** explicit user instructions от `2026-09-03`–`2026-09-04`: реализовывать оставшиеся AC в одной Goal, разносить эпики по отдельным PR, не удерживать safe implementation PR из-за внешнего manual gate, затем выполнить manual validation; все performance, release, lifecycle, remote и version-policy decisions согласованы последовательно.
- **Verified base:** `origin/main` / local `main` = `7c71fbcb05744e18d95fcb91f6fb90296e0e4030`; BACKLOG-01 PR [#23](https://github.com/Just9120/llm-inspector/pull/23), PR CI `33818573495`, exact-main CI [33819193701](https://github.com/Just9120/llm-inspector/actions/runs/33819193701) succeeded.
- **Canonical denominator:** initial release `139`, full roadmap `164`; completed на base — `132/139` и `143/164`.
- **Active implementation denominator:** из исходных `21` AC локально реализованы `B01-AC01..05` и `B02-AC01..09` (`14/21`); остаются семь manual criteria: `E01-AC01` (`1`) + `E12-AC01..06` (`6`). Для B02 отдельно остаются exact-head/main CI и обязательный LIVE gate, поэтому эпик ещё не READY.

**Scope**

- Ратифицировать уже согласованные durable decisions без readiness credit за documentation-only work.
- Реализовать каждый из четырёх затронутых эпиков отдельным reviewable PR: E12, E01/release, B01, B02.
- До появления lifecycle controls зафиксировать immutable observation-only candidate exact SHA/artifact; failed-publication `v1.0.0-rc.1` не переиспользуется, public candidate — `v1.0.0-rc.2`; B01 входит только в линию `v1.1`.
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
3. Observation-only candidate (`v1.0.0-rc.2`, superseding failed-publication `rc.1`) фиксируется до merge B01; `v1.1` lifecycle code не попадает в candidate artifact.
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
| 3 | Freeze v1.0 candidate | Immutable public observation-only `v1.0.0-rc.2` exact SHA/artifact before B01 merge |
| 4 | BACKLOG-01 lifecycle | Inspector-owned process controls, allowlisted parameters, compatibility matrix; no v1.0 contamination |
| 5 | BACKLOG-02 secure remote | Loopback Inspector + Tailscale Serve guidance/token boundary, remote backend semantics and tests |
| 6 | Manual validation | E12 controlled profiles; Windows Home/Pro; Ollama/llama.cpp/LM Studio; Tailscale; OpenCode/Hermes/Open WebUI |
| 7 | Terminal reconciliation | Final readiness/Evidence, Goal `DONE` or `PENDING_EXTERNAL_GATE`, local main sync/cleanup |

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-04T00:34:40Z` |
| Expected base branch | `main` |
| Base SHA | `7c71fbcb05744e18d95fcb91f6fb90296e0e4030` — verified local/`origin/main`; BACKLOG-01 exact-main CI `33819193701` success |
| Working branch | `codex/goal-005-backlog-02-secure-remote` |
| Last verified revision | `47a82d25efc69b0759667f83285b06709a97cc1b` — both failed CI fixtures pass in five consecutive focused runs; full CI-equivalent on the grouped fix head pending |
| Initial worktree state | Clean branch created from verified `origin/main`; unrelated changes absent |
| Completed work | EPIC-01 `v1.0.0-rc.2` terminal release Evidence preserved; B01 terminal through PR #23 / merge `7c71fbc` / exact-main CI `33819193701`. B02 code commit `e9ab608` implements default-off identity+bearer ingress, DPAPI CurrentUser credential lifecycle, explicit Tailscale HTTPS remote backend, separate availability/connect latency and fail-closed remote resource semantics; secure-remote runbook and operational documentation prepared in the worktree |
| Current step | Validate the single grouped follow-up batch for the confirmed PR CI failure, then push it once and wait for replacement exact-head CI |
| Next exact action | Commit this failure checkpoint, run the full CI-equivalent, fetch-check unchanged base, then perform the one allowed grouped follow-up push to PR #24 |
| PR / CI | PR [#24](https://github.com/Just9120/llm-inspector/pull/24) initial head `6b417a6993fa16680993634d411e326d914d7e02`; run [33822032660](https://github.com/Just9120/llm-inspector/actions/runs/33822032660) failed only in tests: IPv4-listener/`localhost` probe mismatch and a transient post-process-kill SQLite handle race; downstream restore/publish/smoke steps were skipped and are not counted as success |
| Deployment | N/A; server/runtime CD remains disabled |
| Validation | Initial exact-head local run at `6b417a6`: SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, full `259/259`, one-file publish and smoke pass. CI run `33822032660`: restore/format/build success, tests `2` failed / `257` passed / `0` skipped, later steps skipped. Fix `47a82d2` uses exact IPv4 fixture target and waits for actual DB/WAL/SHM handle release; both cases pass `5/5` consecutive focused iterations (`10/10` test executions) |
| Blockers | None for safe B02 PR. Tailscale VPS/second-PC LIVE environment is unavailable. Windows Home/Pro exact release checks and E12 controlled measurements remain manual gates. llama.cpp `b10516`, LM Studio/lms and named clients remain unverified manual compatibility targets. `docs/ci-cd-rules.md` profile remains stale (`windows_release.enabled: false`) until explicit CI/CD policy-document request |
| Unverified assumptions | No current external-runtime claim beyond locally verified Ollama/model/hardware facts; actual llama.cpp, LM Studio, Tailscale and client results remain unverified |
| Preserved pre-existing changes | None; branch began clean from verified base |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-04T00:20:10Z` | `132/139 = 95.0%` | `152/164 = 92.7%` | Independent AC-by-AC calculation on B02 code commit `e9ab608`: `B02-AC01..09` have local CODE/TEST evidence; CI/LIVE gates pending. E01 remains `3/4`; E12 remains `7/13` |
| Previous | `2026-09-03T23:34:29Z` | `132/139 = 95.0%` | `143/164 = 87.2%` | Independent calculation after B01 implementation; subsequent PR #23 and exact-main CI confirmed B01 without changing its five already credited AC |

Delta: `0.0 п.п.` initial, `+5.5 п.п.` full. Denominators unchanged; nine B02 AC now pass locally. Разница меньше 10 п.п.; `E01-AC01` остаётся binary-fail до exact-artifact Windows matrix, а B02 не READY без CI/LIVE.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-01 | 🟦 IN PROGRESS | 3/4 | 75% | ✅ | ✅ | ◐ | ✅ | N/A | N/A |
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
| BACKLOG-01 | 🟩 READY | 5/5 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| BACKLOG-02 | 🟦 IN PROGRESS | 9/9 | 100% | ✅ | ✅ | ✅ | — | N/A | — |
| BACKLOG-03 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-04 | ⬜ BACKLOG | 0/2 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-05 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| BACKLOG-06 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| **Full roadmap** | **🟦 IN PROGRESS** | **152/164** | **92.7%** | **✅** | **◐** | **◐** | **◐** | **N/A** | **—** |

## Active AC and gate map

| AC | Current fact / completion gate |
|---|---|
| `E01-AC01` | Not complete: `rc.2` publication/payload/SBOM/provenance pass; Windows Home/Pro exact-artifact Evidence absent |
| `E12-AC01..06` | Not complete: profiles, fail-closed evaluator and frozen fixture merged through PR #20; controlled results for every built-in profile absent |
| `B01-AC01..05` | Complete: PR #23, merge `7c71fbc` and exact-main CI `33819193701`; llama.cpp/LM Studio remain non-verified compatibility targets rather than false support claims |
| `B02-AC01..09` | Locally implemented at `e9ab608`: focused tests pass; exact-head/main CI and encrypted two-host LIVE Evidence pending, so status remains IN PROGRESS |
| `B03-AC01..03` | Conditional backlog; no current demand/authorization |
| `B04-AC01..02` | Conditional backlog; current named clients require no new protocol |

## Candidate next Goals

- Store/MSIX/trusted signing/automatic Store updates — release backlog proposal only.
- Linux/macOS support — only after explicit product demand.
- Additional protocol — only after a supported client demonstrably requires one.

Эти candidates не авторизуют implementation. Пока `GOAL-005` active, следующая Goal не выбирается.
