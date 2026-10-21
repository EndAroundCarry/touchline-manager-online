import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { StructuresApi } from '../../core/structures/structures-api';
import { Structure, Structures } from '../../core/structures/structures.models';
import { StructuresScreen } from './structures';

/**
 * The structures screen.
 *
 * What it must get right: each building has its own tab with its level, spend and upkeep; the next level shows
 * its price, build time and profit change; the upgrade button is disabled with a reason when work is under way,
 * cash is short, the game is read-only or the building is at its top level; the bonus table marks the line that
 * applies; and an order goes to the server against that building's version.
 */

function building(overrides: Partial<Structure> = {}): Structure {
  return {
    kind: 'snack_bar',
    level: 1,
    maxLevel: 10,
    version: 3,
    spendMinor: 100,
    upkeepWeeklyMinor: 30_000,
    next: {
      level: 2,
      spendMinor: 125,
      upkeepWeeklyMinor: 45_000,
      costMinor: 2_500_000,
      buildDays: 1,
      estWeeklyProfitChangeMinor: 5_000,
    },
    building: null,
    bonuses: [
      { reason: 'underdog_win', bp: 3_000, current: false },
      { reason: 'even_win', bp: 1_500, current: true },
      { reason: 'favourite_win', bp: 500, current: false },
      { reason: 'underdog_draw', bp: 1_000, current: false },
      { reason: 'none', bp: 0, current: false },
    ],
    recentWeeks: [
      { weekOf: '2026-10-12', takingsMinor: 435_000, upkeepMinor: 30_000, profitMinor: 405_000 },
      { weekOf: '2026-10-05', takingsMinor: 0, upkeepMinor: 30_000, profitMinor: -30_000 },
    ],
    recentMatches: [
      {
        fixtureId: 'f1',
        opponent: 'Harbour Town',
        spectators: 2_900,
        result: 'win',
        goalsFor: 2,
        goalsAgainst: 1,
        bonusBp: 1_500,
        bonusReason: 'even_win',
        takingsMinor: 333_500,
      },
    ],
    seasonTakingsMinor: 1_000_000,
    seasonUpkeepMinor: 250_000,
    ...overrides,
  };
}

function structures(
  snackBar: Partial<Structure> = {},
  merchandise: Partial<Structure> = {},
  overrides: Partial<Structures> = {},
): Structures {
  return {
    availableCashMinor: 50_000_000,
    snackBar: building(snackBar),
    merchandise: building({
      kind: 'merchandise',
      version: 8,
      spendMinor: 30,
      bonuses: [
        { reason: 'calm', bp: 500, current: true },
        { reason: 'content', bp: 1_000, current: false },
      ],
      recentMatches: [],
      ...merchandise,
    }),
    serverTime: '2026-10-21T00:00:00Z',
    ...overrides,
  };
}

describe('Structures screen', () => {
  let fixture: ComponentFixture<StructuresScreen>;
  let root: HTMLElement;
  let api: { get: ReturnType<typeof vi.fn>; upgrade: ReturnType<typeof vi.fn> };
  let canMutate: ReturnType<typeof signal<boolean>>;

  async function render(both: Structures = structures()): Promise<void> {
    api.get.mockReturnValue(of(both));

    fixture = TestBed.createComponent(StructuresScreen);
    await fixture.whenStable();

    root = fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    api = { get: vi.fn(), upgrade: vi.fn() };
    canMutate = signal(true);

    TestBed.configureTestingModule({
      imports: [StructuresScreen],
      providers: [
        provideRouter([]),
        { provide: StructuresApi, useValue: api },
        { provide: MaintenanceStore, useValue: { canMutate } },
      ],
    });
  });

  const text = (selector: string) =>
    root.querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim();
  const button = () =>
    root.querySelector<HTMLButtonElement>('[data-testid="structures-upgrade-button"]')!;
  const openTab = async (kind: string) => {
    root.querySelector<HTMLButtonElement>(`#structures-tab-${kind}`)!.click();
    await fixture.whenStable();
  };

  it('opens on the snack bar with its level, spend and upkeep', async () => {
    await render();

    expect(root.querySelector('#structures-tab-snack_bar')?.getAttribute('aria-selected')).toBe(
      'true',
    );
    expect(text('[data-testid="structures-level"]')).toBe('Level 1 of 10');
    expect(text('[data-testid="structures-spend"]')).toBe('1.00');
    expect(text('[data-testid="structures-upkeep"]')).toBe('300.00');
    expect(
      root.querySelectorAll('[data-testid="structures-pips"] li[data-reached="true"]'),
    ).toHaveLength(1);
    expect(root.querySelectorAll('[data-testid="structures-pips"] li')).toHaveLength(10);
  });

  it('shows the next level with its price, build time and profit change', async () => {
    await render();

    expect(text('[data-testid="structures-next-spend"]')).toBe('1.25');
    expect(text('[data-testid="structures-next-upkeep"]')).toBe('450.00');
    expect(text('[data-testid="structures-next-cost"]')).toBe('25,000.00');
    expect(text('[data-testid="structures-next-days"]')).toBe('1 day');
    expect(text('[data-testid="structures-next-profit"]')).toBe('+50.00');
    expect(text('[data-testid="structures-available"]')).toBe('500,000.00');
    expect(button().disabled).toBe(false);
  });

  it('marks the result bonus that applied to the last home match', async () => {
    await render();

    const lines = Array.from(root.querySelectorAll('[data-testid="structures-bonuses"] li'));

    expect(lines).toHaveLength(5);
    expect(lines[0].textContent).toContain('Win as the underdog');
    expect(lines[0].textContent).toContain('+30%');
    expect(lines.filter((line) => line.getAttribute('data-current') === 'true')).toHaveLength(1);
    expect(lines[1].textContent).toContain('Win in an even game');
    expect(lines[1].textContent).toContain('last home match');
  });

  it('lists the recent weeks and home matches, and the season totals', async () => {
    await render();

    const weeks = Array.from(root.querySelectorAll('[data-testid="structures-weeks"] tbody tr'));

    expect(weeks).toHaveLength(2);
    expect(weeks[0].textContent).toMatch(/Week of (12 Oct|Oct 12)/);
    expect(weeks[1].textContent).toContain('−300.00');

    const match = text('[data-testid="structures-matches"] tbody tr');

    expect(match).toContain('Harbour Town');
    expect(match).toContain('2,900');
    expect(match).toContain('Won 2–1');
    expect(match).toContain('+15%');
    expect(match).toContain('3,335.00');

    expect(text('[data-testid="structures-season-takings"]')).toBe('10,000.00');
    expect(text('[data-testid="structures-season-upkeep"]')).toBe('2,500.00');
    expect(text('[data-testid="structures-season-profit"]')).toBe('+7,500.00');
  });

  it('says so when nothing has been booked yet', async () => {
    await render(structures({ recentWeeks: [], recentMatches: [] }));

    expect(text('[data-testid="structures-no-weeks"]')).toContain('Nothing has been booked');
    expect(text('[data-testid="structures-no-matches"]')).toContain('not served a home match');
  });

  it('switches to the shop, with its mood bonus and no match list', async () => {
    await render();
    await openTab('merchandise');

    expect(root.querySelector('#structures-tab-merchandise')?.getAttribute('aria-selected')).toBe(
      'true',
    );
    expect(text('[data-testid="structures-spend"]')).toBe('0.30');

    const lines = Array.from(root.querySelectorAll('[data-testid="structures-bonuses"] li'));

    expect(lines[0].textContent).toContain('Calm');
    expect(lines[0].textContent).toContain('+5%');
    expect(lines[0].textContent).toContain('now');
    expect(root.querySelector('[data-testid="structures-matches"]')).toBeNull();
  });

  it('moves between the tabs with the arrow keys', async () => {
    await render();

    root
      .querySelector('#structures-tab-snack_bar')!
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true }));
    await fixture.whenStable();

    expect(root.querySelector('#structures-tab-merchandise')?.getAttribute('aria-selected')).toBe(
      'true',
    );
  });

  it('shows when work finishes and disables the button while a building is going up', async () => {
    await render(
      structures({ building: { targetLevel: 2, dueAt: '2026-10-23T14:00:00Z' }, next: null }),
    );

    expect(text('[data-testid="structures-building"]')).toBe('Building level 2');
    expect(text('[data-testid="structures-ready"]')).toContain('Ready on');
    expect(text('[data-testid="structures-ready"]')).toContain('keeps trading at level 1');
    expect(root.querySelector('[data-testid="structures-upgrade-button"]')).toBeNull();
  });

  it('disables the button with the reason when the club is short of cash', async () => {
    await render(structures({}, {}, { availableCashMinor: 2_499_999 }));

    expect(button().disabled).toBe(true);
    expect(text('[data-testid="structures-upgrade-note"]')).toContain('enough cash');
  });

  it('disables the button in read-only mode', async () => {
    canMutate.set(false);
    await render();

    expect(button().disabled).toBe(true);
    expect(text('[data-testid="structures-upgrade-note"]')).toContain('read-only');
  });

  it('says the building is at its top level and offers no upgrade', async () => {
    await render(structures({ level: 10, next: null }));

    expect(text('[data-testid="structures-top"]')).toBe('Top level');
    expect(root.querySelector('[data-testid="structures-upgrade-button"]')).toBeNull();
  });

  it("orders an upgrade against the open building's version and shows the confirmation", async () => {
    api.upgrade.mockReturnValue(
      of(
        structures(
          { version: 4, building: { targetLevel: 2, dueAt: '2026-10-23T14:00:00Z' }, next: null },
          {},
          { availableCashMinor: 47_500_000 },
        ),
      ),
    );

    await render();
    button().click();
    await fixture.whenStable();

    expect(api.upgrade).toHaveBeenCalledWith('snack_bar', 3);
    expect(text('[data-testid="structures-upgraded"]')).toContain(
      'Snack bar upgrade to level 2 ordered',
    );
    expect(text('[data-testid="structures-available"]')).toBe('475,000.00');
    expect(text('[data-testid="structures-building"]')).toBe('Building level 2');
  });

  it('orders the shop with the shop version', async () => {
    api.upgrade.mockReturnValue(of(structures()));

    await render();
    await openTab('merchandise');
    button().click();
    await fixture.whenStable();

    expect(api.upgrade).toHaveBeenCalledWith('merchandise', 8);
  });
});
