import {expect, test} from '@playwright/test';

import {DemoProjectPage} from './demo-project.page';

test.describe('Browse state', () => {
  let projectPage: DemoProjectPage;

  test.beforeEach(async ({page}) => {
    projectPage = new DemoProjectPage(page);
    await projectPage.goto();
  });

  test('search, filter and sort survive a trip to the dashboard', async () => {
    const {page, entriesList} = projectPage;
    await projectPage.api.createEntryWithForms({seh: 'qbstatea'});
    await projectPage.api.createEntryWithForms({seh: 'qbstateb'});
    const {id: withSenseId} = await projectPage.api.createEntryWithForms({seh: 'qbstatec'});
    await page.evaluate(async (entryId) => {
      await window.__PLAYWRIGHT_UTILS__.demoApi.createSense(entryId, {
        id: crypto.randomUUID(),
        entryId,
        gloss: {en: 'gloss'},
        definition: {},
        semanticDomains: [],
        exampleSentences: [],
        pictures: [],
      });
    }, withSenseId);

    const headwords = entriesList.entryRows.filter({hasNotText: 'Add to dictionary'}).locator('h2');
    const sortTrigger = page.getByTestId('sort-menu-trigger');

    await entriesList.filterByText('qbstate');
    await expect(headwords).toHaveCount(3);
    await page.getByRole('button', {name: 'Toggle filters'}).click();
    await page.getByRole('button', {name: 'Entries missing...'}).click();
    await page.getByRole('option', {name: 'Senses'}).click();
    await page.keyboard.press('Escape');
    await expect(headwords).toHaveCount(2);
    await sortTrigger.click();
    // sortOptions order: headword ascending, then descending
    await page.getByRole('menuitem', {name: 'Headword'}).nth(1).click();
    await expect(headwords).toHaveText(['qbstateb', 'qbstatea']);

    await page.getByRole('button', {name: 'Dashboard'}).click();
    await expect(entriesList.searchInput).toHaveCount(0);
    await page.getByRole('button', {name: 'Browse'}).click();

    await expect(entriesList.searchInput).toHaveValue('qbstate');
    await expect(sortTrigger).toHaveText('Headword');
    await expect(sortTrigger.locator('.i-mdi-sort-alphabetical-descending')).toBeVisible();
    await expect(headwords).toHaveText(['qbstateb', 'qbstatea']);
    await page.getByRole('button', {name: 'Toggle filters'}).click();
    await expect(page.getByRole('button', {name: 'Missing: Senses'})).toBeVisible();
    await page.keyboard.press('Escape');

    // Restored text must not come back selected, or the next keystroke would replace it.
    expect(await entriesList.searchInput.evaluate((input: HTMLInputElement) =>
      document.activeElement !== input || input.selectionStart === input.selectionEnd)).toBe(true);
  });

  test('reset clears the restored filters', async () => {
    const {page, entriesList} = projectPage;
    await entriesList.filterByText('qbstate');
    await page.getByRole('button', {name: 'Toggle filters'}).click();
    await page.getByRole('button', {name: 'Entries missing...'}).click();
    await page.getByRole('option', {name: 'Senses'}).click();
    await page.keyboard.press('Escape');

    await page.getByRole('button', {name: 'Dashboard'}).click();
    await page.getByRole('button', {name: 'Browse'}).click();
    await page.getByRole('button', {name: 'Toggle filters'}).click();
    await page.getByRole('button', {name: 'Reset'}).click();

    await expect(page.getByRole('button', {name: 'Entries missing...'})).toBeVisible();
    await expect(entriesList.searchInput).toHaveValue('');
  });
});
