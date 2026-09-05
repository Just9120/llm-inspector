<script lang="ts">
  import type { domain } from '../../wailsjs/go/models';
  import Metric from './Metric.svelte';
  import { label, dateText, number } from './format.mjs';
  let { graph }: { graph: domain.OperationGraph } = $props();
</script>

<div class="section-heading">
  <h3>Операция · {label(graph.status)}</h3>
  <span class="badge">{graph.turns.length} turns · {graph.tools.length} tools</span>
</div>
<p>
  <code>{graph.id}</code> · {dateText(graph.started_at)}<br />Session:
  <code>{graph.session_id || 'нет данных'}</code>
</p>
{#if graph.truncated}<p class="notice warning">
    Детали ограничены лимитом. Это неполная операция.
  </p>{/if}
<ol class="operation-list">
  {#each graph.turns as turn}<li>
      <h4>Turn {turn.sequence} · {label(turn.outcome)} · {number(turn.duration_ms)} мс</h4>
      <p>{dateText(turn.started_at)} · {label(turn.error_type)}</p>
      <div class="metrics-grid">
        <Metric title="Доступно tools" metric={turn.available_tools} compact /><Metric
          title="Вызвано tools"
          metric={turn.invoked_tools}
          compact
        />
      </div>
      {#each graph.tools.filter((tool) => tool.turn_sequence === turn.sequence) as tool}<div
          class="tool-row"
        >
          <span
            ><code>{tool.name}</code> · №{tool.sequence}<br />{label(tool.status)} · {label(
              tool.error_type,
            )}</span
          ><Metric title="Длительность tool" metric={tool.duration} compact />
        </div>{/each}
    </li>{/each}
</ol>
