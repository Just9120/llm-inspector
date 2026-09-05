<script lang="ts">
  import type { domain } from '../../wailsjs/go/models';
  import Metric from './Metric.svelte';
  import { label, dateText, number } from './format.mjs';
  let { request }: { request: domain.Observation } = $props();
  const fields: [keyof domain.Telemetry, string][] = [
    ['prompt_tokens', 'Входные токены'],
    ['completion_tokens', 'Выходные токены'],
    ['cached_tokens', 'Cache'],
    ['reasoning_tokens', 'Reasoning tokens'],
    ['context_usage', 'Контекст: занято'],
    ['context_limit', 'Лимит контекста'],
    ['context_history', 'Контекст: история'],
    ['context_tools', 'Контекст: tools'],
    ['prompt_speed', 'Обработка prompt'],
    ['generation_speed', 'Генерация'],
    ['model_load_time', 'Загрузка модели'],
    ['queue_time', 'Очередь'],
  ];
</script>

<div class="section-heading">
  <div>
    <h3>{request.telemetry.model || 'Модель не определена'}</h3>
    <p>
      {label(request.client)} · {label(request.telemetry.backend)} · {dateText(request.started_at)}
    </p>
  </div>
  <span class="badge">{label(request.outcome)}</span>
</div>
<p>
  Длительность: {number(request.duration_ms)} мс · {label(request.telemetry.model_load)} · HTTP {request.http_status ??
    '—'}
</p>
{#if request.error_type !== 'none'}<p class="notice warning">
    {label(request.error_type)} · Источник: {label(request.error_origin)}
  </p>{/if}
<div class="metrics-grid">
  <Metric title="До первого токена (TTFT)" metric={request.ttft} />
  <Metric title="Изменение контекста" metric={request.context_change} />
  {#each fields as [key, title]}<Metric
      {title}
      metric={request.telemetry[key] as domain.Metric}
    />{/each}
</div>
<details>
  <summary>Tools и данные корреляции</summary>
  <div class="metrics-grid">
    <Metric title="Доступно tools" metric={request.agent.available_tools} /><Metric
      title="Вызвано tools"
      metric={request.agent.invoked_tools}
    />
  </div>
  <p>
    Результаты tools: {request.agent.tool_results ?? 'нет данных'}. Полнота деталей: {request.agent
      .details_complete
      ? 'подтверждена'
      : 'не подтверждена'}.
  </p>
  <p>Request: <code>{request.request_id}</code></p>
  {#if request.correlation}<p>
      Session: <code>{request.correlation.session_id}</code><br />Operation:
      <code>{request.correlation.operation_id || 'не установлена'}</code><br />Turn:
      <code>{request.correlation.turn_id}</code>
      · №{request.correlation.sequence}
    </p>{:else}<p>Корреляция недоступна: запросы не объединяются предположительно.</p>{/if}
  {#each request.agent.tools ?? [] as tool}<p>{tool.sequence}. <code>{tool.name}</code></p>{/each}
</details>
<details>
  <summary>Backend-specific metrics и версии runtime</summary>
  <div class="metrics-grid">
    {#each Object.entries(request.telemetry.backend_metrics ?? {}) as [key, metric]}<Metric
        title={key}
        {metric}
      />{/each}
  </div>
  <pre>{JSON.stringify(request.runtime ?? { availability: 'unavailable' }, null, 2)}</pre>
</details>
