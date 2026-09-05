# LLM Inspector

LLM Inspector — локальное Windows-приложение для мониторинга, аналитики и диагностики LLM. Go core, Wails v2 и русский Svelte/TypeScript UI: пять разделов — обзор, история, аналитика, backend и настройки. Подробности открываются по мере необходимости.

Версия — **1.0** (в файлах сборки `1.0.0`). Финальный релиз ещё не опубликован: ручные Windows, performance и encrypted two-host проверки выполняются отдельной Goal. Старые `v1.0.0-rc.*` — исторические C#-сборки, не текущая Go-версия. Точное состояние, readiness и Evidence: [delivery-plan.md](docs/delivery-plan.md), [project-spec.md](docs/project-spec.md).

Переход на Go доставлен через [PR #39](https://github.com/Just9120/llm-inspector/pull/39) и [исправление checkout policy #40](https://github.com/Just9120/llm-inspector/pull/40); [CI проверенного main](https://github.com/Just9120/llm-inspector/actions/runs/33975817279) прошёл build/tests/native smoke. Это подтверждение реализации, не разрешение финальной публикации и не ручная проверка всех Windows-сценариев.

## Возможности

- Прозрачный local HTTP/SSE proxy для Ollama, llama.cpp и LM Studio: OpenAI-compatible `GET /v1/models`, `POST /v1/chat/completions`; для LM Studio также native `POST /api/v1/chat`.
- Активные запросы, этапы, tokens/context/timings, progress/ETA, operations/tools и диагностика с явным различием факта, гипотезы и недостаточности данных.
- CPU/RAM, доказанная process attribution, disk/network counters и до 16 NVIDIA GPU. Quality/source/derivation сохраняются; отсутствующие метрики не превращаются в нули. Данные GPU — device-wide, не вымышленная привязка к workload.
- SQLite history с фильтрами и ресурсными timeline, mean/median/P95, recurring errors и сравнение конфигураций. Локальные snapshot/analytics export требуют просмотра точного JSON и SHA-256 перед сохранением.
- Tray, скрытие окна без остановки мониторинга, per-user autostart, четыре независимых уведомления, silent mode и защита от спама.
- Профили «Бережный» (2 с), «Сбалансированный» (1 с), «Детальный» (500 мс) и «Свой профиль» (250 мс–10 с).
- Отдельное, явно подтверждаемое управление Inspector-owned backend: запуск, остановка, restart, загрузка уже установленной модели, validated параметры. Чужие процессы не останавливаются.
- Default-off remote access через private Tailscale Serve, application token под DPAPI CurrentUser и explicit HTTPS tailnet backend. Реальный two-host LIVE пока не подтверждён.

## Быстрый старт

### Для пользователя

Distribution target — portable `LlmInspector.exe` для Windows 11 25H2 Home/Pro x64. Go runtime и frontend встроены: Go, Node.js и .NET устанавливать не требуется. **Microsoft Edge WebView2 Runtime должен быть установлен**; Inspector не скачивает его автоматически. Программа unsigned: предупреждение SmartScreen ожидаемо, подпись и installer пока не входят в поставку.

До финальной публикации исполняемый файл можно получить локальной сборкой ниже. Запуск:

```powershell
.\build\bin\LlmInspector.exe
```

Default gateway: `http://127.0.0.1:5117`; backend — Ollama на `http://127.0.0.1:11434`. Выбор другого runtime:

```powershell
.\build\bin\LlmInspector.exe --backend=llama-cpp
.\build\bin\LlmInspector.exe --backend=lm-studio --backend-url=http://127.0.0.1:4321/ --listener-port=5118
```

Без override используются порты Ollama `11434`, llama.cpp `8080`, LM Studio `1234`. Launch options: `--backend=ollama|llama-cpp|lm-studio`, `--backend-url=http[s]://<literal-loopback>:<port>/`, `--remote-backend-url=https://<node>.<tailnet>.ts.net[:port]/`, `--listener-port=1..65535`. Local/remote URL взаимоисключающие; credentials, path/query/fragment, неизвестные и повторные options отклоняются. Environment proxy/bind overrides не используются.

Настройте штатный OpenAI-compatible base URL клиента:

| Клиент | Base URL |
|---|---|
| Generic/Unknown | `http://127.0.0.1:5117/v1` |
| OpenCode | `http://127.0.0.1:5117/clients/opencode/v1` |
| Hermes | `http://127.0.0.1:5117/clients/hermes/v1` |
| Cline | `http://127.0.0.1:5117/clients/cline/v1` |
| Open WebUI | `http://127.0.0.1:5117/clients/open-webui/v1` |

Это explicit endpoint attribution, не угадывание процесса. Generic URL всегда даёт Generic/Unknown; backend получает стандартный path. Наличие endpoint не означает подтверждённую совместимость всех версий клиента: exact Go runtime/client matrix проверяется отдельно.

Раздел «Backend» позволяет найти executable, проверить путь/версию/endpoint и нажать «Подтверждаю runtime и endpoint». Inspector не устанавливает backend и не скачивает модели. Lifecycle-порт не меняет destination proxy скрыто. Подробности: [backend lifecycle](docs/runbooks/backend-lifecycle.md).

Remote ingress включается в настройках только после подтверждения private HTTPS Serve/user identity/ACL/no-Funnel. Token показывается однократно; Tailscale install/login/ACL/Serve не автоматизированы. Setup, remote backend и LibreChat: [secure remote access](docs/runbooks/secure-remote-access.md).

### Для разработки и локальной проверки

Нужны Windows x64, PowerShell 7, WebView2 и exact toolchains из `.go-version`, `.node-version`, `.npm-version`: Go `1.27.1`, Node.js `22.23.1`, npm `12.0.2`. Команды выполняются из root:

```powershell
./scripts/build-windows.ps1
./eng/release/Test-ReleaseTools.ps1
```

Первый скрипт проверяет pins, Go formatting/module integrity/vet/tests/build, делает locked `npm ci`, устанавливает pinned Wails CLI `v2.15.0` в `artifacts/tools`, генерирует bindings/assets, проверяет frontend и собирает `build/bin/LlmInspector.exe`. Native smoke запускает **этот** GUI executable и требует actual exit 0 после WebView2/bridge/proxy/history/privacy проверок. Второй скрипт локально проверяет release manifest/SBOM/checksums и негативные tamper cases; ничего не публикует.

После первого full build доступны focused checks:

```powershell
./scripts/validate-go.ps1
go test ./internal/desktop -count=1 -timeout 60s
npm --prefix frontend run check
npm --prefix frontend test
./scripts/smoke-windows.ps1
```

Перед full build остановите preview из того же checkout: native frontend dependencies могут быть заблокированы работающим Vite. `artifacts/`, `build/bin/`, `frontend/node_modules/`, `frontend/dist/` и Wails bindings — generated/ignored output, не source.

CI выполняет ту же последовательность в [.github/workflows/ci.yml](.github/workflows/ci.yml), check `windows-go`: ephemeral Windows runner, read-only token, SHA-pinned actions. C#/.NET/Avalonia production path удалён; historical source остаётся в Git. Server CD отключён. Release workflow отдельный, final-tag-only; создание tag/публикация сейчас не авторизованы.

## Данные и ограничения

Данные находятся в `%LOCALAPPDATA%\LLM Inspector\`: `data\inspector.db` (SQLite WAL schema v5), `settings.json` (совместимые v1/v2), `remote-access.json` (только DPAPI ciphertext). Существующая C# history/settings читаются без потери committed записей. Corrupt/newer schema не пересоздаётся автоматически.

Prompt, response, reasoning, tool arguments/results и user code не сохраняются, не индексируются и не логируются. Private payload проходит к backend, но telemetry извлекает только allowlisted metadata. Runtime/export tests используют synthetic canaries, не пользовательскую БД.

Retention: 7/30/90 дней или бессрочно, default 30 дней. Bounded oldest-first cleanup выполняется при старте и применении settings. Ручное удаление — только после preview exact scope и отдельного подтверждения. History bounded: 1000 requests/5000 resource rows; oversized analytics/export требует сузить период, не выдаёт неполный результат за полный. Mean/median/P95 требуют `n >= 3`, recurring error — `n >= 2`.

Optional operation/session correlation требует полного набора headers `X-LLM-Inspector-Session-Id`, `X-LLM-Inspector-Turn-Id` (non-empty GUID в 32-hex N format), положительного `X-LLM-Inspector-Turn-Sequence` и, для operations, `X-LLM-Inspector-Operation-Id`. Только соседние turns одной session/client/backend группируются; headers удаляются перед backend. Временная близость не является доказательством correlation.

Automated smoke не заменяет ручную Windows Home/Pro матрицу, реальный tray/autostart walkthrough, backend/client compatibility и controlled E12 overhead measurements. Remote telemetry не получает локальные GPU/process значения. Нет installer, automatic updates, Linux/macOS или новых protocols.

## Навигация

- [AGENTS.md](AGENTS.md) — repository router и execution boundaries.
- [docs/project-spec.md](docs/project-spec.md) — canonical scope, epics, 143 initial / 168 full atomic AC и Evidence.
- [docs/delivery-plan.md](docs/delivery-plan.md) — текущая Goal, checkpoint, current/previous readiness.
- [docs/delivery-plan-archive.md](docs/delivery-plan-archive.md) — история, не current source of truth.
- [docs/ci-cd-rules.md](docs/ci-cd-rules.md) — обязательный safety contract.
- [docs/architecture.md](docs/architecture.md) — актуальные components, ownership, data flow и ограничения.
- [docs/runbooks/windows-release.md](docs/runbooks/windows-release.md) — final portable release и Windows gates.
- [docs/runbooks/backend-lifecycle.md](docs/runbooks/backend-lifecycle.md) — безопасное управление локальным backend.
- [docs/runbooks/secure-remote-access.md](docs/runbooks/secure-remote-access.md) — private remote setup, threat model и LIVE checklist.
- [Upstream requirements](https://docs.google.com/document/d/1r4o0UiJohJf34j3nL56LWnOxGRi7WDC3jqoDIIjDTnA/edit) — provenance; canonical source of truth — project-spec.

Context Bundle Builder и AI tooling workstreams отсутствуют; optional документы для них не создаются.
