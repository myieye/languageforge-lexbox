<script lang="ts">
  import { page } from '$app/stores';
  import UnexpectedError from '$lib/error/UnexpectedError.svelte';
  import t from '$lib/i18n';
  import { Layout, Page } from '$lib/layout';
</script>

<Layout>
  <Page>
    {#if $page.status === 404}
      <div class="flex flex-col gap-4 items-center">
        <div class="flex gap-2">
          <span class="i-mdi-emoticon-confused-outline text-3xl" />
          <span class="text-2xl">{$t('errors.not_found')}</span>
        </div>
      </div>
    {:else}
      <UnexpectedError />
    {/if}
    <div class="mt-8 text-center">
      <a class="btn btn-success" href="/">{$t('errors.go_home')} <span class="i-mdi-home-outline text-xl"></span></a>
    </div>
  </Page>
</Layout>
