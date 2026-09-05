<script lang="ts">
  import { onMount } from 'svelte';
  import type { artifact } from '../../wailsjs/go/models';
  import { SavePreview } from '../../wailsjs/go/desktop/Facade';
  import { errorText } from './format.mjs';
  let { preview, onclose }: { preview: artifact.Artifact; onclose: () => void } = $props();
  let dialog: HTMLDialogElement;
  let busy = $state(false),
    message = $state('');
  onMount(() => dialog.showModal());
  async function save() {
    busy = true;
    try {
      message = (await SavePreview(preview.sha256))
        ? 'JSON сохранён локально. Ничего не отправлено.'
        : 'Сохранение отменено';
    } catch (e) {
      message = errorText(e);
    } finally {
      busy = false;
    }
  }
</script>

<dialog bind:this={dialog!} oncancel={onclose} aria-labelledby="preview-title">
  <div class="section-heading">
    <h2 id="preview-title">Проверьте состав JSON</h2>
    <button class="subtle" onclick={onclose} aria-label="Закрыть предпросмотр">✕</button>
  </div>
  <p>
    Только технические данные. Проверьте model/tool identifiers перед передачей другому человеку.
  </p>
  <p class="hash">SHA-256: <code>{preview.sha256}</code></p>
  <pre class="json-preview">{preview.json}</pre>
  <p role="status">{message}</p>
  <div class="actions">
    <button disabled={busy} onclick={save}>Сохранить этот JSON…</button><button
      class="secondary"
      onclick={onclose}>Закрыть</button
    >
  </div>
</dialog>
