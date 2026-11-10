<script lang="ts" module>
  import {SortField} from '$lib/dotnet-types';
  import {msg} from 'svelte-i18n-lingui';
  import type {IconClass} from '$lib/icon-class';
  import type {SortDirection} from './options';

  const sortLabels = {
    [SortField.SearchRelevance]: msg`Best match`,
    [SortField.Headword]: msg`Headword`
  } as const;

  const sortIcons: Partial<Record<SortField, Record<SortDirection, IconClass>>> = {
    [SortField.Headword]: {
      asc: 'i-mdi-sort-alphabetical-ascending',
      desc: 'i-mdi-sort-alphabetical-descending'
    }
  };
</script>

<script lang="ts">
  import {t} from 'svelte-i18n-lingui';
  import {badgeVariants} from '$lib/components/ui/badge';
  import * as ResponsiveMenu from '$lib/components/responsive-menu';
  import {cn} from '$lib/utils';
  import {watch, type Getter} from 'runed';
  import {Icon} from '$lib/components/ui/icon';
  import {sortOptions, type SortChoice, type SortConfig} from './options';
  import {Button, buttonVariants} from '$lib/components/ui/button';

  type Props = {
    value?: SortConfig;
    /** Undefined = auto */
    choice?: SortChoice;
    autoSelector: Getter<SortField>;
  };

  let {
    value = $bindable(),
    choice = $bindable(),
    autoSelector,
  }: Props = $props();

  const autoSort = $derived(autoSelector());
  const sortField = $derived(choice?.field ?? autoSort);
  const direction = $derived<SortDirection>(choice?.dir ?? 'asc');
  watch(() => ({ sortField, direction }), ({ sortField, direction }) => {
    value = { field: sortField, dir: direction };
  });
</script>

<ResponsiveMenu.Root>
  <ResponsiveMenu.Trigger class={cn(buttonVariants({variant: 'secondary', size: 'xs'}), badgeVariants({ variant: 'secondary' }), 'border-none h-7')}>
    {#snippet child({props})}
      <Button {...props}
        data-testid="sort-menu-trigger"
        icon={sortIcons[sortField]?.[direction] ?? 'i-mdi-arrow-down'}
        iconProps={{ class: 'size-4' }}>
        {$t(sortLabels[sortField])}
      </Button>
    {/snippet}
  </ResponsiveMenu.Trigger>
  <ResponsiveMenu.Content align="start">
    <ResponsiveMenu.Item
        onSelect={() => choice = undefined}
        class={cn(!choice && 'bg-muted')}
        >
        {$t`Auto`}
        <span class="text-muted-foreground ml-auto">
          ({$t(sortLabels[autoSort])})
        </span>
    </ResponsiveMenu.Item>
    {#each sortOptions as option (option)}
      {@const icon = sortIcons[option.field]?.[option.dir]}
      <ResponsiveMenu.Item
        onSelect={() => choice = option}
        class={cn(choice?.field === option.field && choice.dir === option.dir && 'bg-muted')}
        >
        {#if icon}
          <Icon {icon} />
        {/if}
        {$t(sortLabels[option.field])}
      </ResponsiveMenu.Item>
    {/each}
  </ResponsiveMenu.Content>
</ResponsiveMenu.Root>
