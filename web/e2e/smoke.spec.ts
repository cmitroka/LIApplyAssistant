import { expect, test } from '@playwright/test';

test('home page is Paste & Parse', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { level: 1, name: 'Paste & Parse' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'JobGrid home' })).toBeVisible();
  await expect(page.getByLabel('Job data extract')).toBeVisible();
  await expect(page.getByText('No parsed jobs yet')).toBeVisible();
});

test('an empty extract is rejected before any API call', async ({ page }) => {
  await page.goto('/');

  await page.getByRole('button', { name: 'Parse extract' }).click();

  await expect(page.getByRole('alert')).toHaveText('Paste a job extract first.');
  await expect(page.getByText('No parsed jobs yet')).toBeVisible();
});

test('sample paste, primary nav, and the /jobs deep link', async ({ page }) => {
  await page.goto('/');

  await page.getByRole('button', { name: 'Load sample data' }).click();
  await expect(page.getByLabel('Job data extract')).toHaveValue(/Senior Product Designer/);
  await expect(page.locator('.char-count')).not.toHaveText(/^0 characters/);

  const primaryNav = page.getByRole('navigation', { name: 'Primary' });
  await primaryNav.getByRole('link', { name: 'Job Grid' }).click();
  await expect(page).toHaveURL(/\/jobs$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Job Grid' })).toBeVisible();

  await page.goto('/jobs');
  await expect(page.getByRole('heading', { level: 1, name: 'Job Grid' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Paste & Parse' }).last()).toBeVisible();

  await primaryNav.getByRole('link', { name: 'Paste & Parse' }).click();
  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Paste & Parse' })).toBeVisible();
});
