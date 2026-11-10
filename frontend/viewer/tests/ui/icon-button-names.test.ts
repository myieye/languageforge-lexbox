import {expect, test} from '@playwright/test';

import {DemoProjectPage} from './demo-project.page';
import {MorphTypeKind} from '$lib/dotnet-types/generated-types/MiniLcm/Models/MorphTypeKind';

test('icon-only entry editor buttons have accessible names', async ({page}) => {
  const projectPage = new DemoProjectPage(page);
  await projectPage.goto();
  const headword = 'qiconnames';
  await page.evaluate(async ({hw, morphType}) => {
    const api = window.__PLAYWRIGHT_UTILS__.demoApi;
    const entryId = crypto.randomUUID();
    function sense(gloss: string) {
      const id = crypto.randomUUID();
      return {
        id, entryId, gloss: {en: gloss}, definition: {}, semanticDomains: [], pictures: [],
        exampleSentences: [{id: crypto.randomUUID(), senseId: id, sentence: {seh: {spans: [{text: gloss + ' example', ws: 'seh'}]}}, translations: [], reference: {spans: []}}],
      };
    }
    await api.createEntry({
      id: entryId,
      lexemeForm: {seh: hw, 'seh-Zxxx-x-audio': 'sil-media://staging.languagedepot.org/99cfbab9-74c7-478c-bf68-636e62c6b72f'},
      citationForm: {},
      senses: [sense('first'), sense('second')],
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

  await projectPage.selectEntryByFilter(headword);

  for (const name of ['Delete Meaning 1', 'Delete Meaning 2', 'Move Meaning 2']) {
    await expect(page.getByRole('button', {name, exact: true})).toBeVisible();
  }
  await expect(page.getByRole('button', {name: 'Delete Example 1', exact: true})).toHaveCount(2);
  await expect(page.getByRole('button', {name: 'View options', exact: true})).toHaveCount(2);
  await expect(page.getByRole('button', {name: 'More options', exact: true})).toBeVisible();
  await expect(page.getByRole('button', {name: 'Play audio (Word: seh)', exact: true})).toBeVisible();
  await expect(page.getByRole('button', {name: 'Audio options (Word: seh)', exact: true})).toBeVisible();
});
