<script lang="ts">
  import { beforeNavigate } from '$app/navigation';
  import {useDismiss, useError} from '.';
  import { t } from 'svelte-intl-precompile';
  import UnexpectedError from './UnexpectedError.svelte';

  let dialog: HTMLDialogElement;
  const error = useError();
  const dismiss = useDismiss();
  beforeNavigate(dismiss);
  $: {
    if (dialog) {
      if ($error) {
        open();
      } else {
        close();
      }
    }
  }

  function dismissClick(): void {
    close();
    dismiss();
  }

  function open(): void {
    dialog.showModal();
    dialog.classList.add('modal-open');
  }

  function close(): void {
    dialog.close();
    dialog.classList.remove('modal-open');
  }
</script>

<dialog bind:this={dialog} class="modal">
  <div class="modal-box bg-error text-error-content max-w-[95vw] w-[unset]">
    <UnexpectedError />
    <div class="flex justify-end modal-action">
      <button on:click={dismissClick} class="btn btn-ghost self-end">{$t('modal.dismiss')}</button>
    </div>
  </div>
</dialog>
