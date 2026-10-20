<script lang="ts">
  import t from '$lib/i18n';
  import Icon from '$lib/icons/Icon.svelte';
  import SetTitle from '$lib/layout/SetTitle.svelte';
  import { page } from '$app/state';
  import { afterNavigate, replaceState } from '$app/navigation';
  import { tick } from 'svelte';

  // FW Lite for Windows logs in with this page as its OAuth redirect and receives the query through its protocol
  // handler (ProtocolLoginRedirect.cs). Without a query (other platforms, reloads) there's nothing to hand on.
  const params = page.url.searchParams;
  const appUrl = params.has('code') || params.has('error') ? `silfwlite://localhost/auth${page.url.search}` : undefined;
  const error = params.get('error');
  const errorDescription = params.get('error_description');
  const titleKey = error
    ? 'fw_lite_signed_in.failed_title'
    : appUrl
      ? 'fw_lite_signed_in.return_title'
      : 'fw_lite_signed_in.title';

  let openButton: HTMLAnchorElement | undefined = $state();
  let lastOpened = 0;

  function openApp(): void {
    // a second launch right after the first reaches an app that's no longer waiting, which shows a warning
    if (!appUrl || Date.now() - lastOpened < 2000) return;
    lastOpened = Date.now();
    location.href = appUrl;
  }

  afterNavigate(() => {
    if (!appUrl) return;
    // the router counts as started (which dev builds check) only after the first afterNavigate
    void tick().then(() => {
      // the code is single-use, but it has no business in the history either
      replaceState(page.url.pathname, {});
      openButton?.focus();
      openApp();
    });
  });
</script>

<SetTitle title={$t(titleKey)} />

<div class="flex flex-col items-center grow">
  <div class="flex flex-col justify-center grow max-w-lg">
    <div class="flex-grow"></div>
    <div class="grid gap-x-3 gap-y-1 items-center" style="grid-template-columns: auto 1fr">
      <div class={error && errorDescription ? 'row-span-3' : 'row-span-2'}>
        {#if error}
          <Icon icon="i-mdi-alert-circle" color="text-error" size="text-5xl" />
        {:else}
          <Icon icon="i-mdi-check-decagram" color="text-success" size="text-5xl" />
        {/if}
      </div>
      <h2 class="text-3xl">{$t(titleKey)}</h2>
      {#if error}
        <div>
          {error === 'access_denied' ? $t('fw_lite_signed_in.access_denied') : $t('fw_lite_signed_in.unknown_error')}
          {$t('fw_lite_signed_in.try_again')}
        </div>
        {#if errorDescription}
          <!-- untranslated server text, so only as a detail -->
          <div class="text-sm opacity-75 break-words">{errorDescription}</div>
        {/if}
      {:else if appUrl}
        <div>{$t('fw_lite_signed_in.browser_prompt')}</div>
      {:else}
        <div>{$t('fw_lite_signed_in.message')}</div>
      {/if}
    </div>
    {#if appUrl}
      <div class="flex flex-col items-center gap-4 mt-6">
        <!-- a link, so it works before or without hydration -->
        <a
          bind:this={openButton}
          href={appUrl}
          class="btn"
          class:btn-success={!error}
          class:btn-outline={!!error}
          onclick={(e) => {
            e.preventDefault();
            openApp();
          }}
        >
          {$t('fw_lite_signed_in.open_app')}
        </a>
        <p class="text-sm opacity-75 text-center">{$t('fw_lite_signed_in.nothing_happened')}</p>
      </div>
    {/if}
    <div class="flex-grow-[2]"></div>
  </div>
</div>
