<script lang="ts" context="module">
  import { writable, type Writable } from 'svelte/store';

  export let requestedEmail: Writable<string | null> = writable();
  export let emailResult: Writable<EmailResult | null> = writable();
</script>

<script lang="ts">
  import t from '$lib/i18n';
  import { slide } from 'svelte/transition';
  import type { LexAuthUser } from '$lib/user';
  import { EmailResult } from '.';
  import { Button } from '$lib/forms';
  import { onDestroy } from 'svelte';
  import { Duration } from '$lib/util/time';

  export let user: LexAuthUser;

  let sendingVerificationEmail = false;
  let sentVerificationEmail = false;

  async function sendVerificationEmail(): Promise<void> {
    sendingVerificationEmail = true;
    try {
      const result = await fetch('/api/user/sendVerificationEmail', { method: 'POST' });
      if (!result.ok) throw Error(`Failed to send verification email. ${result.status}: ${result.statusText}.`);
      sentVerificationEmail = true;
    } finally {
      sendingVerificationEmail = false;
    }
  }

  const emailResultUnsubscriber = emailResult.subscribe((result) => {
    if (result) {
      setTimeout(() => emailResult.set(null), Duration.Medium);
    }
  });
  const requestedEmailUnsubscriber = requestedEmail.subscribe((result) => {
    if (result) {
      setTimeout(() => requestedEmail.set(null), Duration.Long);
    }
  });

  onDestroy(() => {
    emailResultUnsubscriber();
    requestedEmailUnsubscriber();
  });
</script>

{#if $requestedEmail}
  <div class="alert alert-info" transition:slide|local>
    <div>
      <div>{$t('account_settings.verify_email.you_have_mail')}</div>
      <span>{$t('account_settings.verify_email.verify_to_change', { $requestedEmail })}</span>
    </div>
    <span class="i-mdi-email-heart-outline" />
  </div>
{:else if $emailResult}
  <div class="alert alert-success" transition:slide|local>
    {#if $emailResult === EmailResult.VerifiedEmail}
      <span>{$t('account_settings.verify_email.verify_success')}</span>
    {:else}
      <span>{$t('account_settings.verify_email.change_success')}</span>
    {/if}
    <span class="i-mdi-check-circle-outline" />
    <a class="btn" href="/">{$t('account_settings.verify_email.go_to_projects')}</a>
  </div>
{:else if !user?.emailVerified}
  {#if sentVerificationEmail}
    <div class="alert alert-info" transition:slide|local>
      <div>
        <div>{$t('account_settings.verify_email.you_have_mail')}</div>
        <span>{$t('account_settings.verify_email.check_inbox')}</span>
      </div>
      <span class="i-mdi-email-heart-outline" />
    </div>
  {:else}
    <div class="alert alert-warning" transition:slide|local>
      <span>{$t('account_settings.verify_email.please_verify')}</span>
      <Button loading={sendingVerificationEmail} on:click={sendVerificationEmail}>
        {$t('account_settings.verify_email.resend')}
      </Button>
    </div>
  {/if}
{/if}

<style lang="postcss">
  .alert {
    @apply text-base;

    & > span[class*='i-mdi'] {
      flex: 0 0 40px;
      @apply text-5xl;
    }

    & > div {
      @apply flex-col items-start;
      & > div:first-child {
        @apply font-bold;
      }
    }
  }
</style>
