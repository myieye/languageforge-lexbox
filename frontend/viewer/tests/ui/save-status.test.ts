import {type Page, expect, test} from '@playwright/test';

import {DemoProjectPage} from './demo-project.page';

type UpdateEntryMode = {delayMs: number, fail?: boolean};

async function setUpdateEntryMode(page: Page, mode: UpdateEntryMode) {
  await page.evaluate((mode) => {
    const api = window.__PLAYWRIGHT_UTILS__.demoApi as typeof window.__PLAYWRIGHT_UTILS__.demoApi & {originalUpdateEntry?: typeof api.updateEntry};
    api.originalUpdateEntry ??= api.updateEntry.bind(api);
    const original = api.originalUpdateEntry;
    api.updateEntry = async (before, after) => {
      await new Promise(resolve => setTimeout(resolve, mode.delayMs));
      if (mode.fail) throw new Error('Simulated save failure');
      return original(before, after);
    };
  }, mode);
}

async function editLexemeForm(projectPage: DemoProjectPage, suffix: string) {
  const input = await projectPage.entryView.getLexemeInput();
  await input.click();
  await input.press('End');
  await input.pressSequentially(suffix);
  await input.press('Tab');
}

test.describe('Save status', () => {
  let projectPage: DemoProjectPage;

  test.beforeEach(async ({page}) => {
    projectPage = new DemoProjectPage(page);
    await projectPage.goto();
    const {headword} = await projectPage.api.getEntryAtIndex(10);
    await projectPage.selectEntryByFilter(headword);
  });

  test('shows Saved after a fast save, without flashing Saving', async ({page}) => {
    const saveStatus = page.locator('[data-save-status]');
    await expect(saveStatus).toHaveCount(0);

    await editLexemeForm(projectPage, 'x');

    await expect(saveStatus).toHaveAttribute('data-save-status', 'just-saved');
    await expect(saveStatus).toHaveText('Saved');
    await expect(page.getByRole('status').filter({hasText: /^Saved$/})).toHaveCount(1);
    // After a moment the label fades but the icon stays, so the answer is always on screen.
    await expect(saveStatus).toHaveAttribute('data-save-status', 'resting', {timeout: 4000});
    await expect(saveStatus).toHaveText('');
    await expect(page.locator('[data-save-status="saving"]')).toHaveCount(0);
  });

  test('hides the saved state while a typed change is not committed yet', async ({page}) => {
    const saveStatus = page.locator('[data-save-status]');
    await editLexemeForm(projectPage, 'x');
    await expect(saveStatus).toHaveAttribute('data-save-status', 'resting', {timeout: 4000});

    const input = await projectPage.entryView.getLexemeInput();
    await input.click();
    await input.press('End');
    await input.pressSequentially('y');
    await expect(saveStatus).toHaveCount(0);

    await input.press('Tab');
    await expect(saveStatus).toHaveAttribute('data-save-status', 'just-saved');
  });

  test('does not carry the status over to another entry', async ({page}) => {
    const saveStatus = page.locator('[data-save-status]');
    await editLexemeForm(projectPage, 'x');
    await expect(saveStatus).toHaveAttribute('data-save-status', 'just-saved');

    const {headword} = await projectPage.api.getEntryAtIndex(11);
    await projectPage.selectEntryByFilter(headword);
    await expect(saveStatus).toHaveCount(0);
  });

  test('shows Saving during a slow save', async ({page}) => {
    await setUpdateEntryMode(page, {delayMs: 2500});
    const saveStatus = page.locator('[data-save-status]');

    await editLexemeForm(projectPage, 'x');

    await expect(saveStatus).toHaveAttribute('data-save-status', 'saving');
    await expect(saveStatus).toHaveText('Saving...');
    await expect(saveStatus).toHaveAttribute('data-save-status', 'just-saved', {timeout: 5000});
    await expect(saveStatus).toHaveText('Saved');
  });

  test('keeps Not saved visible until the next successful save', async ({page}) => {
    await setUpdateEntryMode(page, {delayMs: 0, fail: true});
    const saveStatus = page.locator('[data-save-status]');

    await editLexemeForm(projectPage, 'x');

    await expect(saveStatus).toHaveAttribute('data-save-status', 'failed');
    await expect(saveStatus).toHaveText('Not saved');
    await expect(page.getByRole('status').filter({hasText: /^Not saved$/})).toHaveCount(1);
    await page.waitForTimeout(3000);
    await expect(saveStatus).toHaveAttribute('data-save-status', 'failed');

    // The retry is slow enough that Saving would normally show; the failure stays put instead.
    await setUpdateEntryMode(page, {delayMs: 1500});
    await editLexemeForm(projectPage, 'y');
    await page.waitForTimeout(1200);
    await expect(saveStatus).toHaveAttribute('data-save-status', 'failed');
    await expect(saveStatus).toHaveAttribute('data-save-status', 'just-saved', {timeout: 5000});
  });
});
