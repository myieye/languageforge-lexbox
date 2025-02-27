<script lang="ts" context="module">
  export type Filter<T = Record<string, unknown>> = Readonly<
    {
      [K in keyof T]: { value: T[K]; key: K & string, clear: () => void };
    }[keyof T]
  >;
</script>

<script lang="ts">
  import t from '$lib/i18n';
  import { derived, type Readable, type Writable } from 'svelte/store';
  import Dropdown from '../Dropdown.svelte';

  type Filters = $$Generic<Record<string, unknown>>;

  export let search = '';
  export let autofocus: true | undefined = undefined;
  export let filters: Writable<Filters>;
  export let defaultValues: Filters;
  export let hasActiveFilter: boolean;
  const activeFilters = pickActiveFilters(filters, defaultValues);
  $: {
    hasActiveFilter = $activeFilters.length > 0;
  }

  function reseFilters(): void {
    $filters = defaultValues;
  }

  function resetFilter(key: string): void {
    $filters = {
      ...$filters,
      [key]: defaultValues[key],
    };
  }

  function pickActiveFilters(filterStore: Readable<Filters>, defaultValues: Filters): Readable<Filter<Filters>[]> {
    return derived(filterStore, (values) => {
      const filters: Filter<Filters>[] = [];
      for (const key in values) {
        const value = values[key];
        if (value !== defaultValues[key]) {
          filters.push({ key, value, clear: () => resetFilter(key) } as Filter<Filters>);
        }
      }
      return filters;
    });
  }
</script>

<div class="input filter-bar input-bordered flex items-center gap-2 py-1.5 px-2 mt-4 flex-wrap h-[unset] min-h-12">
  <slot name="activeFilters" activeFilters={$activeFilters} />
  <div class="flex grow">
    <!-- svelte-ignore a11y-autofocus -->
    <input
      bind:value={search}
      placeholder={$t('admin_dashboard.filter_placeholder')}
      class="seach-input input border-none h-8 px-1 focus:outline-none min-w-[120px] flex-grow"
      {autofocus}
    />
    <div class="ml-auto flex join">
      {#if hasActiveFilter}
        <button class="btn btn-square btn-sm join-item" on:click={reseFilters}>
          <span class="text-lg">✕</span>
        </button>
      {/if}
      <div class="join-item">
        <Dropdown>
          <!-- svelte-ignore a11y-label-has-associated-control -->
          <label tabindex="-1" class="btn btn-square btn-sm gap-2">
            <span class="i-mdi-filter-outline text-xl" />
          </label>
          <div slot="content" class="card w-[calc(100vw-1rem)] sm:max-w-[35rem]">
            <div class="card-body max-sm:p-4">
              <slot name="filters" />
            </div>
          </div>
        </Dropdown>
      </div>
    </div>
  </div>
</div>

<style lang="postcss">
  .filter-bar:has(.seach-input:focus) {
    /* copied from .input:focus */
    outline-style: solid;
    outline-width: 2px;
    outline-offset: 2px;
    outline-color: hsl(var(--bc) / 0.2);
  }
</style>
