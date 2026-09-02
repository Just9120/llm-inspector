# Architecture baseline

> Status: `UNDEFINED`  
> Last verified: `2026-09-02T17:39:26Z`

## Фактическое состояние

Архитектура LLM Inspector ещё не определена и не реализована. Repository не содержит source code, dependency manifests, build configuration, schemas, migrations, runtime configuration, tests, workflows или deployment artifacts.

Поэтому на текущем Evidence нельзя утверждать наличие:

- logical/runtime components и module boundaries;
- request interception/proxy mechanism;
- backend adapters;
- telemetry collectors и normalization layer;
- desktop UI/background service/system tray process model;
- database или другого state owner;
- privacy enforcement boundary;
- deployment/update topology.

Product requirements теперь ратифицированы в `docs/project-spec.md`, но сами по себе не выбирают implementation architecture.

Подтверждённая topology boundary: server/runtime deployment target отсутствует и CD отключён. Приложение будет устанавливаться на Windows PC. Будущие package, signing, distribution и update mechanisms относятся к Windows release architecture, а не к server CD.

## Runtime и data flow

```text
UNDEFINED
```

Runtime conclusions отсутствуют: нет configuration, tests или runtime observation, которыми их можно подтвердить.

## Decisions required before implementation

1. Desktop stack и packaging/update model для Windows.
2. Transparent request observation boundary и compatibility strategy для OpenAI-compatible traffic.
3. Adapter contract для Ollama, llama.cpp и LM Studio.
4. Telemetry quality model: exact / calculated / estimated / unavailable.
5. Process model для window, system tray, background monitoring и failure isolation.
6. Local storage engine, schema ownership, retention/cleanup и crash consistency.
7. Privacy/threat model, включая content non-persistence и local network defaults.
8. Resource collector APIs, sampling policy и performance budget.
9. Test strategy для streaming, tool calls, concurrency, backend variation и privacy invariants.
10. Release, signing, distribution, update и LIVE verification model.

Эти пункты — decision backlog, а не утверждённая architecture и не implementation authorization.

## Known technical risks

| Risk | Почему актуален уже сейчас | Treatment |
|---|---|---|
| Scope слишком широк для одного vertical slice | Initial release содержит 139 atomic AC в 12 epics | `DEFER`: delivery разбивать на bounded Goals; не пытаться реализовать весь release одним изменением |
| Privacy invariant зависит от выбранной interception boundary | Нельзя доказать non-persistence до выбора data flow и logging policy | `DOCUMENT`: threat model и negative tests в отдельной approved Goal |
| Backend telemetry неоднородна | Требуется единая semantics без fabricated values | `DOCUMENT`: adapter capability matrix и provenance model |
| Resource attribution может быть недостоверной | Windows/driver APIs не гарантируют per-request/per-process linkage | `DEFER`: spike с quality labels до product promise |
| Windows release Evidence не определено | CD на runtime host не нужен, но package/signing/distribution topology ещё не выбрана | `DOCUMENT`: определить build/release Evidence до release Goal |
