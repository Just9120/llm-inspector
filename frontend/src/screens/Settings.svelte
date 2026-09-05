<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import { background, history } from '../../wailsjs/go/models';
  import type { desktop } from '../../wailsjs/go/models';
  import * as api from '../../wailsjs/go/desktop/Facade';
  import Metric from '../lib/Metric.svelte';
  import { errorText, localInput, utcInput, label } from '../lib/format.mjs';
  let { viewState, trayAvailable }: { viewState: desktop.ViewState; trayAvailable: boolean } =
    $props();
  let settings = $state<Omit<background.Settings, 'convertValues'>>(
    untrack(() => JSON.parse(JSON.stringify(viewState.settings))),
  );
  let retention = $state('30_days'),
    busy = $state(false),
    error = $state(''),
    message = $state(''),
    remoteConfirmed = $state(false),
    token = $state('');
  let all = $state(false),
    from = $state(localInput(new Date(Date.now() - 86400000))),
    to = $state(localInput(new Date(Date.now() + 60000))),
    clear = $state<history.ClearPreview | null>(null),
    clearConfirmed = $state(false);
  const events: [keyof background.NotificationSettings, string][] = [
    ['backend_unavailable', 'Backend недоступен'],
    ['long_operation_completed', 'Завершена долгая операция (от 60 с)'],
    ['recurring_error', 'Повторяющаяся ошибка'],
    ['high_context_usage', 'Контекст заполнен от 90%'],
  ];
  async function perform(task: () => Promise<void>) {
    if (busy) return;
    busy = true;
    error = '';
    message = '';
    try {
      await task();
    } catch (e) {
      error = errorText(e);
    } finally {
      busy = false;
    }
  }
  async function remoteAction(rotate = false) {
    await perform(async () => {
      token = '';
      const result = rotate
        ? await api.RotateRemoteToken(remoteConfirmed)
        : await api.EnableRemote(remoteConfirmed);
      token = result.one_time_token ?? '';
      remoteConfirmed = false;
      message = result.snapshot.message;
    });
  }
  onMount(() => {
    void perform(async () => {
      retention = await api.GetRetention();
    });
  });
</script>

{#if error}<p class="notice warning" role="alert">{error}</p>{/if}{#if message}<p
    class="notice"
    role="status"
  >
    {message}
  </p>{/if}
<section class="panel">
  <h2>Режим наблюдения</h2>
  <p>
    Чем чаще измерения, тем подробнее шкала и выше накладные расходы. Режим не меняет настройки
    модели.
  </p>
  <div class="profile-grid">
    {#each [{ id: 'saver', title: 'Бережный', detail: 'Каждые 2 с · минимальная частота' }, { id: 'balanced', title: 'Сбалансированный', detail: 'Каждую 1 с · по умолчанию' }, { id: 'detailed', title: 'Детальный', detail: 'Каждые 0,5 с · для диагностики' }, { id: 'custom', title: 'Свой профиль', detail: '250–10 000 мс · без release evidence' }] as p}<label
        class:selected={settings.monitoring.profile === p.id}
        class="profile"
        ><input type="radio" bind:group={settings.monitoring.profile} value={p.id} /><strong
          >{p.title}</strong
        ><small>{p.detail}</small></label
      >{/each}
  </div>
  {#if settings.monitoring.profile === 'custom'}<label
      >Интервал, мс<input
        type="number"
        min="250"
        max="10000"
        step="1"
        bind:value={settings.monitoring.custom_sampling_interval_milliseconds}
      /></label
    >{/if}
  <p class="muted">
    Профили задают частоту сбора. CPU/RAM/throughput budgets проверяются отдельным контролируемым
    benchmark; выбор профиля не означает, что эти проверки уже пройдены.
  </p>
  <button
    class="subtle"
    onclick={() => {
      settings.monitoring.profile = 'balanced';
      settings.monitoring.custom_sampling_interval_milliseconds = 1000;
    }}>Вернуть сбалансированный профиль</button
  >
</section>
<section class="panel">
  <h3>Фоновая работа и уведомления</h3>
  <p>
    {trayAvailable
      ? 'При закрытии окна наблюдение продолжается в tray. Для полной остановки выберите «Выйти».'
      : 'Windows tray недоступен: закрытие окна завершит приложение.'}
  </p>
  <label class="check"
    ><input type="checkbox" bind:checked={settings.autostart_enabled} />Запускать при входе в
    Windows</label
  >
  <div class="check-grid">
    {#each events as [key, title]}<label class="check"
        ><input type="checkbox" bind:checked={settings.notifications[key]} />{title}</label
      >{/each}
  </div>
  <label class="check"
    ><input type="checkbox" bind:checked={settings.notifications.silent_mode} />Без звука</label
  >
  <p class="muted">
    Повтор события — не чаще 1 раза за 15 минут. Общий лимит — 3 уведомления за 10 минут.
  </p>
  <div class="actions">
    <button
      disabled={busy}
      onclick={() =>
        perform(async () => {
          await api.SaveSettings(new background.Settings(settings));
          message = 'Настройки сохранены';
        })}>Сохранить настройки</button
    ><button
      class="secondary"
      disabled={busy}
      onclick={() =>
        perform(async () => {
          const paused = await api.ToggleNotifications();
          message = paused ? 'Уведомления приостановлены' : 'Уведомления возобновлены';
        })}
      >{viewState.notifications_paused
        ? 'Возобновить уведомления'
        : 'Приостановить уведомления'}</button
    >
  </div>
</section>
<section class="panel">
  <h3>Данные и срок хранения</h3>
  <p>
    Локально сохраняются времена, токены, model/backend/client identifiers, названия tools, статусы
    и категории ошибок, resource metrics, версии и ID конфигураций. Prompt, response, reasoning,
    tool arguments/results и user code не сохраняются.
  </p>
  <label
    >История запросов, sessions, operations, turns/tools и resource samples<select
      bind:value={retention}
      ><option value="7_days">7 дней</option><option value="30_days">30 дней</option><option
        value="90_days">90 дней</option
      ><option value="indefinite">Без срока</option></select
    ></label
  >
  <p class="muted">
    Настройки и DPAPI-защищённые remote credentials хранятся до изменения или удаления
    пользователем, отдельно от retention истории. WebView2 хранит локальный служебный cache; UI не
    использует localStorage для telemetry или токенов.
  </p>
  <button
    class="secondary"
    disabled={busy}
    onclick={() =>
      perform(async () => {
        const count = await api.SetRetention(retention);
        clear = null;
        message = `Срок применён; удалено старых записей: ${count}`;
      })}>Применить срок и удалить старые записи</button
  >
  <details>
    <summary>Ручная очистка истории</summary>
    <label class="check"
      ><input
        type="checkbox"
        bind:checked={all}
        onchange={() => {
          clear = null;
          clearConfirmed = false;
        }}
      />Вся техническая история</label
    >
    {#if !all}<div class="toolbar">
        <label
          >С<input type="datetime-local" bind:value={from} onchange={() => (clear = null)} /></label
        ><label
          >По<input type="datetime-local" bind:value={to} onchange={() => (clear = null)} /></label
        >
      </div>{/if}
    <button
      class="secondary"
      disabled={busy}
      onclick={() =>
        perform(async () => {
          clearConfirmed = false;
          clear = null;
          clear = await api.PreviewClear(
            new history.ClearScope(
              all ? { all: true } : { all: false, from: utcInput(from), to: utcInput(to) },
            ),
          );
        })}>Посчитать записи для удаления</button
    >
    {#if clear}<div class="notice warning">
        <p>Scope: {clear.scope.all ? 'вся история' : `${from} — ${to}`}. Будут удалены:</p>
        {#each Object.entries(clear.counts) as [category, count]}<p>
            {category}: {count}
          </p>{/each}<label class="check"
          ><input type="checkbox" bind:checked={clearConfirmed} />Подтверждаю безвозвратную очистку
          указанного scope</label
        ><button
          class="danger"
          disabled={busy || !clearConfirmed}
          onclick={() =>
            perform(async () => {
              await api.ConfirmClear(clear!.token, clearConfirmed);
              clear = null;
              clearConfirmed = false;
              message = 'Указанная история удалена';
            })}>Удалить подтверждённые записи</button
        >
      </div>{/if}
  </details>
</section>
<section class="panel">
  <details>
    <summary>Защищённое удалённое подключение</summary>
    <p>{viewState.remote_access.message}</p>
    <span class="badge">{viewState.remote_access.enabled ? 'Включено' : 'Выключено'}</span>
    <p>
      Только private HTTPS Tailscale Serve → loopback proxy. Требуются tailnet identity, ACL и
      отдельный application Bearer token. Funnel должен быть выключен. Inspector не настраивает
      Tailscale и не открывает firewall.
    </p>
    <label class="check"
      ><input type="checkbox" bind:checked={remoteConfirmed} />Подтверждаю private HTTPS Serve,
      нужные user identity/ACL и выключенный Funnel</label
    >
    <div class="actions">
      <button
        class="secondary"
        disabled={busy ||
          !remoteConfirmed ||
          !viewState.remote_access.available ||
          viewState.remote_access.enabled}
        onclick={() => remoteAction()}>Включить доступ</button
      ><button
        class="secondary"
        disabled={busy || !remoteConfirmed || !viewState.remote_access.enabled}
        onclick={() => remoteAction(true)}>Сменить токен</button
      ><button
        class="secondary"
        disabled={busy || !viewState.remote_access.enabled}
        onclick={() =>
          perform(async () => {
            token = '';
            await api.DisableRemote();
            message = 'Удалённый доступ выключен';
          })}>Выключить</button
      >
    </div>
    {#if token}<div class="notice warning">
        <p>
          Токен показан один раз. Скопируйте в конфигурацию разрешённого клиента, не в публичный
          issue или снимок.
        </p>
        <textarea
          readonly
          aria-label="Одноразово показанный Bearer token"
          value={token}
          spellcheck="false"></textarea><button class="secondary" onclick={() => (token = '')}
          >Скрыть токен</button
        >
      </div>{/if}
    <h4>Backend на другом компьютере</h4>
    <p>
      Запуск: <code
        >LlmInspector.exe --backend=ollama
        --remote-backend-url=https://backend.example-tailnet.ts.net</code
      >. Это пример: укажите адрес своего private Serve.
    </p>
    {#if viewState.remote_backend}<p>
        {viewState.remote_backend.message} · {label(viewState.remote_backend.availability)}
      </p>
      <Metric
        title="DNS + TCP connection (не inference latency)"
        metric={viewState.remote_backend.network_connect_latency}
      /><button
        class="secondary"
        disabled={busy}
        onclick={() =>
          perform(async () => {
            const result = await api.ProbeRemoteBackend();
            message = result.message;
          })}>Проверить сеть сейчас</button
      >{/if}
  </details>
</section>
