import { APIRequestContext, Page, expect, test } from '@playwright/test';
import { createAccount } from '../support/account';
import { createVerifiedManager } from '../support/auth-flows';

/**
 * The Stage 4 exit criteria for the tactics screen (F-19).
 *
 * A manager onboards, opens the tactics board, assigns an eleven through the accessible, non-drag
 * assignment table (§11.3), and creates the plan. Then a second client — the API, acting as another
 * device — revises the same plan, and the manager's next save is refused with a version conflict. The
 * journey proves the one thing the API tests cannot: that the board keeps the manager's edits, shows the
 * conflict, and lets them reapply intentionally (§11.2, CONC-1).
 *
 * Like the other journeys it gives its club back at the end, so the shared persistent world is left as it
 * was found.
 */

const apiBaseUrl = process.env['E2E_API_URL'] ?? 'http://localhost:5080';

interface AssignedPlayer {
  readonly id: string;
}

interface PlanSlot {
  readonly slotNumber: number;
  readonly positionFamily: string;
  readonly role: string;
  readonly normalizedX: number;
  readonly normalizedY: number;
  readonly assignedPlayer: AssignedPlayer | null;
}

interface Plan {
  readonly id: string;
  readonly name: string;
  readonly formationPreset: string;
  readonly version: number;
  readonly instructions: Readonly<Record<string, string>>;
  readonly slots: readonly PlanSlot[];
}

interface TacticsRead {
  readonly plans: readonly Plan[];
}

interface LoginRead {
  readonly accessToken: string;
}

/** The body a plan write carries, rebuilt from a read so the API can act as another device. */
interface PlanWrite {
  readonly name: string;
  readonly formationPreset: string;
  readonly mentality: string;
  readonly tempo: string;
  readonly passing: string;
  readonly width: string;
  readonly pressing: string;
  readonly defensiveLine: string;
  readonly tackling: string;
  readonly timeWasting: string;
  readonly slots: readonly Omit<PlanSlot, 'assignedPlayer'>[];
  readonly lineup: readonly { readonly slotNumber: number; readonly playerId: string }[];
}

/** Assigns eleven distinct players through the table's selects, the non-drag alternative. */
async function assignEleven(page: Page): Promise<void> {
  const used = new Set<string>();

  for (let slot = 1; slot <= 11; slot += 1) {
    const select = page.getByLabel(`Player for slot ${slot}`, { exact: true });

    const candidates = await select.locator('option').evaluateAll((options) =>
      options
        .map((option) => ({
          value: option.getAttribute('value') ?? '',
          disabled: option.hasAttribute('disabled'),
        }))
        .filter((option) => option.value.length > 0 && !option.disabled),
    );

    const pick = candidates.find((candidate) => !used.has(candidate.value));

    if (pick === undefined) {
      throw new Error(`No selectable player was free for slot ${slot}.`);
    }

    used.add(pick.value);
    await select.selectOption(pick.value);
  }
}

function toWrite(plan: Plan, name: string): PlanWrite {
  return {
    name,
    formationPreset: plan.formationPreset,
    mentality: plan.instructions['mentality'],
    tempo: plan.instructions['tempo'],
    passing: plan.instructions['passing'],
    width: plan.instructions['width'],
    pressing: plan.instructions['pressing'],
    defensiveLine: plan.instructions['defensiveLine'],
    tackling: plan.instructions['tackling'],
    timeWasting: plan.instructions['timeWasting'],
    slots: plan.slots.map((slot) => ({
      slotNumber: slot.slotNumber,
      positionFamily: slot.positionFamily,
      role: slot.role,
      normalizedX: slot.normalizedX,
      normalizedY: slot.normalizedY,
    })),
    lineup: plan.slots
      .filter((slot): slot is PlanSlot & { assignedPlayer: AssignedPlayer } => slot.assignedPlayer !== null)
      .map((slot) => ({ slotNumber: slot.slotNumber, playerId: slot.assignedPlayer.id })),
  };
}

/** Signs in through the API so the journey can act as a second client. */
async function apiSession(request: APIRequestContext, email: string, password: string): Promise<string> {
  const response = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    data: { email, password },
  });

  expect(response.ok()).toBe(true);

  const session = (await response.json()) as LoginRead;

  return session.accessToken;
}

test.describe('tactics', () => {
  test('a manager shapes a plan, and survives a version conflict', async ({ page, request }) => {
    const account = createAccount();
    await createVerifiedManager(page, request, account);

    await page.goto('/onboarding/manager');
    await page.getByRole('button', { name: 'Create my profile' }).click();
    await page.getByRole('button', { name: 'See the clubs' }).first().click();
    await page.getByRole('button', { name: /^Take over/ }).first().click();

    await expect(page).toHaveURL(/\/dashboard$/);

    // The shell offers the tactics board, which is how a manager reaches it.
    await page.getByRole('link', { name: 'Tactics' }).click();
    await expect(page).toHaveURL(/\/tactics$/);
    await expect(page.getByRole('heading', { name: /tactics$/ })).toBeVisible();

    // The board draws the eleven slots as focusable controls, each with an accessible name (TAC-9).
    await expect(page.getByRole('button', { name: /^Slot \d+/ })).toHaveCount(11);

    // Start a fresh plan so the labels below are deterministic, name it, and shape it.
    const planName = `E2E ${account.displayName}`;
    const mine = `${planName} mine`;

    await page.getByRole('button', { name: 'New plan' }).click();
    await page.getByLabel('Plan name').fill(planName);

    // A formation change keeps the eleven slots; the preset is the server's own arrangement (TAC-1).
    await page.getByRole('combobox', { name: 'Formation' }).selectOption('4-3-3');
    await expect(page.getByRole('button', { name: /^Slot \d+/ })).toHaveCount(11);

    // Assign an eleven without dragging anything, which is what a keyboard or screen reader needs.
    await assignEleven(page);
    await expect(page.getByText(/11 of 11 slots filled/)).toBeVisible();

    await page.getByRole('button', { name: 'Create plan' }).click();
    await expect(page.getByText('Your plan was created.')).toBeVisible();

    // A second client — another device — revises the same plan, bumping its version.
    const token = await apiSession(request, account.email, account.password);
    const bearer = { Authorization: `Bearer ${token}` };

    const read = await request.get(`${apiBaseUrl}/api/v1/tactics`, { headers: bearer });
    expect(read.ok()).toBe(true);

    const plans = ((await read.json()) as TacticsRead).plans;
    const created = plans.find((plan) => plan.name === planName);

    expect(created).toBeDefined();

    const elsewhere = await request.put(`${apiBaseUrl}/api/v1/tactics/${created!.id}`, {
      headers: { ...bearer, 'If-Match': `"${created!.version}"` },
      data: toWrite(created!, `${planName} other device`),
    });

    expect(elsewhere.ok()).toBe(true);

    // The manager edits without reloading, and the save is refused as stale rather than overwriting.
    await page.getByLabel('Plan name').fill(mine);
    await page.getByRole('button', { name: 'Save plan' }).click();

    await expect(page.getByText(/changed on another device/)).toBeVisible();
    await expect(page.getByLabel('Plan name')).toHaveValue(mine);

    // Reapplying sends the draft against the version that just arrived, and it succeeds.
    await page.getByRole('button', { name: 'Reapply my changes' }).click();

    await expect(page.getByText('Your plan was saved.')).toBeVisible();
    await expect(page.getByText(/changed on another device/)).toHaveCount(0);

    // Give the club back, as the other journeys do.
    await page.goto('/dashboard');
    await page.getByRole('button', { name: 'Resign from this club' }).click();

    await expect(page.getByRole('heading', { name: 'Choose a club' })).toBeVisible();
  });

  test('a visitor with no session cannot reach the tactics board', async ({ page }) => {
    await page.goto('/tactics');

    await expect(page).toHaveURL(/\/login\?returnUrl=/);
  });
});
