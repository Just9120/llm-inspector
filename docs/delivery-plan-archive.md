# Delivery plan archive

Этот файл не является current source of truth, implementation authorization или входом для текущего расчёта readiness. Текущее состояние находится в [`delivery-plan.md`](delivery-plan.md).

## GOAL-005 / BACKLOG-01 — Backend/model lifecycle management

- **Product outcome:** `B01-AC01..05` выполнены (`5/5`); BACKLOG-01 — READY, SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Pull Request:** [#23](https://github.com/Just9120/llm-inspector/pull/23), head `70a1eb80c8ce8713fcb42ad283421a63bb8d5f77`, PR CI `33818573495` success.
- **Merge/exact-main Evidence:** merge `7c71fbcb05744e18d95fcb91f6fb90296e0e4030`; exact-main CI `33819193701` success; terminal record [comment](https://github.com/Just9120/llm-inspector/pull/23#issuecomment-5533635587).
- **Delivered:** confirmation-first lifecycle UX, serialized Inspector-owned process manager, exact Windows process ownership, allowlisted typed adapters and embedded compatibility matrix. Ollama `0.33.2` имеет actual-runtime Evidence; llama.cpp `b10516` и LM Studio/lms остаются non-verified manual targets.
- **Cleanup:** local `main` fast-forwarded to exact merge; created local/remote feature branches safely removed after unique-commit check.

## Replaced readiness snapshots

- `2026-09-03T21:44:21Z`: initial release `132/139 = 95.0%`, full roadmap `138/164 = 84.1%`; EPIC-12 implementation was local and controlled measurements absent. Archived after PR #21 delivery/release attempt produced a newer current/previous pair.
- `2026-09-03T20:59:53Z`: initial release `132/139 = 95.0%`, full roadmap `138/164 = 84.1%`; decision ratification had no product AC credit. Archived when EPIC-01 produced a newer current/previous pair.
- `2026-09-03T10:37:48Z`: initial release `132/139 = 95.0%`, full roadmap `138/164 = 84.1%`; BACKLOG-05 local candidate had implemented `B05-AC01..03`, exact-revision CI was pending. Archived when EPIC-12 produced a newer current/previous pair.
- `2026-09-03T10:02:16Z`: initial release `132/139 = 95.0%`, full roadmap `132/164 = 80.5%`; terminal EPIC-12 state before BACKLOG-06. Archived after BACKLOG-06 terminal Evidence and the independent BACKLOG-05 calculation produced a newer current/previous pair.
- `2026-09-03T09:20:19Z`: initial release `125/139 = 89.9%`, full roadmap `125/164 = 76.2%`; terminal EPIC-11 state before EPIC-12 implementation. Archived after terminal EPIC-12 Evidence and the independent BACKLOG-06 calculation produced a newer current/previous pair.
- `2026-09-03T08:53:36Z`: initial release `115/139 = 82.7%`, full roadmap `115/164 = 70.1%`; EPIC-10 terminal state before EPIC-11 implementation. Archived after terminal EPIC-11 Evidence and the independent EPIC-12 calculation produced a newer current/previous pair.
- `2026-09-03T08:14:33Z`: initial release `107/139 = 77.0%`, full roadmap `107/164 = 65.2%`; EPIC-07 terminal state before EPIC-10 implementation. Archived after terminal EPIC-10 Evidence and the independent EPIC-11 calculation produced a newer current/previous pair.
- `2026-09-03T07:35:19Z`: initial release `91/139 = 65.5%`, full roadmap `91/164 = 55.5%`; EPIC-06 terminal state before EPIC-07 implementation. Archived after terminal EPIC-07 Evidence and the independent EPIC-10 calculation produced a newer current/previous pair.
- `2026-09-03T06:40:21Z`: initial release `83/139 = 59.7%`, full roadmap `83/164 = 50.6%`; EPIC-05 terminal state before EPIC-06 implementation. Archived after terminal EPIC-06 Evidence and the independent EPIC-07 calculation produced a newer current/previous pair.
- `2026-09-03T06:17:58Z`: initial release `75/139 = 54.0%`, full roadmap `75/164 = 45.7%`; terminal EPIC-01 calculation. Archived after EPIC-05 reached terminal delivery and EPIC-06 produced a newer independent calculation.
- `2026-09-03T06:01:33Z`: initial release `72/139 = 51.8%`, full roadmap `72/164 = 43.9%`; exact merged-main recovery before EPIC-01. Archived after EPIC-01 terminal delivery and the independent EPIC-05 calculation.
- `2026-09-03T05:10:51Z`: initial release `70/139 = 50.4%`, full roadmap `70/164 = 42.7%`; terminal EPIC-08 state before the bounded EPIC-04 fix. Archived when GOAL-004 reached terminal `72/72` with exact-main CI.
- `2026-09-02T21:05:52Z`: initial release `0/139 = 0%`, full roadmap `0/164 = 0%`; start-of-GOAL-004 foundation had no completed product AC. Snapshot archived when it ceased to be one of the current/previous independently calculated states.
- `2026-09-02T21:43:13Z`: initial release `13/139 = 9.4%`, full roadmap `13/164 = 7.9%`; only EPIC-09 core AC were complete after PR #3. Archived when EPIC-02 terminal CI produced a newer snapshot without changing its already credited AC.
- `2026-09-02T22:15:32Z`: initial release `28/139 = 20.1%`, full roadmap `28/164 = 17.1%`; EPIC-02 product AC were complete but terminal CI Evidence was not. Archived after EPIC-03 produced a newer independent readiness calculation.
- `2026-09-02T22:48:15Z`: initial release `28/139 = 20.1%`, full roadmap `28/164 = 17.1%`; EPIC-02 was `READY` before EPIC-03 implementation. Archived after initial PR #6 CI produced a newer Evidence snapshot.
- `2026-09-02T23:02:05Z`: initial release `41/139 = 29.5%`, full roadmap `41/164 = 25.0%`; EPIC-03 had local code/test Evidence and pending CI. Archived after terminal EPIC-03 delivery Evidence.
- `2026-09-02T23:12:59Z`: initial release `41/139 = 29.5%`, full roadmap `41/164 = 25.0%`; initial PR #6 CI had exposed an inherited abort-fixture race and the deterministic fix was still local. Archived after the grouped follow-up passed.
- `2026-09-02T23:21:16Z`: initial release `41/139 = 29.5%`, full roadmap `41/164 = 25.0%`; EPIC-03 exact-merge `main` CI was terminal success. Archived after EPIC-04 produced a newer independently calculated product state.
- `2026-09-03T00:03:30Z`: initial release `51/139 = 36.7%`, full roadmap `51/164 = 31.1%`; EPIC-04 partial `10/12` was merged, while EPIC-08 and `E09-AC06` were not implemented. Archived after terminal EPIC-08 delivery produced a newer current/previous pair.

## GOAL-005 / EPIC-01 — Portable release automation and failed rc.1 publication

- **Product outcome:** still partial `3/4`; `E01-AC01` remains uncredited until a successful exact GitHub Release and Windows Home/Pro runtime matrix.
- **Pull Request:** [#21](https://github.com/Just9120/llm-inspector/pull/21), head `a81d97eb17cd6ce541f1a7d2eeff5508c77a12a8`, PR CI `33812383296` success.
- **Merge/exact-main Evidence:** merge `ff62f54df4fbd4de443144259ac3d89bddff0044`; exact-main CI `33813413498` success with every required step.
- **Delivered:** single-file release build, exact checksum/manifest/SPDX payload, immutable pins, split permissions and GitHub/Sigstore attestations.
- **Release attempt:** immutable tag `v1.0.0-rc.1` points to the merge; run `33813681861` passed build, tests, payload verification, provenance and SBOM attestation, then failed only in `gh release create` because the checkout-free job lacked repository identity. No GitHub Release was created and the tag was not moved/deleted. Downloaded exact executable SHA-256 `3ea61cd4796fe35de16805605f213b2d10ba3e64067f42d3d3351915c2f4bd02` passes payload and GitHub attestation verification.
- **Recovery:** forward-fix uses a new PR and subsequent `v1.0.0-rc.2`; `rc.1` is not rerun or reused.

## GOAL-005 / EPIC-12 — Performance profiles and release gate harness

- **Product outcome:** still partial `7/13`; `E12-AC01..06` remain uncredited until controlled measurements pass for every built-in profile.
- **Pull Request:** [#20](https://github.com/Just9120/llm-inspector/pull/20), head `5b041ec0ac1b56831671bb4a897ddb0b800d6569`, PR CI `33810069610` success.
- **Merge/exact-main Evidence:** merge `bc53378019b8c62b6ee7cdbdb1af7036b1eb98e1`; exact-main CI `33810329608` success with every required step.
- **Delivered:** persisted Russian-labeled Saver/Balanced/Detailed/Custom profiles with schema migration and atomic sampling switch; exact budgets, fail-closed evaluator and SHA-frozen synthetic workload corpus.
- **Validation:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `221/221` tests without skips, clean self-contained `win-x64` publish and smoke `exit 0`.
- **Cleanup:** local `main` synchronized to exact merge; merged branch removed locally/remotely after ancestry and unique-commit checks.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/20#issuecomment-5532656301).

## GOAL-005 / Decision ratification

- **Outcome:** all remaining performance/release/lifecycle/remote/version-policy decisions ratified in product contract `1.2`; readiness unchanged because documentation alone completed no product AC.
- **Pull Request:** [#19](https://github.com/Just9120/llm-inspector/pull/19), head `f82ec878986ddbc19b7e93f218f4ebe440683ad8`, PR CI `33807351764` success.
- **Merge/exact-main Evidence:** merge `97866b3c69d60105bfe57fec94f4dbb6b9981ad7`; exact-main CI `33807628059` success with every required step.
- **Validation:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `211/211` tests without skips, clean self-contained `win-x64` publish and smoke `exit 0`.
- **Cleanup:** local `main` synchronized to exact merge; ratification branch removed locally/remotely after ancestry and unique-commit checks.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/19#issuecomment-5532345500).

## GOAL-005 / BACKLOG-05 — Multiple GPUs

- **Product outcome:** `READY 3/3`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Pull Request:** [#18](https://github.com/Just9120/llm-inspector/pull/18), head `d834215da8c31a2331bfee880e2b712379b4844e`, PR CI `33746022880` success.
- **Merge/exact-main Evidence:** merge `ecdb542281ca5ad989de0540bf59939f42af8eb7`; exact-main CI `33746269521` success with every required step.
- **Delivered:** bounded discovery of up to 16 distinct supported NVIDIA devices, per-device request-correlated persistence and live/history presentation, and explicit `workload attribution=unavailable` for device-wide `nvidia-smi` data.
- **Validation:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `211/211` tests without skips, clean self-contained `win-x64` publish and smoke `exit 0`.
- **Cleanup:** local `main` synchronized to exact merge; merged branch removed locally/remotely after ancestry and unique-commit checks.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/18#issuecomment-5524584973); [external-gate checkpoint](https://github.com/Just9120/llm-inspector/pull/18#issuecomment-5524607625).

## GOAL-005 / superseded pre-ratification checkpoint

- **Archived:** `2026-09-03T20:59:53Z` after recovery from PR #18 and explicit owner decisions.
- **Superseded state:** base `fa96adfc670e6b2934068681dc5c00e1e8c1fbd4`, branch `codex/goal-005-backlog-05`, local candidate with pending CI, plus unresolved E12/release/remote decisions.
- **Replacement:** verified base `ecdb542281ca5ad989de0540bf59939f42af8eb7`; all performance/release/lifecycle/remote/version decisions are canonicalized in contract `1.2`; active scope is 21 AC, while B03/B04 remain conditional backlog by explicit user decision.

## GOAL-005 / BACKLOG-06 — Analytics export

- **Product outcome:** `READY 3/3`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Pull Request:** [#17](https://github.com/Just9120/llm-inspector/pull/17), head `6361a4ec361e6104cd30559160a78aff478a9315`, PR CI `33743758869` success.
- **Merge/exact-main Evidence:** merge `fa96adfc670e6b2934068681dc5c00e1e8c1fbd4`; exact-main CI `33744027574` success with every required step.
- **Delivered:** versioned selected-range anonymized history and request/resource aggregate export, fail-closed bounded completeness, exact preview/SHA-256/atomic local save and the shared diagnostic-snapshot negative content corpus.
- **Validation:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `210/210` tests, clean self-contained `win-x64` publish and smoke `exit 0`.
- **Cleanup:** local `main` synchronized to exact merge; merged branch removed locally/remotely after ancestry and unique-commit checks.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/17#issuecomment-5524288401).

## GOAL-005 / EPIC-12 — Reliability and runtime-change correlation

- **Product outcome:** partial `7/13`; `E12-AC07..13` complete with CODE/TEST/CI `✅`, while numeric `E12-AC01..06` remain uncredited because required budgets and frozen fixtures are not approved; SPEC remains `◐` and status `IN PROGRESS ⛔`.
- **Pull Request:** [#16](https://github.com/Just9120/llm-inspector/pull/16), head `c43564bad67b0dda17c4faf347051da8c8243c41`, PR CI `33741679566` success.
- **Merge/exact-main Evidence:** merge `7c5528ec3c33396ce1068162fc0b6961a0dfe553`; exact-main CI `33741928312` success with every required step.
- **Delivered:** collector lifecycle isolation, unavailable-source continuity, typed error origin, SQLite startup integrity/process-kill recovery, new writes after restart, schema v5 runtime/version facts and statistically guarded configuration correlation.
- **Validation:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `207/207` tests, clean self-contained `win-x64` publish and smoke `exit 0`.
- **Cleanup:** local `main` synchronized to exact merge; merged branch removed locally/remotely after ancestry and unique-commit checks.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/16#issuecomment-5524011971).

## GOAL-005 / EPIC-11 — Local anonymized diagnostic snapshot

- **Product outcome:** `READY 10/10`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Pull Request:** [#15](https://github.com/Just9120/llm-inspector/pull/15), head `2d015d3adc76b9be3938f79d2637cdcdae9e40b3`, PR CI `33737811632` success.
- **Merge/exact-main Evidence:** merge `9b2933fe802842e60b089a37b1352f393ad94a56`; exact-main CI `33738059071` success with every required step.
- **Delivered:** bounded versioned snapshot allowlist, typed unavailable environment facts, UTC range/operation selection, exact local preview SHA-256/save workflow and end-to-end negative privacy corpus.
- **Validation:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `193/193` tests, clean self-contained `win-x64` publish and smoke `exit 0`.
- **Cleanup:** local `main` synchronized to exact merge; merged branch removed locally/remotely after ancestry and unique-commit checks.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/15#issuecomment-5523510559).

## GOAL-005 / EPIC-01 — Windows desktop product boundary

- **Product outcome:** partial `3/4`; `E01-AC02..04` complete, `E01-AC01` intentionally uncredited and blocked on supported Windows release-matrix Evidence plus signing/distribution decisions.
- **Pull Request:** [#10](https://github.com/Just9120/llm-inspector/pull/10), head `c583550909cf4233916d0c761e31c7be52be033b`, PR CI `33722219199` success.
- **Merge/exact-main Evidence:** merge `6aaae88e3b24306d1c4c2ec165945436eeee05f2`; exact-main CI `33722434890` success with every required step.
- **Delivered:** monitoring/analytics/diagnostics UI surfaces, literal-loopback backend boundary and fail-closed absence of backend lifecycle/runtime mutation commands.
- **Cleanup:** merged branch had zero unique commits and was removed locally/remotely after local `main` synchronized to the exact merge SHA.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/10#issuecomment-5521422014).

## GOAL-005 / EPIC-10 — Windows background monitoring and notifications

- **Product outcome:** `READY 8/8`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Pull Request:** [#14](https://github.com/Just9120/llm-inspector/pull/14), head `e2458a24eff69a9e57f5c5410b4ed4b3b574b3ea`, PR CI `33735289296` success.
- **Merge/exact-main Evidence:** merge `a349b1d36d929324c8e9102c2d9650b483172319`; exact-main CI `33735585399` success with every required step.
- **Delivered:** close-to-tray process lifetime, continued SQLite monitoring, native Windows tray controls, per-user HKCU autostart, atomic settings, four typed notification categories, silent mode and boundary-tested anti-spam policy.
- **Validation:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `186/186` tests, clean self-contained `win-x64` publish and smoke `exit 0`.
- **Cleanup:** local `main` synchronized to exact merge; merged branch removed locally/remotely after ancestry and unique-commit checks.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/14#issuecomment-5523178441).

## GOAL-005 / EPIC-07 — Explainable diagnostics and errors

- **Product outcome:** `READY 16/16`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Pull Request:** [#13](https://github.com/Just9120/llm-inspector/pull/13), head `5b41246b59b447ee1b70beb44d8e101c25388e4b`, PR CI `33731824812` success.
- **Merge/exact-main Evidence:** merge `ea8498efae98adda684dd88d92e52d3401872a5c`; exact-main CI `33732075018` success with every required step.
- **Delivered:** versioned Evidence-quality-aware diagnostics, typed gateway/history errors, metadata-only correlation, recurring frequency analytics and explicit FACT/HYPOTHESIS/INSUFFICIENT_DATA UI semantics.
- **Validation:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `172/172` tests, clean self-contained `win-x64` publish and smoke `exit 0`.
- **Cleanup:** local `main` synchronized to exact merge; merged branch removed locally/remotely after ancestry and unique-commit checks.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/13#issuecomment-5522699991).

## GOAL-005 / EPIC-06 — Windows resource telemetry

- **Product outcome:** `READY 8/8`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Pull Request:** [#12](https://github.com/Just9120/llm-inspector/pull/12), head `78f0a985b719c6d9231cf0a65fe9a9c16da921a5`, PR CI `33728471307` success.
- **Merge/exact-main Evidence:** merge `883ed13bab90c9affa9f6cf26c13161565f27e67`; exact-main CI `33728697717` success with every required step.
- **Delivered:** request/stage-correlated Windows host/process/disk/gateway traffic telemetry, fixed-path NVIDIA GPU metrics, schema v4 persistence, bounded sampling/gap accounting and fail-closed `unavailable` semantics.
- **Validation:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `150/150` tests, clean self-contained `win-x64` publish and smoke `exit 0`.
- **Cleanup:** local `main` synchronized to exact merge; merged branch removed locally/remotely after ancestry and unique-commit checks.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/12#issuecomment-5522253234).

## GOAL-005 / EPIC-05 — Agent operations, tools and concurrency

- **Product outcome:** `READY 8/8`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Pull Request:** [#11](https://github.com/Just9120/llm-inspector/pull/11), head `350026cdb4b2dd8110f2977f6a0c928630055a66`, PR CI `33724914481` success.
- **Merge/exact-main Evidence:** merge `d27296a51df51ef5a8e3d4cabca329d23bdbfaa0`; exact-main CI `33725139103` success with every required step.
- **Delivered:** fail-closed operation correlation, bounded JSON/SSE tool metadata projection, ordered schema v3 operation graph, UI detail and concurrency/privacy coverage.
- **Validation:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0` warnings/errors, `142/142` tests, clean self-contained `win-x64` publish and smoke `exit 0`.
- **Cleanup:** local `main` synchronized to exact merge; merged branch removed locally/remotely after ancestry and unique-commit checks.
- **Terminal Evidence:** [merged PR comment](https://github.com/Just9120/llm-inspector/pull/11#issuecomment-5521783189).

## GOAL-004 — 72-AC core product tranche

- **State:** `DONE`; `72/72` selected product AC and all required Evidence completed.
- **Authorization:** explicit user instruction от `2026-09-03` to implement an approximately 70-AC unified Goal through separate epic PRs and continue fixes despite product blockers.
- **Scope/outcome:** EPIC-02 `15/15`, EPIC-03 `13/13`, EPIC-04 `12/12`, EPIC-08 `18/18`, EPIC-09 `14/14`; all five epics `READY` with SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Terminal fix PR:** [#9](https://github.com/Just9120/llm-inspector/pull/9), head `74024d15e7556f4deac4f30f2bb4a1001e725d95`, PR CI `33720248633` success, merged as `7110c70b6975c939915273b005f05eedfcd2eb14`.
- **Exact-main Evidence:** CI [33720428488](https://github.com/Just9120/llm-inspector/actions/runs/33720428488) success on merge SHA; every required step completed successfully.
- **Product readiness at closure:** initial release `72/139 = 51.8%`; full roadmap `72/164 = 43.9%`.
- **Cleanup:** local `main` and `origin/main` synchronized; merged branch safely removed before GOAL-005 branch creation.
- **Metadata limitation:** terminal Evidence was recorded in the merged PR comment and recovered in the next substantive PR because no approved post-merge writer exists.

## GOAL-004 / EPIC-08 — History, analytics и retention

- **Product outcome:** `READY 18/18`; its real-schema/runtime-canary Evidence also completed `E09-AC06`, making EPIC-09 `READY 14/14`.
- **Product PR:** [#8](https://github.com/Just9120/llm-inspector/pull/8), head `9d4651f883a4fac843510ac378346da39214932f`.
- **Local Evidence:** exact SDK `10.0.400`, locked normal/RID restores, format, Release build `0 warnings / 0 errors`, `112/112` tests, self-contained `win-x64` publish and smoke.
- **Terminal CI:** PR run `33702613336` success; merged as `5757652943753e549ef85f81308c0fc0c6d83686`; exact-main run `33702791561` success with all required steps.
- **Cleanup:** merged branch had zero unique commits and was removed locally/remotely; local `main` was fast-forwarded before the EPIC-04 fix branch.
- **Metadata limitation:** repository has no approved post-merge writer; terminal Evidence was recorded in the merged PR comment and recovered into the next substantive fix PR.

## GOAL-004 / EPIC-03 — Live state, quality и deterministic abort fixture

- **Product outcome:** `READY 13/13`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Product PR:** [#6](https://github.com/Just9120/llm-inspector/pull/6), initial head `d1e8f313b6652cfe1656f1de0c4fac99bd852103`.
- **Detected gate failure:** PR CI `33693671440` failed the inherited `BackendBodyAbortKeepsOriginalStatusAndRecordsRelayFailure` fixture because the backend could abort before the proxy had observed response headers; required downstream steps correctly did not supply success Evidence.
- **Grouped fix:** fixture now releases backend abort only after the client receives exact `200` and the partial body through the proxy; validated head `eb83e98df4fb130257e7f1cc298af1e2b9b99c8c` passed follow-up PR CI `33694308639`.
- **Terminal CI:** merged as `ba63d0b219e61527d3d81994638dee39a11c14bf`; exact-merge `main` run `33694559218` succeeded with all required steps.
- **Cleanup:** merged branch had zero unique commits and was removed locally/remotely; local `main` was fast-forwarded before EPIC-04 branch creation.

## GOAL-004 / EPIC-02 — Backend/client adapters и CI stabilization

- **Product outcome:** `READY 15/15`; SPEC/CODE/TEST/CI `✅`, DEPLOY/LIVE `N/A`.
- **Product PR:** [#4](https://github.com/Just9120/llm-inspector/pull/4), head `f10db376aeb5cf7a039719ae20ea22ce237e283b`, PR CI `33690474686` success, merge `275b7407a015f6a98361f87b74b5dc444f0a8355`.
- **Detected gate failure:** exact-merge `main` CI `33690756965` failed one transport-dependent abort assertion; downstream publish/smoke correctly skipped, so CI Evidence remained failed.
- **Fix PR:** [#5](https://github.com/Just9120/llm-inspector/pull/5), head `521b91c5960196f3effa048f4071bf3b2858f58a`, PR CI `33691566607` success, merge `e1e1b735116b94d73fa87559da8759c5f58d243c`.
- **Terminal CI:** exact-merge `main` run `33691761413` success; all required steps completed without required skips.
- **Cleanup:** both merged feature/fix branches had zero unique commits and were removed locally/remotely; local `main` was fast-forwarded before EPIC-03 branch creation.

## GOAL-003 — Создать reproducible .NET repository skeleton и PR CI foundation

- **State:** `DONE`.
- **Authorization:** explicit user instruction от `2026-09-02`: «ставь цель и начинай» после согласования PR-based flow.
- **Scope:** exact `.NET 10.0.400` toolchain, 9 production/6 test projects, empty Avalonia shell, normal/`win-x64` lock graphs, executable local commands и least-privilege PR/`main` CI без product behavior/CD.
- **Outcome:** foundation реализована; product readiness намеренно осталась `0/139` initial и `0/164` full roadmap.
- **Local Evidence:** locked restores, format, Release build `0 warnings / 0 errors`, `10/10` tests, self-contained `win-x64` publish, smoke и bounded UI launch.
- **Pull Request:** [#2](https://github.com/Just9120/llm-inspector/pull/2), merged `2026-09-02T20:48:29Z`.
- **Merge commit:** `384556f693df9b3dbbc9d06dc2ddbd67328fa5d7`.
- **CI Evidence:** PR run [33681233301](https://github.com/Just9120/llm-inspector/actions/runs/33681233301) success на `acc0a39cc15e228268973883f916ecf7f0e1a2d8`; `main` run [33681476344](https://github.com/Just9120/llm-inspector/actions/runs/33681476344) success на merge commit; all steps terminal success.
- **DEPLOY/LIVE:** N/A; runtime CD target отсутствует.
- **Cleanup:** local/remote feature branch удалена после ancestor/unique-commit checks; local `main == origin/main`, worktree clean.
- **Limitation:** approved post-merge metadata mechanism отсутствовал; terminal Evidence было также записано в merged PR comment, metadata-only follow-up PR/direct push не создавались.

## GOAL-002 — Утвердить architecture baseline и executable delivery foundation design

- **State:** `DONE`.
- **Authorization:** explicit user approval от `2026-09-02`: «Да, вот теперь идем через ПРы по инструкции» в ответ на proposal `GOAL-002`.
- **Scope:** Windows desktop/runtime stack, module/data/privacy boundaries, backend capability matrix, supported Windows matrix, test strategy, performance protocol и Windows build/release design без product implementation.
- **Outcome:** утверждены C# / `.NET 10 LTS`, Avalonia UI, embedded loopback-only Kestrel proxy, SQLite WAL, modular-monolith boundaries, NuGet CPM/lock files, self-contained `win-x64` validation build и signed MSIX release design; server/runtime CD оставлен отключённым.
- **Evidence:** SPEC ✅; CODE N/A; TEST ✅ (structural/link/count/profile validation); CI N/A; DEPLOY N/A; LIVE N/A.
- **Pull Request:** [#1](https://github.com/Just9120/llm-inspector/pull/1), merged `2026-09-02T18:52:30Z`.
- **Merge commit:** `00ca8c3ef727d784ca2e0c9d837231be7f68c5e4`.
- **Closed after recovery verification:** `2026-09-02T20:11:52Z`; local `main`, `origin/main` и GitHub default branch совпали на merge commit.
- **Limitations:** numeric performance budgets, signing identity и distribution channel остаются внешними решениями для будущих Goals.

## GOAL-001 — Ратифицировать product contract и CD applicability

- **State:** `DONE`.
- **Authorization:** explicit user instruction от `2026-09-02`: все requirements upstream документа согласованы; перенести их в `project-spec.md` и декомпозировать; Windows desktop product не использует runtime CD.
- **Scope:** canonicalize 72 upstream requirement bullets, сохранить 9 `[Backlog]` items как canonical backlog, сформировать atomic AC/Evidence/readiness, синхронизировать documentation и CI/CD project profile.
- **Non-goals:** architecture selection, product code, dependencies, CI workflows, remote Git writes, Windows package publication.
- **Outcome:** 12 initial-release epics / 139 AC; 6 backlog epics / 25 AC; full roadmap 164 AC; server/runtime CD disabled.
- **Evidence:** SPEC ✅; CODE N/A; TEST ✅ (local structural/count/link/reference checks); CI N/A; DEPLOY N/A; LIVE N/A.
- **Closed:** `2026-09-02T18:01:24Z`.
- **Git baseline:** ratified documentation сохранена в initial content commit `e0860e4972e486e59fcf3a8499b5da0f2863b96c`.
- **Limitations:** Windows support matrix и numeric performance budgets остаются SPEC gaps.

## BOOTSTRAP-001 — Initial `main` synchronization

- **State:** `DONE` после обязательной final Git/GitHub verification этой task.
- **Authorization:** explicit user instruction от `2026-09-02` выполнить one-time initial commit/push в `main`, затем остановиться перед выбором следующей Goal.
- **Scope:** commit ratified documentation baseline, update checkpoint metadata, выполнить один initial push и доказать equality local/remote HEAD.
- **Non-goals:** feature implementation, CI/repository settings, architecture work, новая Goal.
- **Base content SHA:** `e0860e4972e486e59fcf3a8499b5da0f2863b96c`.
- **Evidence:** local structural validation; final local `HEAD`/`origin/main`/GitHub default-branch verification. Containing metadata commit не self-referenced.
