import {expect, test, type Page} from '@playwright/test';
import {DemoProjectPage} from './demo-project.page';

// The layer's history entry is pushed asynchronously after it opens; a real back press can't beat it.
async function pressBack(page: Page) {
  await page.waitForFunction(() => (history.state as {backHandler?: boolean} | null)?.backHandler === true);
  await page.goBack();
}

// On a phone the entry view lives in the URL, so a back press that no layer handles closes the entry.
test.describe('Back closes the top layer (mobile)', () => {
  let projectPage: DemoProjectPage;

  test.beforeEach(async ({page}) => {
    await page.setViewportSize({width: 390, height: 844});
    projectPage = new DemoProjectPage(page);
    await projectPage.goto();
    await projectPage.selectEntryByFilter('ambuka');
  });

  test('back closes the entry picker, then the new entry dialog on top of it, not the entry', async ({page}) => {
    const lexemeInput = await projectPage.entryView.getLexemeInput();
    await page.locator('[data-field-id="complexForms"]').getByRole('button', {name: /Complex Form|Part of/}).click();
    const picker = page.getByRole('dialog').filter({has: page.getByRole('heading', {name: /Add (complex form|part of)/})});
    await expect(picker).toBeVisible();

    await picker.getByPlaceholder(/Find (entry|word)/).fill('qbackpicker');
    await expect(picker.getByText(/No (entries|words) found/)).toBeVisible();
    await picker.locator('button:has(.i-mdi-book-plus-outline)').click();
    const newEntryDialog = page.getByRole('dialog').filter({has: page.getByRole('heading', {name: /New (Entry|Word)/})});
    await expect(newEntryDialog).toBeVisible();

    await pressBack(page);
    await expect(newEntryDialog).toHaveCount(0);
    await expect(picker).toBeVisible();

    await pressBack(page);
    await expect(picker).toHaveCount(0);
    await expect(lexemeInput).toBeVisible();
    await expect(page).toHaveURL(/entryOpen=true/);
  });

  test('back with a pending semantic domain pick saves it', async ({page}) => {
    type Entry = {id: string, senses: {semanticDomains: {code: string}[]}[]};
    const entryId = new URL(page.url()).searchParams.get('entryId')!;
    const codesBefore = ((await projectPage.api.getEntry(entryId)) as Entry).senses[0].semanticDomains.map(d => d.code);
    const field = page.locator('[data-field-id="semanticDomains"]').first();
    await field.getByRole('combobox').click();
    const options = page.getByRole('dialog').getByRole('option');
    await expect(options.first()).toBeVisible();

    const labels = await options.evaluateAll(els => els.map(el => el.getAttribute('aria-label') ?? ''));
    const label = labels.find(l => !codesBefore.includes(l.split(' ')[0]))!;
    const code = label.split(' ')[0];
    // On mobile a tap only marks the pick as pending until the drawer is submitted
    await page.getByRole('option', {name: label, exact: true}).click();
    await expect(options.first()).toBeVisible();
    expect(((await projectPage.api.getEntry(entryId)) as Entry).senses[0].semanticDomains.map(d => d.code)).not.toContain(code);

    await pressBack(page);
    await expect(options).toHaveCount(0);
    await expect(page).toHaveURL(/entryOpen=true/);
    await expect.poll(async () => ((await projectPage.api.getEntry(entryId)) as Entry).senses[0].semanticDomains.map(d => d.code))
      .toContain(code);
  });
});

test('back closes the edit dialog in a task review and keeps the review list', async ({page}) => {
  await page.setViewportSize({width: 390, height: 844});
  await new DemoProjectPage(page).goto();
  await page.goto('/testing/project-view/tasks?taskId=entry-no-lexeme-form-seh');
  await page.getByText('ambuka').first().click();
  const popup = page.getByRole('dialog');
  await popup.locator('input:not([type=submit]):visible').first().fill('qbackreview');
  await popup.getByRole('button', {name: 'Next'}).click();
  await popup.getByText('Close', {exact: true}).click();
  await expect(popup).toHaveCount(0);
  await page.getByRole('button', {name: 'Done'}).click();
  await expect(page.getByText(/You completed 1/)).toBeVisible();
  await page.getByRole('button', {name: 'Review'}).click();

  await page.getByText(/qbackreview/).click();
  const editDialog = page.getByRole('dialog').filter({has: page.getByRole('heading', {name: /Update/})});
  await expect(editDialog).toBeVisible();
  await pressBack(page);
  await expect(editDialog).toHaveCount(0);
  await expect(page.getByRole('button', {name: 'Finish'})).toBeVisible();
});
