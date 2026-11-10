<script lang="ts">
  import {Icon} from '$lib/components/ui/icon';
  import * as Tooltip from '$lib/components/ui/tooltip';
  import {useSaveHandler} from '$lib/services/save-event-service.svelte';
  import {cn} from '$lib/utils';
  import {watch} from 'runed';
  import {onDestroy} from 'svelte';
  import {prefersReducedMotion} from 'svelte/motion';
  import {fade} from 'svelte/transition';
  import {t} from 'svelte-i18n-lingui';

  // Fast saves never show "Saving…", and once shown it stays long enough to read.
  const SAVING_DELAY_MS = 1000;
  const SAVING_MIN_MS = 500;
  const JUST_SAVED_MS = 2000;

  const saveHandler = useSaveHandler();

  let savingVisible = $state(false);
  let savingVisibleSince = 0;
  let justSaved = $state(false);
  let savingTimer: ReturnType<typeof setTimeout> | undefined;
  let justSavedTimer: ReturnType<typeof setTimeout> | undefined;

  watch(() => saveHandler.saving, (saving) => {
    clearTimeout(savingTimer);
    if (saving) {
      if (!savingVisible) {
        savingTimer = setTimeout(() => {
          savingVisible = true;
          savingVisibleSince = Date.now();
        }, SAVING_DELAY_MS);
      }
    } else if (savingVisible) {
      savingTimer = setTimeout(() => {
        savingVisible = false;
        flashSaved();
      }, savingVisibleSince + SAVING_MIN_MS - Date.now());
    } else {
      flashSaved();
    }
  }, {lazy: true});

  function flashSaved() {
    if (saveHandler.lastResult !== 'saved') return;
    justSaved = true;
    clearTimeout(justSavedTimer);
    justSavedTimer = setTimeout(() => justSaved = false, JUST_SAVED_MS);
  }

  onDestroy(() => {
    clearTimeout(savingTimer);
    clearTimeout(justSavedTimer);
  });

  // A retry doesn't hide a failure: only a later successful save does.
  const status = $derived.by(() => {
    if (saveHandler.lastResult === 'failed') return 'failed';
    if (savingVisible) return 'saving';
    if (justSaved) return 'just-saved';
    if (saveHandler.lastResult === 'saved') return 'resting';
    return 'none';
  });

  // Cleared during a fast save so each save gets announced again.
  const announcement = $derived.by(() => {
    switch (status) {
      case 'failed': return $t`Not saved`;
      case 'saving': return $t`Saving…`;
      case 'none': return '';
      default: return saveHandler.saving ? '' : $t`Saved`;
    }
  });

  const tooltip = $derived(status === 'failed'
    ? $t`Your last change was not saved. Edit the field again to retry.`
    : $t`Your changes are saved on this device.`);
</script>

<span role="status" class="sr-only">{announcement}</span>
{#if status !== 'none'}
  <Tooltip.Root>
    <Tooltip.Trigger>
      <!-- Not focusable: there's nothing to act on, and the status region already announces it. -->
      {#snippet child({props: {tabindex: _tabindex, type: _type, ...props}})}
        <span
          {...props}
          aria-hidden="true"
          data-save-status={status}
          class={cn('flex items-center text-sm', status === 'failed' ? 'text-destructive' : 'text-muted-foreground')}>
          {#if status === 'failed' || status === 'saving' || status === 'just-saved'}
            <span
              class={cn('whitespace-nowrap', status !== 'failed' && 'max-md:hidden')}
              transition:fade={{duration: prefersReducedMotion.current ? 0 : 150}}>
              {#if status === 'failed'}
                {$t`Not saved`}
              {:else if status === 'saving'}
                {$t`Saving…`}
              {:else}
                {$t`Saved`}
              {/if}
            </span>
          {/if}
          <span class="size-9 flex items-center justify-center">
            {#if status === 'failed'}
              <Icon icon="i-mdi-alert-circle" class="size-5" />
            {:else if status === 'saving'}
              <Icon icon="i-mdi-loading" class="size-5 animate-spin motion-reduce:hidden" />
              <Icon icon="i-mdi-content-save-outline" class="size-5 motion-safe:hidden" />
            {:else}
              <Icon icon="i-mdi-content-save-check-outline" class={cn('size-5', status === 'just-saved' && 'text-foreground')} />
            {/if}
          </span>
        </span>
      {/snippet}
    </Tooltip.Trigger>
    <Tooltip.Content>{tooltip}</Tooltip.Content>
  </Tooltip.Root>
{/if}
