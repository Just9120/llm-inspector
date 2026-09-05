<script lang="ts">
  import type { history } from '../../wailsjs/go/models';
  let {
    filter = $bindable(),
    from = $bindable(),
    to = $bindable(),
    prefix = 'Период',
  }: { filter: history.Filter; from: string; to: string; prefix?: string } = $props();
</script>

<div class="filter-grid">
  <label>{prefix}: с<input type="datetime-local" bind:value={from} /></label><label
    >По<input type="datetime-local" bind:value={to} /></label
  >
  <label
    >Клиент<select bind:value={filter.client}
      ><option value="">Все</option><option value="opencode">OpenCode</option><option value="hermes"
        >Hermes</option
      ><option value="cline">Cline</option><option value="open-webui">Open WebUI</option><option
        value="generic">Другой / неизвестный</option
      ></select
    ></label
  >
  <label
    >Backend<select bind:value={filter.backend}
      ><option value="">Все</option><option value="ollama">Ollama</option><option value="llama-cpp"
        >llama.cpp</option
      ><option value="lm-studio">LM Studio</option></select
    ></label
  >
  <label
    >Модель<input
      bind:value={filter.model}
      placeholder="Точный ID или все"
      maxlength="128"
    /></label
  >
</div>
<details>
  <summary>Дополнительные фильтры</summary>
  <div class="filter-grid">
    <label
      >Session ID<input bind:value={filter.session_id} placeholder="GUID" maxlength="36" /></label
    ><label
      >Operation ID<input
        bind:value={filter.operation_id}
        placeholder="GUID"
        maxlength="36"
      /></label
    >
    <label
      >Статус<select bind:value={filter.outcome}
        ><option value="">Все</option><option value="completed">Завершён</option><option
          value="backend_unavailable">Backend недоступен</option
        ><option value="client_cancelled">Отменён клиентом</option><option value="relay_failed"
          >Ошибка relay</option
        ></select
      ></label
    >
    <label
      >Тип ошибки<select bind:value={filter.error_type}
        ><option value="">Все</option><option value="none">Нет</option><option
          value="connection_refused">Соединение отклонено</option
        ><option value="model_loading">Загрузка модели / 503</option><option value="http_api_error"
          >HTTP/API</option
        ><option value="timeout">Тайм-аут</option><option value="context_overflow">Контекст</option
        ><option value="client_cancelled">Отмена клиентом</option><option value="backend_crash"
          >Сбой backend</option
        ><option value="relay_failed">Ошибка relay</option></select
      ></label
    >
  </div>
</details>
