# Synthetic benchmark fixtures

Здесь хранятся только deterministic synthetic fixtures без captured user prompts, responses, source code
или credentials. Canonical EPIC-12 corpus находится в `epic12/v1/reference-workloads.json`; его SHA-256
закреплён automated test и меняется только явным reviewed contract update.

Fixture задаёт workload shapes, deterministic seed/output limits и reference runtime identity. Он не
является результатом измерений: release Evidence возникает только после минимум пяти чередующихся
paired `AB/BA` repetitions каждого built-in profile и отдельного idle run (`10 min` warm-up + `1 h`
measurement) на утверждённом reference hardware.
