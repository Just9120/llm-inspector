# Secure remote access runbook

> Scope: `BACKLOG-02`, Tailscale Serve first profile
> Runtime deployment: `N/A`
> Required terminal gate: actual encrypted two-host `LIVE` Evidence

## 1. Что реализовано

LLM Inspector никогда не открывает LAN/public listener: Go HTTP gateway продолжает слушать один literal-loopback endpoint `127.0.0.1`. Private ingress создаётся только отдельной явной командой пользователя через Tailscale Serve. Inspector не устанавливает Tailscale, не выполняет login, не меняет tailnet ACL и не запускает/останавливает Serve.

У ingress два независимых authentication factors:

1. Tailscale Serve добавляет `Tailscale-User-Login` только для authenticated user traffic внутри tailnet. Funnel traffic этого header не получает и отклоняется Inspector.
2. Inspector требует отдельный random `256-bit` bearer token. Token хранится только как Windows DPAPI CurrentUser ciphertext в `%LOCALAPPDATA%\LLM Inspector\remote-access.json`, показывается при creation/rotation и не может быть прочитан обратно через UI.

Remote backend задаётся только explicit option `--remote-backend-url=https://<node>.<tailnet>.ts.net[:port]/`. Plain HTTP, public/non-Tailscale hostname, IP literal, credentials, path, query и fragment fail closed. Backend на другом PC остаётся loopback-only и публикуется в tailnet через private HTTPS Serve, а не через LAN/wildcard bind.

## 2. Preconditions

- Обе машины принадлежат нужному tailnet и имеют отдельные authenticated identities.
- Tailnet HTTPS включён, ACL разрешает только intended source user/device и destination.
- Первый verified profile требует user identity. Tailscale не добавляет identity headers для tagged devices; такой VPS не может пройти текущий Inspector ingress gate и остаётся unsupported до отдельного authenticated design.
- На Inspector PC gateway показывает listener вида `http://127.0.0.1:5117/`.
- Funnel не настроен для выбранного HTTPS port. Public Internet exposure запрещён даже при наличии application token.
- Token не копируется в repository, issue/PR, логи, shell transcript или documentation.

Актуальный Tailscale contract: [Serve делится сервисом только внутри tailnet, применяет ACL и добавляет identity headers](https://tailscale.com/docs/features/tailscale-serve); [CLI reference описывает `--bg`, HTTPS default, status и disable](https://tailscale.com/docs/reference/tailscale-cli/serve). Funnel является отдельным public-Internet механизмом и в этом runbook не используется.

## 3. Включение Inspector ingress

1. Запустите Inspector локально и проверьте в UI exact loopback listener/port.
2. В разделе «Настройки → Защищённое удалённое подключение» подтвердите все четыре условия: private HTTPS Serve, user identity, intended ACL, Funnel выключен.
3. Нажмите «Включить доступ».
4. Скопируйте показанный token непосредственно в secret storage клиента. После скрытия token Inspector его повторно не показывает; при потере выполните rotation.
5. В отдельном PowerShell пользователя, управляющего Tailscale, выполните для default port:

```powershell
tailscale serve --bg --https=443 http://127.0.0.1:5117
tailscale serve status
tailscale funnel status
```

Expected state:

- Serve показывает private URL `https://<inspector-node>.<tailnet>.ts.net` и proxy target `http://127.0.0.1:5117`;
- Funnel status не показывает public route для этого target/port;
- Inspector listener по-прежнему `127.0.0.1`, не `0.0.0.0`, `[::]`, LAN IP или Tailscale IP.

Не используйте `tailscale funnel`, `--http`, TCP forwarding, wildcard backend bind или router/NAT port forwarding.

## 4. Настройка LibreChat на VPS

LibreChat поддерживает OpenAI-compatible custom endpoints: `baseURL` задаётся в `librechat.yaml`, а secret рекомендуется брать из environment variable, не хранить прямо в YAML ([официальная инструкция](https://www.librechat.ai/docs/quick_start/custom_endpoints), [описание `baseURL`](https://www.librechat.ai/docs/configuration/librechat_yaml/object_structure/custom_endpoint)).

На VPS сохраните token как secret environment value `LLM_INSPECTOR_TOKEN`. Не коммитьте реальное значение. Пример `librechat.yaml`:

```yaml
version: 1.3.13
endpoints:
  custom:
    - name: 'LLM Inspector'
      apiKey: '${LLM_INSPECTOR_TOKEN}'
      baseURL: 'https://<inspector-node>.<tailnet>.ts.net/v1'
      models:
        default: ['<exact-model-id>']
        fetch: true
      titleConvo: true
      titleModel: 'current_model'
      modelDisplayLabel: 'Local LLM via Inspector'
```

Перезапустите LibreChat его штатной процедурой. `GET /v1/models` и `POST /v1/chat/completions` должны проходить; Inspector-reserved client paths также доступны под тем же origin. Проверка без token и с неверным token обязана вернуть `401`, а Serve/Funnel-like request без `Tailscale-User-Login` — `403`.

## 5. Backend на другом PC

На backend PC runtime должен слушать только loopback. Пример для Ollama default port:

```powershell
tailscale serve --bg --https=443 http://127.0.0.1:11434
tailscale serve status
tailscale funnel status
```

На Inspector PC запустите exact executable с explicit remote target:

```powershell
.\LlmInspector.exe --backend=ollama --remote-backend-url=https://<backend-node>.<tailnet>.ts.net/
```

Для development run:

```powershell
.\build\bin\LlmInspector.exe --backend=ollama --remote-backend-url=https://<backend-node>.<tailnet>.ts.net/
```

UI отдельно показывает:

- remote target availability (`Unknown`, `Probing`, `Available`, `Unavailable`);
- `DNS+TCP connect` latency как calculated network-connect probe;
- request TTFT/total duration/inference telemetry в существующих request surfaces.

DNS+TCP connect не включает TLS handshake и backend inference, поэтому не вычитается из total duration и не называется чистым network RTT. Для remote request local Windows CPU/RAM/process/GPU metrics имеют `unavailable`; exact gateway byte counters могут оставаться доступны.

Другие WireGuard/private overlays остаются `Compatible`, но текущий fail-closed CLI validator принимает только first-profile `HTTPS *.ts.net`. Их support требует отдельного reviewed profile и reproduction; они не называются `Проверено`.

## 6. Token rotation и отключение

Rotation:

1. Снова подтвердите security boundary.
2. Нажмите «Сменить токен» и перенесите новый token в client secret storage.
3. Старый token становится недействительным сразу после успешной atomic save.
4. Перезапустите/reload client configuration и повторите negative/positive checks.

Отключение:

1. Нажмите «Выключить»: persisted token удаляется, remote requests начинают fail closed.
2. Удалите exact Serve mapping той же конфигурацией, которой он был создан:

```powershell
tailscale serve --bg --https=443 http://127.0.0.1:5117 off
tailscale serve status
```

Не применяйте `tailscale serve reset`, если на машине существуют другие intended Serve mappings: reset имеет более широкий scope.

## 7. Threat model и controls

| Threat | Control | Residual / gate |
|---|---|---|
| LAN/public доступ к Inspector | Единственный Go HTTP bind — `127.0.0.1`; environment URL/bind overrides не применяются | Local same-user/process threats вне network boundary |
| Funnel/public proxy | Non-loopback Host без Tailscale user identity получает `403`; runbook требует отдельную проверку Funnel status | Пользователь всё ещё может вручную настроить запрещённый Funnel; это не действие Inspector |
| Tailnet participant без разрешения | Tailnet ACL + Serve user identity + separate bearer token | Tagged devices без user identity отклоняются в first profile |
| Stolen/replayed application token | 256-bit random token, constant-time compare, immediate rotation/revocation | Bearer token остаётся replayable до rotation; клиент обязан защищать secret |
| Token disclosure at rest | DPAPI CurrentUser; JSON содержит только ciphertext; atomic replace | Malware в том же Windows user context вне contract |
| Token leak в backend | Remote ingress Authorization и Tailscale/proxy identity headers удаляются перед forwarding | Local backend credentials продолжают проходить по local configured policy |
| Unencrypted/misdirected remote backend | Только explicit `https://*.ts.net/`, normal TLS validation, redirects disabled | DNS/TLS/tailnet correctness подтверждается LIVE test |
| False latency attribution | Отдельный DNS+TCP connect metric с versioned derivation; inference metrics не вычисляются вычитанием | Probe не является full network RTT/TLS latency |
| False local resource attribution | Remote target не запускает Windows host/process/GPU probe; values остаются `unavailable` | Remote host resource telemetry не реализована |
| Secret/config corruption | Unknown JSON fields, schema mismatch, DPAPI/user mismatch и invalid token fail closed | Recovery — disable/recreate config under explicit user control |

## 8. Required LIVE checklist

До `LIVE: ✅` сохранить sanitized Evidence exact revision/artifact и выполнить:

1. Windows Inspector host и реальный VPS/second PC в одном tailnet; exact OS/Tailscale versions записаны без identities/secrets.
2. `tailscale serve status` подтверждает private HTTPS mapping; Funnel status подтверждает отсутствие public mapping.
3. Listener Inspector остаётся exact loopback.
4. Remote `GET /v1/models`: no token → `401`; wrong token → `401`; correct token + Serve identity → success.
5. Remote streaming `POST /v1/chat/completions` проходит без semantic/order regression; application token и identity headers не доходят до backend.
6. LibreChat на VPS делает реальный request через custom endpoint.
7. Remote backend target с другого PC переходит `Available`; отключение target даёт `Unavailable` без fabricated latency.
8. Request UI показывает DNS+TCP connect отдельно от TTFT/total duration; local process/GPU attribution для remote backend — `unavailable`.
9. Rotation немедленно инвалидирует старый token; disable отклоняет remote request; exact Serve mapping затем удалён.

Пока хотя бы один обязательный пункт не подтверждён фактическим two-host runtime, `BACKLOG-02` остаётся `IN PROGRESS / PENDING_EXTERNAL_GATE`, `LIVE: —`; automated loopback/stub tests не подменяют LIVE Evidence.
