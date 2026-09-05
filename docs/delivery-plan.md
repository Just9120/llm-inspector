# Delivery plan

> Dashboard status: `IN_PROGRESS`
> Updated: `2026-09-05`

## Current Goal

**GOAL-006 — Go migration и русский desktop UI**

- **State:** `IN_PROGRESS`.
- **Authorization:** explicit user instruction от `2026-09-05`: «Тогда ставь единую goal и приступай к непрерывной реализации», после согласования Go/Wails v2/Svelte-TypeScript и русского современного неперегруженного UI в Google Docs.
- **Scope:** ратификация четырёх stack/UX требований в canonical spec; перенос всех реализованных Windows-функций EPIC-01..12, B01/B02/B05/B06 с preservation contracts; Go core, Wails v2 shell, Svelte/TypeScript UI; regression/security/privacy tests, совместимость SQLite/settings, воспроизводимая локальная Windows-сборка, перенос CI/build commands при сохранении safety boundaries, PR/merge/main synchronization.
- **Non-goals:** final tag/release/WinGet, manual Windows Home/Pro walkthrough, controlled performance measurements, two-host LIVE, Linux/macOS, новые LLM protocols и функции за пределами согласованного scope. Существующие manual gates остаются отдельными.
- **Required Evidence:** SPEC/CODE/TEST/CI подтверждены для доставленных increments; локальная Windows build/smoke и regression parity для конечного artifact. DEPLOY/LIVE — N/A для Goal; product B02 LIVE по-прежнему обязателен до READY.
- **Stop condition:** все Goal AC выполнены и merged branches safely removed (`DONE`); либо конкретный неустранимый scope blocker/external gate после завершения независимой работы.
- **Safety:** CI/CD safety contract, permissions, trusted release trigger и production operations не ослабляются. Owner от `2026-09-05` явно разрешил обновить **только стек и команды** в `AGENTS.md` и Project CI/CD profile при cutover; safety rules, permissions, release gates и disabled CD остаются неизменными.
- **Точечное согласование:** owner `2026-09-05` разрешил уточнить только E01-AC04: наблюдение не выполняет lifecycle автоматически; отдельный B01 доступен после explicit exact-target confirmation. Другие product AC и denominator не изменены.
- **Terminal metadata:** explicit owner approval `2026-09-05`: main хранит датированный pre-merge checkpoint со ссылкой на финальный PR; фактические DONE, exact CI/merge SHA и clean-main/branch cleanup фиксируются terminal-комментарием того же PR. Post-deploy writer не включается, metadata-only PR/extra push и обход protections не нужны.

### Goal acceptance criteria

| ID | Требование | State на pre-merge checkpoint |
|---|---|---|
| G06-01 | Canonical spec содержит 4 согласованных stack/UX AC; Goal/boundaries/denominator явны | DONE |
| G06-02 | Go proxy/adapters сохраняют HTTP/SSE bytes/order, cancellation, quality/provenance и privacy | DONE — PR #34 и main CI PASS |
| G06-03 | Live state, tokens/context/timings, operations/tools и diagnostics имеют parity | Local PASS — core PR #35; final UI/resource wiring и regression tests PASS; cutover CI pending |
| G06-04 | SQLite/history/analytics/retention/snapshot/export сохраняют committed data и allowlist | Local PASS — core PR #36; exact migrations/recovery/selection и desktop save/clear confirmation PASS; cutover CI pending |
| G06-05 | Windows resources/GPU, tray/background/autostart/notifications, profiles перенесены | Local PASS — core PR #37; native/synthetic fixtures и final settings/host wiring PASS; manual walkthrough вне Goal |
| G06-06 | B01 lifecycle и B02 remote сохраняют ownership/confirmation/DPAPI/loopback | Local PASS — core PR #38; typed facade/UI complete, no user runtime/credentials changed; cutover CI pending |
| G06-07 | Русский Wails/Svelte UI предоставляет все функции через понятную навигацию | Local PASS — пять разделов, progressive disclosure, Svelte checks, actual WebView2 navigation/history/custom profile smoke |
| G06-08 | Reproducible Windows executable, smoke/regression, нет C#/.NET/Avalonia runtime path | Local PASS — clean checkout и root дают identical SHA-256; locked build/release payload suite PASS |
| G06-09 | Local checks → PR CI → merge → main CI; актуальные docs и cleanup branches | PENDING — final PR ещё не открыт; terminal Evidence фиксируется в нём после фактических gates |

Goal — не новая реализация после cutover. G06-03..08 имеют local CODE/TEST Evidence, но full Goal DoD ещё требует final CI/delivery. Не объявлять будущий merge success.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | 2026-09-05T12:00:04Z — clean build completion; последующая pre-PR документационная сверка |
| Base branch / SHA | main / `238d3ba5f7d607fa6768a6092f9cd3f2ce36f3fd` — PR #39 merged |
| Working branch | `codex/goal-006-checkout-fix` — narrow post-merge completion-review correction |
| Last verified revision | `7e93c159d9ce238e4b2ec7c378c24beee7af93f8` — final clean-source build/smoke/release validation |
| Worktree state | Clean at start; только GOAL-006 code/docs. Собственные detached verification worktrees под artifacts ожидают safe cleanup |
| Completed work | Tranches 1–5 merged и CI verified; tranche 6 полностью реализован и локально проверен: composition/facade, русский UI, Windows host, resource/runtime evidence, locked CI/release cutover, legacy removal и documentation |
| Current step | Final local validation и independent 168-AC review завершены; подготовка initial push/PR |
| Next exact action | Restore shared .gitattributes policies lost during cutover, regression/clean-build validation, один grouped correction PR в той же Goal; затем final main CI и cleanup |
| PR locator | [Финальный PR по exact head branch](https://github.com/Just9120/llm-inspector/pulls?q=is%3Apr+head%3Acodex%2Fgoal-006-go-desktop); number/URL появится при создании, не придуман заранее |
| CI | Cutover PR #39 / `33965234911` SUCCESS; merged `238d3ba`, applicable main run `33975166335`. Correction PR/CI ещё не запускались |
| Local executable | `build/bin/LlmInspector.exe`, SHA-256 `9c327b2e3385b6ca3820e41cb7591301680dec303265954911b0ae2c256234ec`; exact clean checkout hash совпадает |
| Deployment / release | DEPLOY/LIVE N/A для Goal; no tag/release/WinGet, CD disabled |
| Blockers / external gates | Для implementation blocker нет. Final PR/main CI ещё pending; manual Windows/E12/B02 LIVE вынесены за scope Goal |
| Preserved pre-existing changes | None |
| Terminal recovery | Owner-approved terminal comment в этом же PR содержит exact final SHA/checks/merge/cleanup/DONE. После закрытия не создавать metadata-only PR, не переходить к следующей Goal |

### Validation и ограничения

Post-merge completion review: исходный `.gitattributes` существовал, но был ошибочно заменён при добавлении frontend LF rules. Narrow correction восстанавливает прежние общие text/JSON/YAML/Markdown/PowerShell policies и toolchain pin files, сохраняя новые Go/frontend/frozen/embedded LF rules; obsolete C# extensions не возвращаются. Это исправление реального config regression в GOAL-006, не metadata-only PR и не следующая Goal. Readiness denominator/AC не изменились. Шесть собственных clean verification worktrees уже удалены после проверки чистоты и merged ancestry; product data не затронуты.

PR #39 hosted artifact SHA-256 `87af704dfb9600a99af501c9a78beb30f9c0129c5862b11ac16c17fec37ba151` отличается от локального; cross-host bit identity не доказана. До release никакое manual Evidence не переносится между разными hashes; exact candidate identity остаётся обязательным отдельным gate. CI/native smoke и local two-checkout reproduction не подменяют этот gate.

- `scripts/build-windows.ps1` PASS из root и нового detached checkout без old assets/bindings/node_modules; exact Go/Node/npm pins, Go formatting/mod verify/vet/tests/build, locked npm install, Wails bindings/assets, Svelte/TS/Prettier/Node tests, root host vet/tests.
- 149 internal Go test functions + fuzz seed corpus, root 2 tests, frontend 3 Node tests. Финальный `go test ./... -json` дал 201 pass events (включая subtests/seeds), 0 fail и 0 skip; frontend 3/3 PASS, Svelte/TypeScript 0 errors / 0 warnings. Это не 1:1 эквивалент прежних 260 C# cases и не readiness denominator.
- Native smoke: actual GUI exit 0, WebView2 bridge, русский frontend, все 5 screens, synthetic request/history и custom-profile reactivity, proxy/SQLite/private canary. Отдельно визуально проверен обзор. Реальные user DB/settings/credentials/backend models не затронуты.
- Release-tools PASS: 100 Go/npm/toolchain SBOM dependencies, exact source/manifest/artifact identity, duplicate checksum/wrong source SHA/tamper rejection, no publication.
- Clean checkout выявил LF/CRLF drift formatting, frozen corpus и embedded SQL/config. Scoped `.gitattributes` + regression policy закрепляют exact bytes; после fixes full clean build PASS и root/clean hashes идентичны. Это локальная воспроизводимость, не утверждение об ещё не запущенном public release.
- 154 legacy source/config files удалены, 14 protocol fixtures moved intact; SQL/matrix reference hashes независимы от current implementation. История восстановима через Git. Generated output не коммитится.
- Race detector локально unavailable (нет C compiler); bounded/concurrency/repeat tests не заменяют race detection. Нет manual all-controls/DPI/Windows Home/Pro matrix, controlled performance или real two-host LIVE.
- Более ранние tranche checkpoints/CI corrections и intermediate artifact hashes перенесены в archive; они не заменяют exact final artifact Evidence.

## Project readiness snapshots

| Snapshot | Initial release | Full agreed roadmap | Основание |
|---|---:|---:|---|
| Current — final local Go review | 136/143 = 95.1% | 152/168 = 90.5% | Все 168 unique AC независимо сверены с current source/tests/product surface; ID ledger и 16 missing AC в project-spec §7 |
| Previous — first desktop verified minimum | 4/143 = 2.8% | 4/168 = 2.4% | Тогда был подтверждён только E01-AC02/03/07/08; остальные ожидали финального review |

Рост >10 п.п. объясняется завершением desktop/storage/resource/control integration и полной проверкой каждого AC, не заимствованием C# readiness. Denominator неизменён после 4 approved stack/UX AC. B02-AC01/02/04/05 не получают кредит за local stubs — требуется фактический other-PC/VPS/encrypted flow. CODE/TEST подтверждают implementation path, но не LIVE.

## Execution pipeline и Roadmap

1. **Current Goal:** final cutover PR → required `windows-go` → merge без bypass → exact-main CI → safe main sync/merged branch/worktree cleanup → terminal PR comment → DONE и остановка.
2. **Candidate manual validation Goal (не авторизована):** exact Go artifact Windows Home/Pro clean-machine/upgrade/tray/autostart/recovery и critical backend/client flows; controlled built-in profile E12 measurements; encrypted Windows↔VPS/second-PC B02 checklist. Non-goals: публикация, новые функции/протоколы/OS. AC: Evidence exact SHA/version/machine/profile; честные PASS/FAIL/PENDING по всем применимым сценариям, без переноса historical C# credit. Required: TEST и B02 LIVE; blockers — доступ к второй Windows edition/машине/стабильному reference workload и runtime versions.
3. **Candidate publication Goal (не авторизована):** final `v1.0.0` только после approved validation; trusted release/SBOM/provenance/SmartScreen и затем отдельная процедура WinGet при новом owner approval.
4. **Conditional backlog:** Linux/macOS и дополнительные API protocols только при explicit demand; signing/installer/updates и CI action maintenance — отдельные proposals, не implementation authorization.

Только шаг 1 входит в текущую Goal. При обнаружении failed required check исправлять связанную причину в этой же Goal; нельзя обходить gates или объявлять missing manual Evidence успешным.
