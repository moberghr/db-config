import { test } from '@playwright/test';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const SCREENSHOTS_DIR = path.join(__dirname, '../../website/static/img/screenshots');

/**
 * Seed localStorage with the Zustand persist envelope for the theme store.
 * Zustand `persist` writes: { state: { theme: 'light'|'dark' }, version: 0 }
 */
async function seedTheme(page: import('@playwright/test').Page, theme: 'light' | 'dark') {
  await page.addInitScript((t) => {
    localStorage.setItem(
      'db-config-theme',
      JSON.stringify({ state: { theme: t }, version: 0 }),
    );
  }, theme);
}

/**
 * Seed localStorage with a Zustand persist envelope for the scope store.
 * Zustand merges persisted state with defaults, so we only need to specify
 * the fields we want to override.
 */
async function seedScopeStore(
  page: import('@playwright/test').Page,
  overrides: Record<string, unknown>,
) {
  await page.addInitScript((o) => {
    localStorage.setItem(
      'db-config-scope',
      JSON.stringify({ state: o, version: 0 }),
    );
  }, overrides);
}

/**
 * Helper: navigate to the demo page, wait for the entries table to render,
 * and disable animations for determinism.
 *
 * The custom Dialog in this app is a plain <div> overlay (no role="dialog").
 * We locate dialog content by heading text instead.
 */
async function gotoDemo(page: import('@playwright/test').Page) {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/?demo');
  await page.waitForLoadState('networkidle');
  // Wait for the table to appear — confirms demo data loaded
  await page.locator('table').waitFor({ timeout: 15000 });
}

/**
 * Wait for a dialog to be visible by looking for its <h2> heading text.
 * The custom Dialog renders a fixed overlay with a DialogContent <div>
 * containing a <h2> (DialogTitle).
 */
async function waitForDialogTitle(page: import('@playwright/test').Page, titlePattern: string | RegExp) {
  await page.locator('h2').filter({ hasText: titlePattern }).waitFor({ timeout: 10000 });
}

// ─────────────────────────────────────────────────────────────────────────────
// 01 — entries-list: full page, all 3 scopes, scope badges, masked secrets,
//       AccessWarningBanner at top
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`01-entries-list${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    await gotoDemo(page);
    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/01-entries-list${suffix}.png`,
      fullPage: true,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 02 — edit-value: EditValueDialog open on Stripe:ApiKey row
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`02-edit-value${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    await gotoDemo(page);

    // Click the Edit (Pencil) button on the Stripe:ApiKey row.
    // The row shows only entry.key in the Key column ("Stripe:ApiKey").
    const stripeRow = page.getByRole('row').filter({ hasText: 'Stripe:ApiKey' }).first();
    const editBtn = stripeRow.getByTitle('Edit');
    await editBtn.click();

    // Wait for the EditValueDialog — its title is an <h2> with "Edit: Stripe:ApiKey"
    await waitForDialogTitle(page, /Edit:.*Stripe:ApiKey/);

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/02-edit-value${suffix}.png`,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 03 — create-entry: CreateEntryDialog open with scope dropdown visible
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`03-create-entry${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    await gotoDemo(page);

    // Click the "New Entry" button
    await page.getByRole('button', { name: /new entry/i }).click();

    // Wait for the dialog heading
    await waitForDialogTitle(page, 'New Entry');

    // The scope dropdown is a <select>; focus it so it appears active
    const scopeSelect = page.locator('#create-scope');
    await scopeSelect.focus();

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/03-create-entry${suffix}.png`,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 04 — secret-revealed: eye icon clicked on EmailNotifications:SmtpPassword row
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`04-secret-revealed${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    await gotoDemo(page);

    // Find the SmtpPassword row — key column shows "EmailNotifications:SmtpPassword"
    const smtpRow = page.getByRole('row').filter({ hasText: 'EmailNotifications:SmtpPassword' }).first();

    // Click the eye/reveal button — title is "Reveal value" before clicking
    const revealBtn = smtpRow.getByTitle('Reveal value');
    await revealBtn.click();

    // Wait for the value to be revealed (the button title changes to "Hide value")
    await smtpRow.getByTitle('Hide value').waitFor();

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/04-secret-revealed${suffix}.png`,
      fullPage: true,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 05 — history-dialog: EntryHistoryDialog open for Stripe:ApiKey showing 3 audit rows
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`05-history-dialog${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    await gotoDemo(page);

    // Click the History (clock) button on the Stripe:ApiKey row
    const stripeRow = page.getByRole('row').filter({ hasText: 'Stripe:ApiKey' }).first();
    const historyBtn = stripeRow.getByTitle('History');
    await historyBtn.click();

    // Wait for the history dialog heading: "History — Stripe:ApiKey"
    await waitForDialogTitle(page, /History.*Stripe:ApiKey/);

    // Wait for audit rows to be visible — look for "Insert" action chip
    await page.locator('span').filter({ hasText: 'Insert' }).first().waitFor({ timeout: 8000 });

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/05-history-dialog${suffix}.png`,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 06 — history-diff: same dialog, Compare button on an Update row expanded
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`06-history-diff${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    await gotoDemo(page);

    // Open history for Stripe:ApiKey
    const stripeRow = page.getByRole('row').filter({ hasText: 'Stripe:ApiKey' }).first();
    await stripeRow.getByTitle('History').click();

    // Wait for the history dialog to open
    await waitForDialogTitle(page, /History.*Stripe:ApiKey/);

    // Wait for all history rows to render
    await page.locator('span').filter({ hasText: 'Insert' }).first().waitFor({ timeout: 8000 });

    // Find a row that contains the "Update" action chip and click its Compare button.
    // History rows are <tr> elements; each has an Update chip and a Compare button.
    // We look for a table row that contains both "Update" and "Compare"
    const compareBtn = page.getByRole('row').filter({ hasText: 'Update' }).first()
      .getByRole('button', { name: /compare/i });
    await compareBtn.click();

    // Wait for the diff panel — "Character-level diff" text appears in the expanded row
    await page.getByText('Character-level diff').waitFor({ timeout: 8000 });

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/06-history-diff${suffix}.png`,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 07 — bulk-edit-toolbar: 3 rows selected, BulkActionsToolbar visible
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`07-bulk-edit-toolbar${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    await gotoDemo(page);

    // Select 3 entries using their checkboxes (aria-label "Select {key}")
    await page.getByRole('checkbox', { name: 'Select Stripe:ApiKey' }).click();
    await page.getByRole('checkbox', { name: 'Select Stripe:WebhookSecret' }).click();
    await page.getByRole('checkbox', { name: 'Select ConnectionStrings:Default' }).click();

    // Wait for the BulkActionsToolbar — it shows "N selected"
    await page.getByText('3 selected').waitFor();

    // Also confirm the action buttons are visible
    await page.getByRole('button', { name: /toggle issecret/i }).waitFor();

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/07-bulk-edit-toolbar${suffix}.png`,
      fullPage: true,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 08 — import-dialog: ImportDialog in preview phase with entries
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`08-import-dialog${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    await gotoDemo(page);

    // Click the Import button in the toolbar
    await page.getByRole('button', { name: /^import$/i }).click();

    // Wait for the import dialog heading
    await waitForDialogTitle(page, /Import entries/);

    // Upload a JSON file to advance to preview phase
    const fixturePath = path.join(__dirname, 'fixtures', 'import-sample.json');
    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(fixturePath);

    // Wait for preview phase — "Review the X entries to be imported" text
    await page.getByText(/Review the .* entries to be imported/).waitFor({ timeout: 8000 });

    // Also wait for the collision policy section
    await page.getByText(/Collision policy/).waitFor();

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/08-import-dialog${suffix}.png`,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 09 — scope-selector: capture just the ScopeSelector region
//      App=PaymentService, IncludeScopes=Shared, PlatformDefaults
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`09-scope-selector${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    await gotoDemo(page);

    // ScopeSelector renders inside a flex row containing the #scope-app input.
    // Grab the closest ancestor div that contains that input.
    const scopeArea = page.locator('div').filter({
      has: page.locator('#scope-app'),
    }).first();

    await scopeArea.waitFor();

    await scopeArea.screenshot({
      path: `${SCREENSHOTS_DIR}/09-scope-selector${suffix}.png`,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 10 — access-warning: full-page hero shot with AccessWarningBanner prominent
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`10-access-warning${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    await gotoDemo(page);

    // Scroll to top to ensure the banner is at the top of the viewport
    await page.evaluate(() => window.scrollTo(0, 0));

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/10-access-warning${suffix}.png`,
      fullPage: true,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 11 — tree-view: EntriesTreeView in tree mode with all groups expanded,
//      showing Stripe → Payment + Auth subgroups
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`11-tree-view${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    // Seed the scope store to boot in tree mode
    await seedScopeStore(page, { listMode: 'tree' });
    await gotoDemo(page);

    // The tree view has "Expand all" / "Collapse all" controls.
    // Click "Expand all" to deterministically show all groups.
    await page.getByRole('button', { name: /expand all/i }).click();

    // Wait for the Stripe group's children to be visible —
    // "Payment" subgroup should appear as a group row under Stripe
    await page.getByRole('row').filter({ hasText: 'Payment' }).first().waitFor({ timeout: 8000 });

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/11-tree-view${suffix}.png`,
      fullPage: true,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 12 — tenant-selector: ScopeSelector with Tenant input filled with "Acme"
//      Seeds db-config-scope with tenantId: "Acme" before navigating so the
//      field already shows the tenant selection on load.
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`12-tenant-selector${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    // Seed scope store with tenantId = "Acme"
    await seedScopeStore(page, { tenantId: 'Acme' });
    await gotoDemo(page);

    // Locate the ScopeSelector area (the div containing #scope-app)
    const scopeArea = page.locator('div').filter({
      has: page.locator('#scope-app'),
    }).first();

    await scopeArea.waitFor();

    // Wait for the tenant input to show "Acme" (it should be seeded from localStorage)
    await page.locator('#scope-tenant').waitFor({ timeout: 8000 });

    await scopeArea.screenshot({
      path: `${SCREENSHOTS_DIR}/12-tenant-selector${suffix}.png`,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 13 — tenant-entries-view: EntriesTable showing rows with Default + Acme
//      tenant badges visible. Seeds tenantId="" so the all-tenants list is
//      shown — the table renders both the "" global default entries and
//      the Acme-tenanted rows with their respective badges.
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`13-tenant-entries-view${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    // Empty tenantId shows all entries (global defaults) — the demo adapter
    // returns all demo entries including those with tenantId: 'Acme'
    await seedScopeStore(page, { tenantId: '' });
    await gotoDemo(page);

    // Wait for at least one "Default" tenant badge in the table
    await page.locator('table').waitFor({ timeout: 15000 });

    // The Tenant column renders a "Default" badge for global entries and
    // a colored badge for tenant-specific entries — wait for both to appear
    await page.getByText('Default').first().waitFor({ timeout: 8000 });

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/13-tenant-entries-view${suffix}.png`,
      fullPage: true,
    });
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// 14 — create-with-tenant-dialog: CreateEntryDialog open with TenantId field
//      visible and pre-filled with the current scope's tenantId ("Acme")
// ─────────────────────────────────────────────────────────────────────────────
for (const theme of ['light', 'dark'] as const) {
  const suffix = theme === 'dark' ? '-dark' : '';
  test(`14-create-with-tenant-dialog${suffix}`, async ({ page }) => {
    await seedTheme(page, theme);
    // Seed with tenantId = "Acme" so the dialog pre-fills it
    await seedScopeStore(page, { tenantId: 'Acme' });
    await gotoDemo(page);

    // Click the "New Entry" (or "+ New entry") button
    await page.getByRole('button', { name: /new entry/i }).click();

    // Wait for the dialog heading
    await waitForDialogTitle(page, 'New Entry');

    // Wait for the TenantId input to appear (it should show "Acme")
    await page.locator('#create-tenant').waitFor({ timeout: 8000 });

    await page.screenshot({
      path: `${SCREENSHOTS_DIR}/14-create-with-tenant-dialog${suffix}.png`,
    });
  });
}
