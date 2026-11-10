import {expect, test} from '@playwright/test';
import {DemoProjectPage} from './demo-project.page';

test.describe('Delete dialog names what is being deleted', () => {
  const longSentence = 'Ndimbalemba tsamba yakulapha kuna mwanache wanga wakukhala ku dziko yakutali kwene kwene, mbandiyembekezera.';

  test('shows the definition, the sentence, or a number', async ({page}) => {
    const projectPage = new DemoProjectPage(page);
    await projectPage.goto();
    await projectPage.api.createEntryWithSenses('qdelnames', [
      {definition: 'a small round fruit', examples: [longSentence]},
      {examples: ['', {ws: 'seh-Zxxx-x-audio', text: 'recording.wav'}]},
    ]);
    await projectPage.selectEntryByFilter('qdelnames');

    const deleteButtons = page.locator('.i-mdi-trash-can');
    await expect(deleteButtons).toHaveCount(5);
    const dialog = page.getByRole('alertdialog');

    async function expectPrompt(buttonIndex: number, text: string | RegExp) {
      await deleteButtons.nth(buttonIndex).click();
      await expect(dialog.getByText(/Are you sure you want to delete/)).toHaveText(text);
      await dialog.getByRole('button', {name: "Don't delete"}).click();
      await expect(dialog).toBeHidden();
    }

    await expectPrompt(0, /^Are you sure you want to delete (Sense|Meaning): a small round fruit\?$/);
    // Long sentences are cut short
    await expectPrompt(1, /^Are you sure you want to delete Example sentence: Ndimbalemba tsamba yakulapha [^…]+…\?$/);
    await expectPrompt(2, /^Are you sure you want to delete (Sense|Meaning) 2\?$/);
    await expectPrompt(3, 'Are you sure you want to delete Example 1?');
    // An audio file reference is not a name
    await expectPrompt(4, 'Are you sure you want to delete Example 2?');
  });
});
