# Delivery plan

> Dashboard status: `NO ACTIVE GOAL`
> Updated: `2026-09-04T07:51:09Z`

## Current Goal

Активная Goal отсутствует. `GOAL-005 — Завершить согласованный Windows delivery scope` остановлена в предусмотренном terminal state `PENDING_EXTERNAL_GATE`: весь безопасно выполнимый implementation scope доставлен, а отсутствующие обязательные проверки требуют внешней среды или длительного controlled run. Полный terminal record перенесён в [`delivery-plan-archive.md`](delivery-plan-archive.md#goal-005--завершить-согласованный-windows-delivery-scope).

- **Последняя authorization:** explicit user instruction от `2026-09-04`: отменить version branches, оставить единственную line `main`, не публиковать новые prerelease, сохранять product version `1.0` до final `v1.0.0` после required validation; historical `rc.3` не удалять.
- **Stop condition:** выполнена ветка `PENDING_EXTERNAL_GATE`; `DONE` не заявляется без Windows Home, controlled E12 measurements и обязательного B02 LIVE Evidence.
- **Implementation authorization:** отсутствует. Candidate Goals ниже являются только предложениями.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-04T07:51:09Z` |
| Expected base branch | `main` |
| Base SHA | `1c865e0561c45955906c902c6d23a761ed8ad363` — verified local/`origin/main`, clean before single-main policy branch creation |
| Branch policy | `main` is the only persistent branch after the owner-approved cleanup; historical tags/releases remain immutable |
| Last verified product revision | `1c865e0561c45955906c902c6d23a761ed8ad363`; policy changes do not add product AC credit |
| Worktree state | Terminal target is one clean synchronized `main`; the temporary policy branch is deleted after merge/exact-main CI and the exact cleanup record is kept in the merged PR comment |
| Completed work | All GOAL-005 code increments and terminal reconciliation merged. Historical `rc.3` remains public Evidence; subsequent release source is single `main` and the next allowed public version is final `v1.0.0` after required validation |
| Current step | No active execution after owner-authorized single-main/final-only policy reconciliation and obsolete branch cleanup |
| Next exact action | Await explicit authorization for a new bounded Goal; do not create final `v1.0.0` tag before required validation |
| Validation / Evidence | Single-main policy branch local full CI-equivalent success: exact SDK `10.0.400`, `260/260`, no skips, build 0 warnings/errors, RID publish/smoke pass, executable FileVersion `1.0.0.0`; GOAL-005 closeout PR/main CI `33847850161`/`33848084925` success |
| PR / CI | PR #29 merged as `1c865e0561c45955906c902c6d23a761ed8ad363`; historical PR #28/release Evidence remains in [terminal comment](https://github.com/Just9120/llm-inspector/pull/28#issuecomment-5536465281) |
| Release artifact | [`v1.0.0-rc.3`](https://github.com/Just9120/llm-inspector/releases/tag/v1.0.0-rc.3), executable SHA-256 `8816be54377101d73030e5a876a61b971f21ec9783c9c36905e1df9054ec2c48`; exact 6-asset set, `3/3` checksums, SPDX 2.3 (`32` packages / `1` file), provenance and SBOM Sigstore verification pass |
| Deployment | N/A; server/runtime CD remains disabled |
| External gates | Windows Home `25H2` exact-artifact matrix; controlled E12 measurements for all three built-in profiles; Tailscale encrypted two-host LIVE; llama.cpp `b10516`; LM Studio/lms; Open WebUI E2E |
| Unverified assumptions | No result is inferred for an unavailable target. Windows Pro evidence covers exact public `rc.3` direct Ollama/OpenCode/Hermes flows, not Windows Home or unavailable clients/runtimes |
| Preserved pre-existing changes | None |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-04T07:51:09Z` | `132/139 = 95.0%` | `152/164 = 92.7%` | Independent AC-by-AC calculation on exact merged `main` `1c865e0` plus historical exact `rc.3` release/runtime Evidence. Version/branch policy changes no denominator or AC; E01 remains `3/4`, E12 `7/13` |
| Previous | `2026-09-04T06:59:16Z` | `132/139 = 95.0%` | `152/164 = 92.7%` | Independent calculation on exact merged `main` `d2c3df5` plus exact `rc.3` Evidence; denominators and completed AC were identical |

Delta: `0.0 п.п.` initial и `0.0 п.п.` full. Новая Evidence устранила failed `rc.2` implementation path и подтвердила Pro exact-artifact flows, но `E01-AC01` остаётся binary-unfulfilled без полной Home/Pro matrix; controlled E12 results также отсутствуют.

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
| EPIC-12 | 🟦 IN PROGRESS | 7/13 | 53.8% | ✅ | ✅ | ◐ | ✅ | N/A | N/A |
| **Initial release** | **🟦 IN PROGRESS** | **132/139** | **95.0%** | **✅** | **✅** | **◐** | **✅** | **N/A** | **N/A** |
| BACKLOG-01 | 🟩 READY | 5/5 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| BACKLOG-02 | 🟦 IN PROGRESS | 9/9 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | — |
| BACKLOG-03 | ⬜ BACKLOG | 0/3 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-04 | ⬜ BACKLOG | 0/2 | 0% | ✅ | — | — | — | N/A | N/A |
| BACKLOG-05 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| BACKLOG-06 | 🟩 READY | 3/3 | 100% | ✅ | ✅ | ✅ | ✅ | N/A | N/A |
| **Full roadmap** | **🟦 IN PROGRESS** | **152/164** | **92.7%** | **✅** | **◐** | **◐** | **✅** | **N/A** | **—** |

## Remaining gate map

| Surface | Verified state / completion gate |
|---|---|
| `E01-AC01` | Public `rc.3` artifact identity, release pipeline, smoke and Pro Ollama/OpenCode/Hermes flows pass; full Windows Home/Pro release matrix remains required, so AC receives no partial credit |
| `E12-AC01..06` | Profiles, budgets, evaluator and frozen corpus exist with automated CI; controlled reference measurements for Saver/Balanced/Detailed are absent |
| `B01` compatibility | Product AC complete; Ollama `0.33.2` verified, llama.cpp `b10516` and LM Studio/lms remain non-verified targets |
| `B02` LIVE | Product AC complete in code/test/CI; encrypted Windows↔VPS/second-PC LIVE Evidence absent, so epic is not READY |
| Conditional backlog | B03 Linux/macOS and B04 additional protocols remain unimplemented and unauthorized; they do not block the Windows initial-release denominator |

## Candidate next Goals

- Pre-publication validation `v1.0`: build one exact candidate from intended `main`, complete Windows Pro/Home walkthrough, controlled E12 profiles, B02 LIVE and available runtime/client integrations; final `v1.0.0` tag remains blocked until required Evidence.
- WinGet publication readiness: license decision, stable version, multi-file manifests and isolated install/uninstall validation after product validation.
- Release workflow maintenance: replace deprecated `actions/attest-sbom` with the supported `actions/attest` path without changing release trust boundaries.

Эти candidates не авторизуют implementation. Новая Goal начинается только после explicit user approval.
