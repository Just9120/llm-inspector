# Управление локальным backend

> Scope: B01 development line (`v1.1+`). Observation-only release `v1.0.0-rc.2` этих controls не содержит.

## Safety boundary

LLM Inspector управляет только backend process, который сам запустил после явного подтверждения exact executable path, version и literal-loopback endpoint. Если порт уже занят, Inspector показывает PID владельца и ничего не изменяет. Перед force stop повторно сверяются PID, process start time и полный executable path; PID-only ownership запрещён.

Команды выполняются через typed `ProcessStartInfo.ArgumentList` с `UseShellExecute=false`. UI не принимает arbitrary arguments/environment, public bind, CORS flags, service commands, install/update/download operations или credentials. Stop, restart и model switch блокируются, пока есть active Inspector requests, и показывают их количество. После crash автоматического recovery нет.

## Первый запуск

1. Откройте раздел «Управление локальным backend» и выберите `Ollama`, `llama.cpp` или `LM Studio`.
2. Нажмите «Найти и проверить». Если runtime отсутствует в standard paths/PATH, укажите полный путь к его `.exe`.
3. Проверьте показанные exact path, version, compatibility status и endpoint.
4. Установите checkbox подтверждения. Любое изменение local port создаёт новый endpoint и требует повторного подтверждения.
5. При необходимости настройте параметры и нажмите «Запустить».

`Проверено` означает exact runtime/version с локальным Evidence в embedded matrix. `Совместимо` означает, что используется документированный capability contract, но exact runtime matrix ещё не воспроизведён. `Только наблюдение` не разрешает lifecycle. `Не поддерживается` блокирует lifecycle.

## Model load

- Ollama: укажите exact installed model ID. Inspector инициирует native `POST /api/generate` без prompt и подтверждает ID через `/api/tags`.
- llama.cpp: выберите существующий `.gguf` по полному пути. Model switch выполняется безопасным restart того же confirmed executable с последней valid typed configuration и подтверждается через `/v1/models`.
- LM Studio: укажите model key из `lms ls`. Inspector вызывает official `lms load` и подтверждает exact identity через `lms ps`.

Загрузка/установка/обновление model не выполняется. Неуспешное или неоднозначное подтверждение model identity не считается успешным load.

## Allowlisted parameters

| Backend | Parameters |
|---|---|
| Ollama | local port, context, keep-alive, parallel requests, max loaded models, max queue |
| llama.cpp | local port, context, GPU layers `auto/off/all/N`, CPU threads, parallel slots |
| LM Studio | local port, context, GPU offload `auto/off/max/0..1`, model TTL, model ID |

Пустое значение возвращает native backend default; «Сбросить к defaults» сбрасывает весь профиль. Parameter validation происходит до создания process/command. Новая configuration применяется при следующей подходящей операции start/model load/restart; local port нельзя менять у работающего process.

Lifecycle endpoint и gateway destination — разные explicit settings. Если вы меняете local port относительно launch configuration Inspector, при следующем запуске Inspector передайте совпадающий `--backend` и `--backend-url=http://127.0.0.1:<port>/`; Inspector не перенаправляет уже запущенный gateway скрыто.

## Stop, restart и recovery

- Повторный start idempotent и не создаёт второй process.
- LM Studio сначала получает official `lms server stop`; затем используется bounded exact-process fallback, только если тот же owner ещё жив.
- Для attached Ollama/llama.cpp сначала отправляется Windows graceful close, затем после bounded wait допускается force stop exact identity.
- Если readiness не подтверждён, Inspector очищает только созданный им partial process.
- Состояние `Crashed` не запускает automatic restart. Устраните причину и нажмите «Перезапустить».

## Compatibility Evidence

Embedded `config/runtime-compatibility.json` содержит version match, capabilities, Windows matrix, verification date, exact Inspector evidence revision, sanitized evidence и limitations. Ollama `0.33.2` имеет status `verified`. llama.cpp `b10516` и LM Studio `lms 0.0.47+` остаются `compatible`/`PENDING_EXTERNAL_GATE` до actual Windows runs; это не блокирует safe code delivery и не выдаётся за verified compatibility.
