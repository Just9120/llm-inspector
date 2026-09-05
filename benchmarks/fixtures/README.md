# Synthetic benchmark fixtures

Здесь хранятся только deterministic synthetic fixtures без captured user prompts, responses, source code
или credentials. Current EPIC-12 corpus находится в `epic12/v2/reference-workloads.json`; его SHA-256
закреплён automated test и меняется только явным reviewed contract update.

Fixture задаёт workload shapes, deterministic seed/output limits и reference runtime identity. Он не
является результатом измерений: release Evidence возникает только после минимум пяти чередующихся
paired `AB/BA` repetitions каждого built-in profile и отдельного idle run (`10 min` warm-up + `1 h`
measurement) на утверждённом reference hardware.

Revision v2 отражает explicit owner approval GOAL-007 от 2026-09-05: установленная Ollama 0.33.3
вместо 0.33.2. Только reference runtime version изменена; schema, model/digest, prompts, seed,
context, output limits, concurrency, durations и workload set побайтно сохранены. Frozen v1
остаётся historical reference; новый corpus не доказывает выполненные measurements.
