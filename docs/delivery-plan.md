# Delivery plan

> Dashboard status: `GOAL-005 IN_PROGRESS`
> Updated: `2026-09-04T05:48:12Z`

## Current Goal

### `GOAL-005 — Завершить согласованный Windows delivery scope`

- **State:** `IN_PROGRESS` (EPIC-01 observation-only `v1.0.0-rc.3` forward-fix increment).
- **Authorization source:** explicit user instructions от `2026-09-03`–`2026-09-04`: реализовывать оставшиеся AC в одной Goal, разносить эпики по отдельным PR, не удерживать safe implementation PR из-за внешнего manual gate, затем выполнить manual validation; все performance, release, lifecycle, remote и version-policy decisions согласованы последовательно.
- **Verified base:** `origin/release/v1.0` / local `release/v1.0` = `8aae2fbdd69ae8d8d2c1dd0b4796d1bea6883479`, exact source of immutable public `v1.0.0-rc.2`; release run [33815294790](https://github.com/Just9120/llm-inspector/actions/runs/33815294790) succeeded. Lifecycle/remote commits from current `main` are intentionally outside this maintenance line.
- **Canonical denominator:** initial release `139`, full roadmap `164`; completed на base — `132/139` и `138/164`.
- **Active implementation denominator:** branch-local increment не добавляет новых product AC и обслуживает только pending `E01-AC01`; исходная GOAL-005 denominator остаётся `21`, а выполненные позднее B01/B02 increments существуют только в `main` и не backport-ятся.

**Scope**

- Ратифицировать уже согласованные durable decisions без readiness credit за documentation-only work.
- Реализовать каждый из четырёх затронутых эпиков отдельным reviewable PR: E12, E01/release, B01, B02.
- Immutable observation-only `v1.0.0-rc.2` остаётся неизменным. Explicit decision от `2026-09-04` разрешает создать от его exact source maintenance line `release/v1.0`, перенести только release infrastructure, tray forward fix и необходимую CI test stabilization, затем выпустить `v1.0.0-rc.3`; B01 остаётся только в линии `v1.1` из `main`.
- После code increments выполнить общую manual-validation phase по exact artifacts/configuration; каждый недоступный внешний target оставить `PENDING_EXTERNAL_GATE`, не подменяя Evidence.
- После maintenance merge подтвердить exact-`release/v1.0` CI, выпустить/проверить exact `rc.3`, затем синхронизировать local branches и безопасно удалить merged feature branch.

**Non-goals**

- `BACKLOG-03` Linux/macOS (`3` AC): demand не подтверждён, implementation не требуется сейчас.
- `BACKLOG-04` additional protocols (`2` AC): OpenCode, Hermes и Open WebUI используют существующий OpenAI-compatible contract; `/v1/responses`, Anthropic Messages и Ollama native generation не добавляются.
- Microsoft Store/MSIX/trusted signing/automatic Store updates; backend/model download/install/update; automatic backend restart after crash.
- Arbitrary command/args/env execution, privileged service management, public listener/Funnel/direct backend Internet exposure.
- Изменение остальных canonical AC, privacy/fault-isolation rules или CI/CD safety gates.

**Goal acceptance criteria**

1. Все `21/21` active AC выполнены либо Goal остановлена в `PENDING_EXTERNAL_GATE` с exact отсутствующим Evidence; частичный AC не получает credit.
2. Каждый epic increment проходит focused tests, полный local CI-equivalent, PR exact-head CI, merge и exact-main CI.
3. Observation-only lineage сохраняется: `release/v1.0` начинается от exact `v1.0.0-rc.2` source, принимает только reviewed release infrastructure/forward fixes, а новый candidate получает version `v1.0.0-rc.3`; `v1.1` lifecycle code не попадает в artifact.
4. Manual phase проверяет exact frozen artifacts/configuration и не смешивает v1.0/v1.1 Evidence.
5. Readiness пересчитывается только как completed canonical AC / `139` и / `164`; завершение active scope даёт максимум `139/139` initial и `159/164` full, потому что B03/B04 явно остаются backlog.
6. Terminal documentation содержит exact commits, PR/CI, artifact hashes, manual results и unresolved gates без metadata-only PR loop.

**Required Evidence**

- `E12-AC01..06`: SPEC/CODE/TEST/CI `✅` плюс controlled reference benchmark results для каждого built-in profile.
- `E01-AC01`: SPEC/CODE/TEST/CI `✅`, exact artifact SHA-256/SBOM/provenance и manual Windows 11 `25H2` Home/Pro run Evidence; DEPLOY/LIVE `N/A`.
- `B01-AC01..05`: SPEC/CODE/TEST/CI `✅`; Ollama `0.33.2` verified, llama.cpp `b10516` и LM Studio/lms target versions требуют отдельного actual-runtime Evidence до compatibility claim.
- `B02-AC01..09`: security review и SPEC/CODE/TEST/CI `✅`, DEPLOY `N/A`, LIVE `✅` только по фактическому encrypted Windows↔VPS/second-PC test.

**Known external gates**

- Windows Home `25H2` exact-artifact run ожидает nested VM. Exact public `rc.2` на Windows Pro `25H2` build `26200.9168` упал в critical proxy flow; merged-main hotfix reproduction прошёл, но новый `rc.3` artifact ещё не опубликован.
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
| 3 | Freeze v1.0 candidate | Immutable public `rc.2` сохранён; forward fix через isolated `release/v1.0` / `rc.3` |
| 4 | BACKLOG-01 lifecycle | Inspector-owned process controls, allowlisted parameters, compatibility matrix; no v1.0 contamination |
| 5 | BACKLOG-02 secure remote | Loopback Inspector + Tailscale Serve guidance/token boundary, remote backend semantics and tests |
| 6 | Manual validation | E12 controlled profiles; Windows Home/Pro; Ollama/llama.cpp/LM Studio; Tailscale; OpenCode/Hermes/Open WebUI |
| 7 | Terminal reconciliation | Final readiness/Evidence, Goal `DONE` or `PENDING_EXTERNAL_GATE`, local main sync/cleanup |

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-04T05:48:12Z` |
| Expected base branch | `release/v1.0` |
| Base SHA | `8aae2fbdd69ae8d8d2c1dd0b4796d1bea6883479` — verified local/`origin/release/v1.0` and `v1.0.0-rc.2^{commit}` identity |
| Working branch | `codex/goal-005-v1.0.0-rc.3` |
| Last verified revision | `48ee5252cc7d94491058b7f1924c9fa4e8d60d9f` — three isolated reviewed changes plus branch-local release documentation; full local CI-equivalent pass |
| Initial worktree state | Clean feature branch created from verified `origin/release/v1.0`; unrelated changes absent |
| Completed work | Public `rc.2` run `33815294790` published six exact assets and executable SHA-256 `4e78ee7cdcde7eb6188d8299f9576447b65faad7439f839e739e32048bd7e683`; payload/SBOM/provenance/smoke pass, but Pro critical proxy flow exposed nonexistent `ShellNotifyIconW`. Maintenance branch now contains deterministic tag-to-line mapping commit `3269292`, exact exported `Shell_NotifyIconW` fix `47d37ec` with native-symbol regression test, and resource-monitor fixture stabilization `e7a685d` required after confirmed main CI race. No lifecycle/remote implementation is present |
| Current step | Fetch-check unchanged `origin/release/v1.0`, inspect the exact four-commit diff, then perform the one initial push and open PR targeting `release/v1.0` |
| Next exact action | Fetch `origin`, verify base `8aae2fb` and clean worktree, push once, then create the maintenance PR |
| PR / CI | Maintenance PR not created; initial push and exact-head CI pending. Main infrastructure/fix PRs #26/#27 and exact-main run `33840821568` are terminal success but are supporting Evidence, not ancestry of this branch |
| Deployment | N/A; server/runtime CD remains disabled |
| Validation | Exact SDK `10.0.400`; locked normal/RID restores; format clean; Release build `0` warnings/errors; full branch-local `225/225` (`74` Unit + `53` Integration + `65` Windows + `20` Contract + `7` Privacy + `6` Performance), zero skips; one-file win-x64 publish SHA-256 `e8a508f74b5e683a54dbd090c301f9e3a8c22ad62b2903494b6dbe39eb51f73d`, smoke `exit 0`. Focused policy `5/5`, tray symbol `1/1`, repeated resource monitor `20/20` also pass |
| Blockers | None for safe maintenance PR. Corrected `rc.3` release/attestation and Windows Home/Pro exact-artifact tests remain delivery/manual gates |
| Unverified assumptions | New exact release artifact does not yet exist; Windows Home, llama.cpp, LM Studio, Tailscale and Open WebUI remain unverified external targets. OpenCode/Hermes results from merged main hotfix must be rerun against exact `rc.3` before release Evidence |
| Preserved pre-existing changes | None; branch began clean from verified base |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-04T05:42:42Z` | `132/139 = 95.0%` | `138/164 = 84.1%` | Independent AC-by-AC calculation for exact observation-only maintenance lineage: `rc.2` publication passed but exact Pro artifact failed; local forward fix adds no AC credit until a new exact-artifact Windows matrix passes |
| Previous | `2026-09-04T00:57:51Z` | `132/139 = 95.0%` | `138/164 = 84.1%` | Same branch-local denominator at immutable `rc.2` source `8aae2fb`; public artifact failure kept `E01-AC01` uncredited |

Delta: `0.0 п.п.` initial и full. Denominators unchanged; release automation is supporting CODE/TEST evidence, while `E01-AC01` remains binary-fail until the exact-artifact Windows matrix passes.

## Epic readiness и Evidence

| Epic | Status | Completed / total | Readiness | SPEC | CODE | TEST | CI | DEPLOY | LIVE |
|---|---|---:|---:|---|---|---|---|---|---|
| EPIC-01 | 🟦 IN PROGRESS | 3/4 | 75% | ✅ | ◐ | ❌ | ◐ | N/A | N/A |
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
| **Initial release** | **🟦 IN PROGRESS** | **132/139** | **95.0%** | **✅** | **◐** | **❌** | **◐** | **N/A** | **N/A** |
| BACKLOG-01 | ⬜ BACKLOG | 0/5 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-02 | ⬜ BACKLOG | 0/9 | 0% | ✅ | — | — | — | N/A | — |
| BACKLOG-03 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-04 | ⬜ BACKLOG | 0/2 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-05 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| BACKLOG-06 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| **Full roadmap** | **🟦 IN PROGRESS** | **138/164** | **84.1%** | **✅** | **◐** | **❌** | **◐** | **N/A** | **—** |

## Active AC and gate map

| AC | Current fact / completion gate |
|---|---|
| `E01-AC01` | Not complete: `rc.2` publication/payload/SBOM/provenance pass, but exact Pro artifact fails critical proxy flow; current `release/v1.0` branch contains local forward fix, while reviewed merge, `rc.3` publication and Home/Pro exact-artifact Evidence are absent |
| `E12-AC01..06` | Not complete: profiles, fail-closed evaluator and frozen fixture merged through PR #20; controlled results for every built-in profile absent |
| `B01-AC01..05` | Not complete: lifecycle/version contract approved; implementation and runtime Evidence absent |
| `B02-AC01..09` | Not complete: remote security contract approved; implementation and required LIVE Evidence absent |
| `B03-AC01..03` | Conditional backlog; no current demand/authorization |
| `B04-AC01..02` | Conditional backlog; current named clients require no new protocol |

## Candidate next Goals

- Store/MSIX/trusted signing/automatic Store updates — release backlog proposal only.
- Linux/macOS support — only after explicit product demand.
- Additional protocol — only after a supported client demonstrably requires one.

Эти candidates не авторизуют implementation. Пока `GOAL-005` active, следующая Goal не выбирается.
