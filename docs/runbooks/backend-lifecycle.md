# Управление локальным backend

> Scope: B01 в single-main product line. Historical `v1.0.0-rc.3` этих controls не содержит; следующий final `v1.0.0` из `main` содержит их после required validation.

## Safety boundary

LLM Inspector управляет только backend process, который сам запустил после явного подтверждения exact executable path, version и literal-loopback endpoint. Если порт уже занят, Inspector показывает PID владельца и ничего не изменяет. Перед force stop повторно сверяются PID, process start time и полный executable path; PID-only ownership запрещён.

Команды выполняются через typed argv без shell (direct process/argv в Go). UI не принимает arbitrary arguments/environment, public bind, CORS flags, service commands, install/update/download operations или credentials. Stop, restart и model switch блокируются, пока есть active Inspector requests, и показывают их количество. После crash автоматического recovery нет.

Go lifecycle и desktop wiring проверены на synthetic processes; реальные runtime/version checks выполняются отдельно. В Go detached LM Studio listener должен принадлежать Job Object, созданному Inspector до запуска CLI. Уже работающий GUI/daemon остаётся внешним; его нужно самостоятельно закрыть перед managed start. Wildcard listener не принимается как подтверждённый loopback target. Закрытие Inspector не останавливает backend; после нового запуска он считается внешним.

## Первый запуск

1. Откройте раздел «Backend» и выберите `Ollama`, `llama.cpp` или `LM Studio`.
2. Нажмите «Найти runtime». Если runtime отсутствует в standard paths/PATH, укажите полный путь к его `.exe`.
3. Проверьте показанные exact path, version, compatibility status и endpoint.
4. Нажмите «Подтверждаю runtime и endpoint». Любое изменение local port создаёт новый endpoint и требует повторного подтверждения.
5. При необходимости настройте параметры и нажмите «Запустить».

`Проверено` означает exact runtime/version с Evidence указанной Inspector revision в embedded matrix. Историческая C# revision не подтверждает LIVE совместимость текущего Go executable; UI показывает эту оговорку. `Совместимо` означает, что используется документированный capability contract, но exact runtime matrix ещё не воспроизведён. `Только наблюдение` не разрешает lifecycle. `Не поддерживается` блокирует lifecycle.

## Model load

- Ollama: укажите exact installed model ID. Go Inspector проверяет его наличие через `/api/tags`, инициирует native `POST /api/generate` с пустым prompt и подтверждает фактический loaded ID через `/api/ps`. `/api/tags` сам по себе подтверждает только установку, не загрузку.
- llama.cpp: выберите существующий `.gguf` по полному пути. Model switch выполняется безопасным restart того же confirmed executable с последней valid typed configuration и подтверждается через `/v1/models`.
- LM Studio: выберите model key из `lms ls --json`. Inspector вызывает official `lms load` и подтверждает exact identity через `lms ps --json`, не совпадение подстроки. GPU `auto` использует native default (флаг `--gpu` не передаётся).

Загрузка/установка/обновление model не выполняется. Неуспешное или неоднозначное подтверждение model identity не считается успешным load.

## Allowlisted parameters

| Backend | Parameters |
|---|---|
| Ollama | local port, context, keep-alive, parallel requests, max loaded models, max queue |
| llama.cpp | local port, context, GPU layers `auto/off/all/N`, CPU threads, parallel slots |
| LM Studio | local port, context, GPU offload `auto/off/max/0..1`, model TTL, model ID |

Пустое значение возвращает native backend default; «Вернуть штатные значения» сбрасывает весь профиль. Parameter validation происходит до создания process/command. Новая configuration применяется при следующей подходящей операции start/model load/restart; local port нельзя менять у работающего process.

Lifecycle endpoint и gateway destination — разные explicit settings. Если вы меняете local port относительно launch configuration Inspector, при следующем запуске Inspector передайте совпадающий `--backend` и `--backend-url=http://127.0.0.1:<port>/`; Inspector не перенаправляет уже запущенный gateway скрыто.

## Stop, restart и recovery

- Повторный start idempotent и не создаёт второй process.
- LM Studio сначала получает official `lms server stop`; затем используется bounded exact-process fallback, только если тот же owner ещё жив.
- Для attached Ollama/llama.cpp сначала отправляется Windows graceful close, затем после bounded wait допускается force stop exact identity.
- Если readiness не подтверждён, Inspector очищает только созданный им partial process.
- Состояние `Crashed` не запускает automatic restart. Устраните причину и нажмите «Перезапустить».

## Compatibility Evidence

Embedded `internal/lifecycle/config/runtime-compatibility.json` содержит version match, capabilities, Windows matrix, verification date, exact Inspector evidence revision, sanitized evidence и limitations. Ollama `0.33.2` имеет исторический status `verified` для C# reference revision; Go walkthrough ещё не выполнен. llama.cpp `b10516` и LM Studio `lms 0.0.47+` остаются `compatible`/`PENDING_EXTERNAL_GATE` до actual Windows runs; это не блокирует safe code delivery и не выдаётся за verified compatibility.
