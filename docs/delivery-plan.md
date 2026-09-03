# Delivery plan

> Dashboard status: `GOAL-005 IN_PROGRESS`
> Updated: `2026-09-03T22:46:11Z`

## Current Goal

### `GOAL-005 — Завершить согласованный Windows delivery scope`

- **State:** `IN_PROGRESS` (EPIC-01 release forward-fix increment).
- **Authorization source:** explicit user instructions от `2026-09-03`–`2026-09-04`: реализовывать оставшиеся AC в одной Goal, разносить эпики по отдельным PR, не удерживать safe implementation PR из-за внешнего manual gate, затем выполнить manual validation; все performance, release, lifecycle, remote и version-policy decisions согласованы последовательно.
- **Verified base:** `origin/main` / local `main` = `ff62f54df4fbd4de443144259ac3d89bddff0044`; EPIC-01 automation PR [#21](https://github.com/Just9120/llm-inspector/pull/21) and exact-main CI [33813413498](https://github.com/Just9120/llm-inspector/actions/runs/33813413498) succeeded.
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
| Updated UTC | `2026-09-03T22:46:11Z` |
| Expected base branch | `main` |
| Base SHA | `ff62f54df4fbd4de443144259ac3d89bddff0044` — verified local/`origin/main`; exact-main CI `33813413498` success |
| Working branch | `codex/goal-005-epic-01-release-fix` |
| Last verified revision | `2044bc09a7dd221c2ac9d69bee76e217b08df672` — full local CI-equivalent succeeded; this checkpoint commit will not self-reference its own containing revision |
| Initial worktree state | Clean branch created from verified `origin/main`; unrelated changes absent |
| Completed work | EPIC-01 PR #21 head `a81d97eb17cd6ce541f1a7d2eeff5508c77a12a8`, PR CI `33812383296`, merge `ff62f54df4fbd4de443144259ac3d89bddff0044`, exact-main CI `33813413498`. Immutable tag `v1.0.0-rc.1` points to that merge. Release run `33813681861`: build/payload/both attestations success; final publication failed because checkout-free `gh` lacked repository identity; no GitHub Release exists. Downloaded exact artifact SHA-256 `3ea61cd4796fe35de16805605f213b2d10ba3e64067f42d3d3351915c2f4bd02` passes payload and GitHub attestation verification. Forward-fix commit `60d15b8` adds final-step `GH_REPO` and policy coverage |
| Current step | Create final checkpoint commit, revalidate exact local head, then one grouped initial push and forward-fix PR |
| Next exact action | Re-run full CI-equivalent on final local head, fetch-check unchanged `origin/main`, push once and open release forward-fix PR |
| PR / CI | Forward-fix PR not yet created; initial push and exact-head CI pending |
| Deployment | N/A; server/runtime CD remains disabled |
| Validation | PR #21 local/PR/main full `224/224` validation succeeded. Release run `33813681861` independently passed trusted-tag ancestry, locked CI-equivalent, single publish/smoke, exact payload verification/upload, build provenance and SBOM attestation; only `gh release create` failed with `fatal: not a git repository`. Downloaded `rc.1` payload verifier and `gh attestation verify` both pass for SHA-256 `3ea61cd4…f4bd02`. Forward-fix at `2044bc0…`: exact SDK `10.0.400`, locked restores, format, build `0` warnings/errors, `224/224` tests with zero skips, exact one-file publish and smoke success |
| Blockers | None for safe forward-fix PR. Successful `v1.0.0-rc.2` release/attestation verification and Windows Home/Pro exact-artifact tests remain delivery/manual gates. `docs/ci-cd-rules.md` profile remains stale (`windows_release.enabled: false`) until an explicit CI/CD policy-document request; safety contract itself is unchanged |
| Unverified assumptions | No current external-runtime claim beyond locally verified Ollama/model/hardware facts; actual llama.cpp, LM Studio, Tailscale and client results remain unverified |
| Preserved pre-existing changes | None; branch began clean from verified base |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-03T22:39:21Z` | `132/139 = 95.0%` | `138/164 = 84.1%` | Independent AC-by-AC calculation after PR #21 and failed `rc.1` publication; CODE/TEST/CI advanced, but `E01-AC01` remains failed without a published exact artifact and Windows Home/Pro Evidence |
| Previous | `2026-09-03T22:15:12Z` | `132/139 = 95.0%` | `138/164 = 84.1%` | Local EPIC-01 candidate passed full validation before PR #21; trusted release and Windows Home/Pro Evidence were absent |

Delta: `0.0 п.п.` initial и full. Denominators unchanged; release automation is supporting CODE/TEST evidence, while `E01-AC01` remains binary-fail until the exact-artifact Windows matrix passes.

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
| `E01-AC01` | Not complete: automation merged and `rc.1` build/payload/attestations passed, but publication failed; forward-fix `rc.2` and Windows Home/Pro Evidence absent |
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
