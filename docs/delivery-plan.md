# Delivery plan

> Dashboard status: `NO ACTIVE GOAL`
> Updated: `2026-09-04T06:59:16Z`

## Current Goal

Активная Goal отсутствует. `GOAL-005 — Завершить согласованный Windows delivery scope` остановлена в предусмотренном terminal state `PENDING_EXTERNAL_GATE`: весь безопасно выполнимый implementation scope доставлен, а отсутствующие обязательные проверки требуют внешней среды или длительного controlled run. Полный terminal record перенесён в [`delivery-plan-archive.md`](delivery-plan-archive.md#goal-005--завершить-согласованный-windows-delivery-scope).

- **Последняя authorization:** explicit user instructions от `2026-09-03`–`2026-09-04`, включая terminal reconciliation и удаление всех branches кроме `main` и `release/v1.0`.
- **Stop condition:** выполнена ветка `PENDING_EXTERNAL_GATE`; `DONE` не заявляется без Windows Home, controlled E12 measurements и обязательного B02 LIVE Evidence.
- **Implementation authorization:** отсутствует. Candidate Goals ниже являются только предложениями.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | `2026-09-04T06:59:16Z` |
| Expected base branch | `main` |
| Base SHA | `d2c3df58fb111ce62968b6144cf58720ab036053` — verified local/`origin/main`, clean before reconciliation branch creation |
| Release line | `origin/release/v1.0` = `821b17abf68bb63dd09f83a834d2d3bdec2e899c`; annotated `v1.0.0-rc.3` peels to the same commit |
| Last verified product revision | `d2c3df58fb111ce62968b6144cf58720ab036053`; this terminal metadata commit does not self-reference |
| Worktree state | No unrelated or pre-existing changes; one reconciliation branch is used only for this closeout PR and must be deleted after merge |
| Completed work | All GOAL-005 code increments merged. Observation-only `rc.3` published from isolated `release/v1.0`; B01 lifecycle and B02 secure remote remain on `main`/future `v1.1` and are absent from `v1.0` artifact |
| Current step | Terminal reconciliation and branch cleanup requested explicitly by the user |
| Next exact action | After terminal PR/CI/merge, verify only `main` and `release/v1.0` locally/remotely, then await explicit authorization for a new Goal |
| Validation / Evidence | Exact-main CI `33840821568` success (`260/260`); release-line CI `33842121186` success (`225/225`); trusted release run `33842346524` success; exact public executable smoke and Windows Pro Ollama/OpenCode/Hermes E2E pass |
| PR / CI | PR #27 merged as `d2c3df58fb111ce62968b6144cf58720ab036053`; PR #28 merged into `release/v1.0` as `821b17abf68bb63dd09f83a834d2d3bdec2e899c`; terminal release Evidence: [PR #28 comment](https://github.com/Just9120/llm-inspector/pull/28#issuecomment-5536465281) |
| Release artifact | [`v1.0.0-rc.3`](https://github.com/Just9120/llm-inspector/releases/tag/v1.0.0-rc.3), executable SHA-256 `8816be54377101d73030e5a876a61b971f21ec9783c9c36905e1df9054ec2c48`; exact 6-asset set, `3/3` checksums, SPDX 2.3 (`32` packages / `1` file), provenance and SBOM Sigstore verification pass |
| Deployment | N/A; server/runtime CD remains disabled |
| External gates | Windows Home `25H2` exact-artifact matrix; controlled E12 measurements for all three built-in profiles; Tailscale encrypted two-host LIVE; llama.cpp `b10516`; LM Studio/lms; Open WebUI E2E |
| Unverified assumptions | No result is inferred for an unavailable target. Windows Pro evidence covers exact public `rc.3` direct Ollama/OpenCode/Hermes flows, not Windows Home or unavailable clients/runtimes |
| Preserved pre-existing changes | None |

## Project readiness snapshots

| Snapshot | Timestamp | Initial release | Full agreed roadmap | Denominator и основание |
|---|---|---:|---:|---|
| Current | `2026-09-04T06:59:16Z` | `132/139 = 95.0%` | `152/164 = 92.7%` | Independent AC-by-AC calculation on exact merged `main` `d2c3df5` plus exact `rc.3` release/runtime Evidence. E01 remains `3/4`; E12 remains `7/13`; no partial AC credit |
| Previous | `2026-09-04T05:26:01Z` | `132/139 = 95.0%` | `152/164 = 92.7%` | Independent calculation before PR #27/#28 terminal delivery; denominators and completed AC were identical |

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

- Manual validation `v1.0.0-rc.3`: full exact-artifact Windows Pro/Home walkthrough, controlled E12 profiles and available runtime/client integrations; defect fixes only when reproduced.
- WinGet publication readiness: license decision, stable version, multi-file manifests and isolated install/uninstall validation after product validation.
- Release workflow maintenance: replace deprecated `actions/attest-sbom` with the supported `actions/attest` path without changing release trust boundaries.

Эти candidates не авторизуют implementation. Новая Goal начинается только после explicit user approval.
