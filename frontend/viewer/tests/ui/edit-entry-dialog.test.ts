import {expect, test, type Page} from '@playwright/test';
import {DemoProjectPage} from './demo-project.page';

async function openReviewList(page: Page) {
  await new DemoProjectPage(page).goto();
  await page.goto('/testing/project-view/tasks?taskId=entry-no-lexeme-form-seh');
  await page.getByRole('row').first().click();
  const popup = page.getByRole('dialog');
  await popup.locator('input:not([type=submit]):visible').first().fill('qreview');
  await popup.getByRole('button', {name: 'Next'}).click();
  await popup.getByText('Close', {exact: true}).click();
  await expect(popup).toHaveCount(0);
  await page.getByRole('button', {name: 'Done'}).click();
  await page.getByRole('button', {name: 'Review'}).click();
}

async function openEditDialog(page: Page) {
  await page.getByText(/qreview/).click();
  const dialog = page.getByRole('dialog').filter({has: page.getByRole('heading', {name: /Update/})});
  await expect(dialog).toBeVisible();
  const input = dialog.locator('input:not([type=submit]):visible').first();
  return {dialog, input};
}

test('a dismissed edit in the edit dialog is gone when it reopens', async ({page}) => {
  await openReviewList(page);
  let {dialog, input} = await openEditDialog(page);
  await expect(input).toHaveValue('qreview');
  await input.fill('qdiscarded');
  await dialog.getByRole('button', {name: 'Cancel'}).click();
  await expect(dialog).toHaveCount(0);

  ({dialog, input} = await openEditDialog(page));
  await expect(input).toHaveValue('qreview');
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
});

test('an updated edit in the edit dialog is there when it reopens', async ({page}) => {
  await openReviewList(page);
  let {dialog, input} = await openEditDialog(page);
  await input.fill('qreviewupdated');
  await dialog.getByRole('button', {name: /Update/}).click();
  await expect(dialog).toHaveCount(0);

  ({dialog, input} = await openEditDialog(page));
  await expect(input).toHaveValue('qreviewupdated');
});
