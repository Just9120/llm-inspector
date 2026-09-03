# Delivery plan

> Dashboard status: `GOAL-005 IN_PROGRESS`
> Updated: `2026-09-03T23:38:00Z`

## Current Goal

### `GOAL-005 — Завершить согласованный Windows delivery scope`

- **State:** `IN_PROGRESS` (BACKLOG-01 lifecycle increment).
- **Authorization source:** explicit user instructions от `2026-09-03`–`2026-09-04`: реализовывать оставшиеся AC в одной Goal, разносить эпики по отдельным PR, не удерживать safe implementation PR из-за внешнего manual gate, затем выполнить manual validation; все performance, release, lifecycle, remote и version-policy decisions согласованы последовательно.
- **Verified base:** `origin/main` / local `main` = `8aae2fbdd69ae8d8d2c1dd0b4796d1bea6883479`; EPIC-01 release-fix PR [#22](https://github.com/Just9120/llm-inspector/pull/22), exact-main CI [33814980537](https://github.com/Just9120/llm-inspector/actions/runs/33814980537) and trusted-tag release [33815294790](https://github.com/Just9120/llm-inspector/actions/runs/33815294790) succeeded.
- **Canonical denominator:** initial release `139`, full roadmap `164`; completed на base — `132/139` и `138/164`.
- **Active implementation denominator:** из исходных `21` AC локально выполнены `B01-AC01..05`; остаются `16`: `E01-AC01` (`1`) + `E12-AC01..06` (`6`) + `B02-AC01..09` (`9`).

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
| Updated UTC | `2026-09-03T23:38:00Z` |
| Expected base branch | `main` |
| Base SHA | `8aae2fbdd69ae8d8d2c1dd0b4796d1bea6883479` — verified local/`origin/main`; exact-main CI `33814980537` and release `33815294790` success |
| Working branch | `codex/goal-005-backlog-01-lifecycle` |
| Last verified revision | `1319b37299b0827a4c5a990d538bedb79f678d62` — full local CI-equivalent succeeded; this checkpoint commit will not self-reference its own containing revision |
| Initial worktree state | Clean branch created from verified `origin/main`; unrelated changes absent |
| Completed work | EPIC-01 forward-fix PR #22 head `5ef5f56`, PR CI `33814760385`, merge `8aae2fbdd69ae8d8d2c1dd0b4796d1bea6883479`, exact-main CI `33814980537`; immutable `v1.0.0-rc.2` release run `33815294790` полностью успешен, exact downloaded artifact/sums/manifest/SPDX/Sigstore attestations verified. B01 code commit `327bdd3a2011eb7c6f84299dc3945e2d4b4e95b5` implements confirmation-first start/stop/restart/model-load/parameter flows for three built-ins, exact owned-process runtime, compatibility matrix and tests |
| Current step | Create final checkpoint commit, revalidate exact local head, fetch-check unchanged base, then one initial push and B01 PR |
| Next exact action | Re-run full CI-equivalent on final checkpoint head, verify `origin/main` remains `8aae2fb…`, push once and open B01 PR |
| PR / CI | B01 PR not created; exact-head CI pending |
| Deployment | N/A; server/runtime CD remains disabled |
| Validation | `v1.0.0-rc.2` has 6 exact assets; executable SHA-256 `4e78ee7…e683`, manifest source SHA exact, SPDX 2.3 `32` packages/`1` file, both bundles parse, `gh attestation verify` and downloaded smoke pass. B01 at `1319b37`: exact SDK `10.0.400`, locked normal/RID restores, format, build `0` warnings/errors, full `244/244` tests with zero skips, clean one-file self-contained publish (`62,806,564` bytes) and smoke pass |
| Blockers | None for safe B01 PR. llama.cpp `b10516` and LM Studio/lms actual runtime tests remain manual gates and are not marked verified. Windows Home/Pro exact release checks remain an E01 gate. `docs/ci-cd-rules.md` profile remains stale (`windows_release.enabled: false`) until explicit CI/CD policy-document request |
| Unverified assumptions | No current external-runtime claim beyond locally verified Ollama/model/hardware facts; actual llama.cpp, LM Studio, Tailscale and client results remain unverified |
| Preserved pre-existing changes | None; branch began clean from verified base |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-03T23:34:29Z` | `132/139 = 95.0%` | `143/164 = 87.2%` | Independent AC-by-AC calculation on B01 code commit `327bdd3`: `B01-AC01..05` have CODE/TEST evidence; exact-head CI pending. E01 remains `3/4` because Home/Pro manual Evidence is absent despite successful rc2 publication |
| Previous | `2026-09-03T22:39:21Z` | `132/139 = 95.0%` | `138/164 = 84.1%` | Independent calculation after PR #21/failed rc1 publication, before successful rc2 and B01 implementation |

Delta: `0.0 п.п.` initial, `+3.1 п.п.` full. Denominators unchanged; five B01 AC now pass locally. `E01-AC01` remains binary-fail until the exact-artifact Windows matrix passes.

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
| BACKLOG-01 | 🟦 IN PROGRESS | 5/5 | 100% | ✅ | ✅ | ✅ | — | N/A | N/A |
| BACKLOG-02 | ⬜ BACKLOG | 0/9 | 0% | ✅ | — | — | — | N/A | — |
| BACKLOG-03 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-04 | ⬜ BACKLOG | 0/2 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-05 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| BACKLOG-06 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| **Full roadmap** | **🟦 IN PROGRESS** | **143/164** | **87.2%** | **✅** | **◐** | **◐** | **◐** | **N/A** | **—** |

## Active AC and gate map

| AC | Current fact / completion gate |
|---|---|
| `E01-AC01` | Not complete: `rc.2` publication/payload/SBOM/provenance pass; Windows Home/Pro exact-artifact Evidence absent |
| `E12-AC01..06` | Not complete: profiles, fail-closed evaluator and frozen fixture merged through PR #20; controlled results for every built-in profile absent |
| `B01-AC01..05` | Locally complete at `327bdd3`: implementation plus `178/178` focused tests pass; exact-head PR/main CI pending, llama.cpp/LM Studio remain non-verified compatibility targets |
| `B02-AC01..09` | Not complete: remote security contract approved; implementation and required LIVE Evidence absent |
| `B03-AC01..03` | Conditional backlog; no current demand/authorization |
| `B04-AC01..02` | Conditional backlog; current named clients require no new protocol |

## Candidate next Goals

- Store/MSIX/trusted signing/automatic Store updates — release backlog proposal only.
- Linux/macOS support — only after explicit product demand.
- Additional protocol — only after a supported client demonstrably requires one.

Эти candidates не авторизуют implementation. Пока `GOAL-005` active, следующая Goal не выбирается.
