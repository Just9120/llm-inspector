# Delivery plan

> Dashboard status: `IN_PROGRESS`
> Updated: `2026-09-05T16:16:10Z` — датированный pre-merge docs checkpoint; terminal state — в PR #39.

## Current Goal

**GOAL-006 — Go migration и русский desktop UI**

- **State:** `IN_PROGRESS`.
- **Authorization:** explicit user instruction от `2026-09-05`: «Тогда ставь единую goal и приступай к непрерывной реализации», после согласования Go/Wails v2/Svelte-TypeScript и русского современного неперегруженного UI в Google Docs.
- **Scope:** ратификация четырёх stack/UX требований в canonical spec; перенос всех реализованных Windows-функций EPIC-01..12, B01/B02/B05/B06 с preservation contracts; Go core, Wails v2 shell, Svelte/TypeScript UI; regression/security/privacy tests, совместимость SQLite/settings, воспроизводимая локальная Windows-сборка, перенос CI/build commands при сохранении safety boundaries, PR/merge/main synchronization.
- **Docs closeout authorization:** explicit user instruction `2026-09-05`: «поправь еще документацию следующим пр». Разрешён отдельный docs-only PR после завершённых code PR #39/#40: фактические статусы/Evidence, checkpoint, navigation/build limitations и архивирование устаревшей истории; без изменения AC, denominator, кода, AGENTS или CI/CD safety contract. Это точечное исключение из прежнего запрета metadata-only follow-up, только для закрытия этой Goal.
- **Non-goals:** final tag/release/WinGet, manual Windows Home/Pro walkthrough, controlled performance measurements, two-host LIVE, Linux/macOS, новые LLM protocols и функции за пределами согласованного scope. Существующие manual gates остаются отдельными.
- **Required Evidence:** SPEC/CODE/TEST/CI подтверждены для доставленных increments; локальная Windows build/smoke и regression parity для конечного artifact. DEPLOY/LIVE — N/A для Goal; product B02 LIVE по-прежнему обязателен до READY.
- **Stop condition:** все Goal AC выполнены и merged branches safely removed (`DONE`); либо конкретный неустранимый scope blocker/external gate после завершения независимой работы.
- **Safety:** CI/CD safety contract, permissions, trusted release trigger и production operations не ослабляются. Owner от `2026-09-05` явно разрешил обновить **только стек и команды** в `AGENTS.md` и Project CI/CD profile при cutover; safety rules, permissions, release gates и disabled CD остаются неизменными.
- **Точечное согласование:** owner `2026-09-05` разрешил уточнить только E01-AC04: наблюдение не выполняет lifecycle автоматически; отдельный B01 доступен после explicit exact-target confirmation. Другие product AC и denominator не изменены.
- **Terminal metadata:** explicit owner approval `2026-09-05`: main хранит датированный pre-merge checkpoint со ссылкой на финальный PR; фактические DONE, exact CI/merge SHA и clean-main/branch cleanup фиксируются terminal-комментарием того же PR. Post-deploy writer не включается, дополнительные записи в main ради terminal metadata и обход protections не нужны. Отдельный docs PR ниже разрешён новым explicit user request; после него новый metadata-only PR не создаётся.

### Goal acceptance criteria

| ID | Требование | State на pre-merge checkpoint |
|---|---|---|
| G06-01 | Canonical spec содержит 4 согласованных stack/UX AC; Goal/boundaries/denominator явны | DONE |
| G06-02 | Go proxy/adapters сохраняют HTTP/SSE bytes/order, cancellation, quality/provenance и privacy | DONE — PR #34 и main CI PASS |
| G06-03 | Live state, tokens/context/timings, operations/tools и diagnostics имеют parity | DONE — Go state/diagnostics и final UI/resource wiring; exact-main CI PASS |
| G06-04 | SQLite/history/analytics/retention/snapshot/export сохраняют committed data и allowlist | DONE — SQLite migrations/recovery/selection, privacy/export и desktop confirmation; exact-main CI PASS |
| G06-05 | Windows resources/GPU, tray/background/autostart/notifications, profiles перенесены | DONE — native/synthetic resource и background fixtures, settings/host wiring; manual walkthrough вне Goal |
| G06-06 | B01 lifecycle и B02 remote сохраняют ownership/confirmation/DPAPI/loopback | DONE — typed lifecycle/remote facade/UI, ownership/confirmation/DPAPI tests; B02 LIVE вне Goal |
| G06-07 | Русский Wails/Svelte UI предоставляет все функции через понятную навигацию | DONE — пять русских разделов, Svelte checks, actual WebView2 navigation/history/custom profile smoke |
| G06-08 | Reproducible Windows executable, smoke/regression, нет C#/.NET/Avalonia runtime path | DONE — locked build/release checks и local two-checkout identical SHA-256; cross-host identity не заявлена |
| G06-09 | Local checks → PR CI → merge → main CI; актуальные docs и cleanup branches | IN_PROGRESS — code PR #39/#40 и main CI SUCCESS; остаются owner-requested docs PR и final safe cleanup |

**Implementation delivery завершён:** G06-01..08 выполнены, code PR #39/#40 merged, exact-main CI SUCCESS. **Goal DoD: 8/9** на этом checkpoint: G06-09 ожидает только явно запрошенный документационный PR и итоговую очистку. Product AC и manual/publication gates этим не закрываются.

## Active execution checkpoint

| Field | Verified state |
|---|---|
| Updated UTC | 2026-09-05T16:16:10Z — Git/GitHub/CI readback перед docs PR |
| Base branch / SHA | main / `49d1e9aaf48c8b6780803dcc115100e3a2a5b5f7`; local main = origin/main = GitHub main |
| Working branch | `codex/docs-go-closeout` — отдельный явно разрешённый документационный PR |
| Last verified revision | `49d1e9aaf48c8b6780803dcc115100e3a2a5b5f7` — local full build/smoke/release suite и exact-main CI SUCCESS |
| Worktree state | Clean at branch creation; только requested documentation changes. Один собственный detached verification worktree ожидает safe cleanup; шесть уже удалены |
| Completed work | Вся Go implementation доставлена PR #34..40; русский desktop, parity/storage/privacy/native host, CI/release cutover и исправление checkout policy проверены |
| Current step | Docs-only diff/AC/links и полная local validation PASS; requirements и safety contract неизменны |
| Next exact action | Initial push и docs PR; после required CI/merge/main CI выполнить cleanup и terminal comment |
| PR | [Cutover #39](https://github.com/Just9120/llm-inspector/pull/39) и [checkout fix #40](https://github.com/Just9120/llm-inspector/pull/40) MERGED; [docs PR locator по exact branch](https://github.com/Just9120/llm-inspector/pulls?q=is%3Apr+head%3Acodex%2Fdocs-go-closeout) — на checkpoint ещё не создан |
| CI | Code final main [33975817279](https://github.com/Just9120/llm-inspector/actions/runs/33975817279) / `49d1e9aaf48c8b6780803dcc115100e3a2a5b5f7`: `windows-go` SUCCESS, 2026-09-05T15:50:33Z, required skips отсутствуют. Docs PR/main runs ещё не запускались |
| Local executable | `build/bin/LlmInspector.exe`, SHA-256 `9c327b2e3385b6ca3820e41cb7591301680dec303265954911b0ae2c256234ec`; independent local clean checkout hash совпадает |
| Deployment / release | DEPLOY/LIVE N/A для Goal; no tag/release/WinGet, CD disabled |
| Blockers / external gates | Implementation blockers отсутствуют. Docs delivery/cleanup ещё pending; manual Windows/E12/B02 LIVE вне scope Goal, public release пока не готов |
| Preserved pre-existing changes | None; пользовательские runtime/data/credentials не менялись |
| Terminal recovery | После фактических gates terminal comment в [PR #39](https://github.com/Just9120/llm-inspector/pull/39) содержит docs PR, exact final SHA/checks/merge/cleanup/DONE; искать запись с заголовком `GOAL-006 — terminal Evidence`. Этот checkpoint не переписывается постфактум и не доказывает будущие events |

### Validation и ограничения

- Docs closeout local validation `2026-09-05T16:24:29Z`: шесть Markdown files, 23 local links, normative spec sections/168 product AC/9 Goal AC unchanged; повторный подсчёт 136/143 и 152/168. Full build/native smoke/release-tools PASS, executable hash прежний. Первый sandbox run остановился на Node `spawn EPERM`; вне sandbox этот gate прошёл. Отдельный local run получил `TempDir RemoveAll: directory not empty` в `TestEngineSettingsAndBusyCountAreActual`; при readback каталог пуст, 20 focused repeats и последующий full suite PASS. Причина filesystem cleanup failure не установлена; test не отключён и код не менялся, intermittent test-environment risk остаётся.

PR #39 и final-main CI `33975817279` дали одинаковый hosted executable SHA-256 `87af704dfb9600a99af501c9a78beb30f9c0129c5862b11ac16c17fec37ba151`, который отличается от локального; cross-host bit identity не доказана. До release никакое manual Evidence не переносится между разными hashes; exact candidate identity остаётся обязательным отдельным gate. CI/native smoke и local two-checkout reproduction не подменяют этот gate.

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
| Current — code delivery / CI verified, 2026-09-05 | 136/143 = 95.1% | 152/168 = 90.5% | Повторный подсчёт 168 unique AC и ledger: 7 initial / 16 full не выполнены; exact-main CI подтвердил CODE/TEST, но не missing manual/LIVE AC |
| Previous — final local Go review, 2026-09-05 | 136/143 = 95.1% | 152/168 = 90.5% | Независимый final AC-by-AC review до CI; тот же denominator, CI ожидался |

Изменение к предыдущему snapshot: **0 п.п.** CI подтверждает Evidence и READY для 13 полностью выполненных эпиков, но не добавляет product AC. Более ранний verified minimum и объяснение роста >10 п.п. перенесены в archive. Denominator неизменён после 4 approved stack/UX AC. B02-AC01/02/04/05 не получают кредит за local stubs — требуется фактический other-PC/VPS/encrypted flow. CODE/TEST подтверждают implementation path, но не LIVE.

## Execution pipeline и Roadmap

1. **Current Goal:** requested docs-only PR → required `windows-go` → merge без bypass → exact-main CI → safe main sync/merged branch/worktree cleanup → terminal PR comment → DONE и остановка.
2. **Candidate manual validation Goal (не авторизована):** exact Go artifact Windows Home/Pro clean-machine/upgrade/tray/autostart/recovery и critical backend/client flows; controlled built-in profile E12 measurements; encrypted Windows↔VPS/second-PC B02 checklist. Non-goals: публикация, новые функции/протоколы/OS. AC: Evidence exact SHA/version/machine/profile; честные PASS/FAIL/PENDING по всем применимым сценариям, без переноса historical C# credit. Required: TEST и B02 LIVE; blockers — доступ к второй Windows edition/машине/стабильному reference workload и runtime versions.
3. **Candidate publication Goal (не авторизована):** final `v1.0.0` только после approved validation; trusted release/SBOM/provenance/SmartScreen и затем отдельная процедура WinGet при новом owner approval.
4. **Conditional backlog:** Linux/macOS и дополнительные API protocols только при explicit demand; signing/installer/updates и CI action maintenance — отдельные proposals, не implementation authorization.

Только шаг 1 входит в текущую Goal. При обнаружении failed required check исправлять связанную причину в этой же Goal; нельзя обходить gates или объявлять missing manual Evidence успешным.
