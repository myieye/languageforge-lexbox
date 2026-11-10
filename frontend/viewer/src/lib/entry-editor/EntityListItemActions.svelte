<script lang="ts" generics="T">
  import HistoryView from '../history/HistoryView.svelte';
  import {useFeatures} from '$lib/services/feature-service';
  import { Reorderer } from '$lib/components/reorderer';
  import {Button} from '$lib/components/ui/button';
  import {t} from 'svelte-i18n-lingui';

  type Props<T> = {
    items: T[];
    i: number;
    id?: string;
    subject: string;
    getDisplayName: (item: T) => string | undefined;
    readonly: boolean;
    onmove?: (newIndex: number) => void;
    ondelete?: () => void;
  };

  let {
    items = $bindable(),
    i,
    id,
    subject,
    getDisplayName,
    readonly,
    onmove,
    ondelete,
  }: Props<T> = $props();

  const features = useFeatures();

  let showHistoryView = $state(false);
  const item = $derived(items[i]);
</script>

{#if !readonly || features.history}
<div class="flex gap-2">
  {#if !readonly}
    <Reorderer
      direction="vertical"
      {item}
      {items}
      {getDisplayName}
      label={$t`Move ${subject}`}
      onchange={(_newItems, _fromIndex, newIndex) => onmove?.(newIndex)}
    />
  {/if}
  {#if features.history && id}
    <Button onclick={() => showHistoryView = true} size="icon" variant="secondary" icon="i-mdi-history" title={$t`History of ${subject}`} />
    <HistoryView bind:open={showHistoryView} {id}/>
  {/if}
  {#if !readonly}
    <Button onclick={ondelete} size="icon" variant="secondary" icon="i-mdi-trash-can" title={$t`Delete ${subject}`} />
  {/if}
</div>
{/if}
