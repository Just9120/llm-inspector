<script lang="ts">
  import { onMount } from 'svelte';
  import type { lifecycle } from '../../wailsjs/go/models';
  import * as api from '../../wailsjs/go/desktop/Facade';
  import { label, errorText } from '../lib/format.mjs';
  let { activeCount, proxyEndpoint }: { activeCount: number; proxyEndpoint: string } = $props();
  let backend = $state('Ollama'),
    path = $state(''),
    snapshot = $state<lifecycle.Snapshot | null>(null),
    parameters = $state<lifecycle.Parameter[]>([]),
    values = $state<Record<string, string>>({}),
    models = $state<string[]>([]),
    model = $state(''),
    busy = $state(false),
    error = $state(''),
    message = $state('');
  const names: Record<string, string> = {
    Ollama: 'Ollama',
    LlamaCpp: 'llama.cpp',
    LmStudio: 'LM Studio',
  };
  const capabilities = $derived(snapshot?.target?.compatibility.capabilities ?? []);
  async function refresh() {
    snapshot = await api.GetLifecycle(backend);
    values = { ...snapshot.parameters };
    parameters = await api.GetLifecycleParameters(backend);
  }
  async function perform(task: () => Promise<void>) {
    if (busy) return;
    busy = true;
    error = '';
    message = '';
    try {
      await task();
      await refresh();
    } catch (e) {
      error = errorText(e);
      try {
        await refresh();
      } catch {
        /* Original failure remains visible. */
      }
    } finally {
      busy = false;
    }
  }
  async function change() {
    snapshot = null;
    models = [];
    model = '';
    path = '';
    await perform(refresh);
  }
  onMount(() => {
    void perform(refresh);
  });
</script>

<section class="panel">
  <div class="section-heading">
    <div>
      <h2>Локальные backend</h2>
      <p>Управление только подтверждённым runtime, запущенным этим Inspector.</p>
    </div>
    <span class="badge">Активных запросов: {activeCount}</span>
  </div>
  <div class="toolbar">
    <label
      >Backend<select bind:value={backend} onchange={change} disabled={busy}
        ><option>Ollama</option><option value="LlamaCpp">llama.cpp</option><option value="LmStudio"
          >LM Studio</option
        ></select
      ></label
    ><span class="status-pill">{label(snapshot?.state)}</span>
  </div>
  <div class="path-input">
    <label
      >Путь к {names[backend]}<input
        bind:value={path}
        placeholder="Автопоиск или абсолютный путь к .exe"
        disabled={busy}
      /></label
    ><button
      class="secondary"
      disabled={busy}
      onclick={() =>
        perform(async () => {
          path = await api.ChooseExecutable();
        })}>Выбрать…</button
    ><button
      disabled={busy}
      onclick={() =>
        perform(async () => {
          snapshot = await api.DiscoverBackend(backend, path);
        })}>Найти runtime</button
    >
  </div>
  <p class="muted">
    Inspector не устанавливает backend и не скачивает модели. Установленный внешний процесс не будет
    остановлен или присвоен.
  </p>
  {#if snapshot?.target}
    <div class="target-card">
      <h3>Проверьте точную цель</h3>
      <p><code>{snapshot.target.executable}</code></p>
      <p>
        Версия: {snapshot.target.version}<br />Endpoint: <code>{snapshot.target.endpoint}</code><br
        />Совместимость: {label(snapshot.target.compatibility.status)}
      </p>
      {#if !snapshot.confirmed}<button
          class="secondary"
          disabled={busy || !capabilities.includes('start')}
          onclick={() =>
            perform(async () => {
              await api.ConfirmBackend(backend, snapshot!.target!.confirmationToken);
            })}>Подтверждаю runtime и endpoint</button
        >{:else}<span class="badge success">Цель подтверждена</span>{/if}
      <details>
        <summary>Evidence и ограничения версии</summary>
        <p>
          Матрица содержит исторические проверки точных версий. Совместимость текущей Go-сборки с
          реальным runtime проверяется отдельно. Технический отчёт ниже сохраняет исходные
          identifiers и ограничения.
        </p>
        <pre>{JSON.stringify(snapshot.target.compatibility, null, 2)}</pre>
      </details>
    </div>
  {/if}
  {#if snapshot?.confirmed}<div class="actions">
      <button
        disabled={busy || !capabilities.includes('start')}
        onclick={() =>
          perform(async () => {
            await api.StartBackend(backend);
          })}>Запустить</button
      >
      <button
        class="secondary"
        disabled={busy || activeCount > 0 || !snapshot.owned || !capabilities.includes('stop')}
        onclick={() =>
          perform(async () => {
            await api.StopBackend(backend);
          })}>Остановить</button
      >
      <button
        class="secondary"
        disabled={busy || activeCount > 0 || !snapshot.owned || !capabilities.includes('restart')}
        onclick={() =>
          perform(async () => {
            await api.RestartBackend(backend);
          })}>Перезапустить</button
      >
      <button class="subtle" disabled={busy} onclick={() => perform(refresh)}
        >Проверить состояние</button
      >
    </div>{/if}
  {#if snapshot?.owned}<p class="muted">
      Owned PID: {snapshot.owned.pid}. При выходе из Inspector backend продолжит работу.
    </p>{/if}
  {#if activeCount > 0}<p class="notice">
      Остановка, restart и загрузка модели запрещены до завершения активных запросов.
    </p>{/if}
  <p class="muted">
    Текущий proxy направлен на <code>{proxyEndpoint}</code>. Изменение lifecycle-порта не
    перенастраивает proxy: при необходимости перезапустите Inspector с <code>--backend-url=…</code>.
  </p>
</section>
{#if error || snapshot?.error}<p class="notice warning" role="alert">
    {error || snapshot?.error}
  </p>{/if}
{#if busy}<p role="status">
    Операция выполняется. Запуск или загрузка модели могут занять несколько минут…
  </p>{/if}
{#if message}<p role="status">{message}</p>{/if}
<section class="panel">
  <h3>Установленная модель</h3>
  {#if backend === 'LlamaCpp'}<div class="path-input">
      <label
        >Файл GGUF<input bind:value={model} placeholder="Абсолютный локальный путь .gguf" /></label
      ><button
        class="secondary"
        disabled={busy}
        onclick={() =>
          perform(async () => {
            model = await api.ChooseModel();
          })}>Выбрать GGUF…</button
      >
    </div>
    <p class="muted">
      До запуска это выбор модели. Фактическая загрузка подтверждается после готовности backend.
    </p>
  {:else}<div class="toolbar">
      <label
        >Модель<select bind:value={model}
          ><option value="">Выберите установленную модель</option>{#each models as id}<option
              value={id}>{id}</option
            >{/each}</select
        ></label
      ><button
        class="secondary"
        disabled={busy || !snapshot?.confirmed || !capabilities.includes('model-load')}
        onclick={() =>
          perform(async () => {
            models = await api.GetModels(backend);
          })}>Прочитать список</button
      >
    </div>{/if}
  <button
    class="secondary"
    disabled={busy ||
      activeCount > 0 ||
      !model ||
      !snapshot?.confirmed ||
      !capabilities.includes('model-load')}
    onclick={() =>
      perform(async () => {
        await api.LoadModel(backend, model);
        message =
          backend === 'LlamaCpp' && snapshot?.state !== 'running'
            ? 'Модель выбрана. Запустите backend для подтверждения загрузки.'
            : 'Загрузка точной модели подтверждена backend.';
      })}>{backend === 'LlamaCpp' ? 'Выбрать / загрузить GGUF' : 'Загрузить модель'}</button
  >
  <p>Текущая выбранная / подтверждённая модель: <code>{snapshot?.model || 'нет данных'}</code></p>
</section>
<section class="panel">
  <details>
    <summary>Параметры runtime</summary>
    <p>
      Пустое поле — штатное значение backend. Применяется при следующем запуске; смена endpoint
      требует нового подтверждения.
    </p>
    <div class="filter-grid">
      {#each parameters as p}<label
          >{p.label}<input
            bind:value={values[p.id]}
            placeholder={p.default || 'Штатное значение'}
            maxlength="512"
          /><small
            >{p.hint}{#if p.maximum}
              · {p.minimum}–{p.maximum}{/if}</small
          ></label
        >{/each}
    </div>
    <div class="actions">
      <button
        class="secondary"
        disabled={busy || !snapshot?.confirmed || !capabilities.includes('parameters')}
        onclick={() =>
          perform(async () => {
            await api.SetBackendParameters(backend, { ...values });
            message = 'Параметры сохранены для следующего запуска';
          })}>Применить параметры</button
      ><button
        class="subtle"
        disabled={busy || !snapshot?.confirmed || !capabilities.includes('parameters')}
        onclick={() =>
          perform(async () => {
            await api.ResetBackendParameters(backend);
          })}>Вернуть штатные значения</button
      >
    </div>
  </details>
</section>
