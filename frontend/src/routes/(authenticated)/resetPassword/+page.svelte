<script lang="ts">
  import { goto } from '$app/navigation';
  import { SubmitButton, Form, FormError, Input, lexSuperForm } from '$lib/forms';
  import t from '$lib/i18n';
  import Page from '$lib/layout/Page.svelte';
  import { hash } from '$lib/util/hash';
  import { z } from 'zod';
  import { notifySuccess } from '$lib/notify';
  import type { PageData } from './$types';
  import { passwordFormRules } from '$lib/forms/utils';

  export let data: PageData;

  const formSchema = z.object({
    password: passwordFormRules($t),
  });
  let { form, errors, enhance, submitting, message } = lexSuperForm(formSchema, async () => {
    const response = await fetch('api/login/resetPassword', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ passwordHash: await hash($form.password) }),
    });
    if (!response.ok) {
      return response.statusText;
    }
    notifySuccess($t('login.password_reset'));
    await goto(data.home);
  });
</script>

<Page>
  <svelte:fragment slot="header">
    {$t('reset_password.title')}
  </svelte:fragment>
  <Form {enhance}>
    <Input
      bind:value={$form.password}
      type="password"
      label={$t('reset_password.new_password')}
      error={$errors.password}
      autofocus
    />
    <FormError error={$message} />
    <SubmitButton loading={$submitting}>{$t('reset_password.submit')}</SubmitButton>
  </Form>
</Page>
