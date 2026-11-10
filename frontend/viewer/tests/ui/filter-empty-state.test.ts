import {expect, test} from '@playwright/test';

import {DemoProjectPage} from './demo-project.page';
import {MorphTypeKind} from '$lib/dotnet-types/generated-types/MiniLcm/Models/MorphTypeKind';

test.describe('Empty list with filters on', () => {
  let projectPage: DemoProjectPage;

  test.beforeEach(async ({page}) => {
    projectPage = new DemoProjectPage(page);
    await projectPage.goto();
  });

  test('offers to clear the filters and keeps the search text', async ({page}) => {
    const headword = 'qfilteredout';
    await page.evaluate(async ({hw, morphType}) => {
      const id = crypto.randomUUID();
      await window.__PLAYWRIGHT_UTILS__.demoApi.createEntry({
        id,
        lexemeForm: {seh: hw},
        citationForm: {},
        senses: [{
          id: crypto.randomUUID(),
          entryId: id,
          gloss: {en: 'gloss'},
          definition: {},
          semanticDomains: [],
          exampleSentences: [],
          pictures: [],
        }],
        note: {},
        literalMeaning: {},
        morphType,
        components: [],
        complexForms: [],
        complexFormTypes: [],
        publishIn: [],
        homographNumber: 0,
      }, {includeComplexFormsAndComponents: true, autoAddMainPublication: false});
    }, {hw: headword, morphType: MorphTypeKind.Stem});

    const search = 'qfiltered';
    const {entriesList} = projectPage;
    const entryRow = entriesList.entryRows.filter({hasNotText: 'Add to dictionary'}).filter({hasText: headword});
    const clearFilters = page.getByRole('button', {name: 'Clear filters'});
    const hint = page.getByText(/Filters might be hiding some (entries|words)/);

    await entriesList.filterByText(search);
    await expect(entryRow).toHaveCount(1);
    await expect(clearFilters).toHaveCount(0);

    // the entry has a sense, so this filter hides it
    await page.getByRole('button', {name: 'Toggle filters'}).click();
    await page.getByRole('button', {name: 'Entries missing...'}).click();
    await page.getByRole('option', {name: 'Senses'}).click();
    await page.keyboard.press('Escape');

    await expect(entryRow).toHaveCount(0);
    await expect(page.getByText(/No (entries|words) found/)).toBeVisible();
    await expect(hint).toBeVisible();
    await expect(page.getByText('Add to dictionary')).toBeVisible();

    await clearFilters.click();

    await expect(entryRow).toHaveCount(1);
    await expect(entriesList.searchInput).toHaveValue(search);
    await expect(hint).toHaveCount(0);
    await page.getByRole('button', {name: 'Toggle filters'}).click();
    await expect(page.getByRole('button', {name: 'Entries missing...'})).toBeVisible();
  });

  test('says nothing about filters when only the search finds nothing', async ({page}) => {
    await projectPage.entriesList.filterByText('qnosuchheadword');
    await expect(page.getByText(/No (entries|words) found/)).toBeVisible();
    await expect(page.getByRole('button', {name: 'Clear filters'})).toHaveCount(0);
  });
});
