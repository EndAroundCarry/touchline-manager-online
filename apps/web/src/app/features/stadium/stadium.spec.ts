import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { StadiumApi } from '../../core/stadium/stadium-api';
import { Stadium } from '../../core/stadium/stadium.models';
import { StadiumScreen } from './stadium';

/**
 * The stadium screen.
 *
 * What it must get right: the ground is drawn in the club's own colour at the level it has reached; the four
 * kinds of place each show their price, their cost to add and how full they get; an order shows what it
 * costs and cannot be placed when the club cannot pay or in read-only mode; and a placed order goes to the
 * server against the version the screen last read.
 */

function stadium(overrides: Partial<Stadium> = {}): Stadium {
  return {
    clubId: 'c1',
    level: 1,
    maxLevel: 10,
    capacity: 5_000,
    maxCapacity: 50_000,
    seatsPerLevel: 5_000,
    seatsToNextLevel: 1,
    primaryColour: '#8c2f39',
    secondaryColour: '#f2e3e5',
    stands: [
      {
        stand: 'standing',
        seats: 3_000,
        ticketPriceMinor: 800,
        buildCostMinor: 30_000,
        expectedSold: 3_000,
      },
      {
        stand: 'seating',
        seats: 1_000,
        ticketPriceMinor: 1_500,
        buildCostMinor: 60_000,
        expectedSold: 1_000,
      },
      {
        stand: 'covered_seating',
        seats: 900,
        ticketPriceMinor: 2_500,
        buildCostMinor: 100_000,
        expectedSold: 900,
      },
      {
        stand: 'vip',
        seats: 100,
        ticketPriceMinor: 9_000,
        buildCostMinor: 400_000,
        expectedSold: 40,
      },
    ],
    expectedDemand: 9_000,
    fullHouseMinor: 7_050_000,
    expectedGateMinor: 6_810_000,
    availableMinor: 50_000_000,
    version: 7,
    serverTime: '2026-10-06T00:00:00Z',
    ...overrides,
  };
}

describe('Stadium screen', () => {
  let fixture: ComponentFixture<StadiumScreen>;
  let root: HTMLElement;
  let api: { get: ReturnType<typeof vi.fn>; build: ReturnType<typeof vi.fn> };
  let canMutate: ReturnType<typeof signal<boolean>>;

  async function render(ground: Stadium = stadium()): Promise<void> {
    api.get.mockReturnValue(of(ground));

    fixture = TestBed.createComponent(StadiumScreen);
    await fixture.whenStable();

    root = fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    api = { get: vi.fn(), build: vi.fn() };
    canMutate = signal(true);

    TestBed.configureTestingModule({
      imports: [StadiumScreen],
      providers: [
        provideRouter([]),
        { provide: StadiumApi, useValue: api },
        { provide: MaintenanceStore, useValue: { canMutate } },
      ],
    });
  });

  const text = (selector: string) =>
    root.querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim();
  const stand = (code: string) => root.querySelector<HTMLElement>(`[data-stand="${code}"]`)!;
  const type = async (code: string, value: string) => {
    const input = stand(code).querySelector<HTMLInputElement>('input')!;

    input.value = value;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const buildButton = (code: string) =>
    stand(code).querySelector<HTMLButtonElement>('button[type="submit"]')!;

  it('shows the level, the capacity and how far the next level is', async () => {
    await render();

    expect(text('[data-testid="stadium-level"]')).toBe('1 of 10');
    expect(text('[data-testid="stadium-capacity"]')).toBe('5,000');
    expect(text('[data-testid="stadium-next-level"]')).toContain(
      '1 more place takes the stadium to level 2',
    );
    expect(root.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('100');
  });

  it('says there is nowhere further to go at the top level', async () => {
    await render(stadium({ level: 10, capacity: 50_000, seatsToNextLevel: 0 }));

    expect(text('[data-testid="stadium-next-level"]')).toBe(
      'This is the largest stadium there is.',
    );
  });

  it("draws the stadium at its level with the seats in the club's two colours", async () => {
    await render(stadium({ level: 3, primaryColour: '#6a4c93', secondaryColour: '#f2c94c' }));

    const picture = root.querySelector('[data-testid="stadium-figure"] svg')!;
    const strokes = (role: string) =>
      Array.from(
        new Set(
          Array.from(picture.querySelectorAll(`path[data-role="${role}"]`)).map((path) =>
            path.getAttribute('stroke'),
          ),
        ),
      );

    expect(picture.getAttribute('data-level')).toBe('3');
    expect(picture.getAttribute('aria-label')).toContain('level 3');

    expect(strokes('seat')).toEqual(['#6a4c93']);
    expect(strokes('seat-alt')).toEqual(['#f2c94c']);
  });

  it('names the stands in the picture', async () => {
    await render(stadium({ level: 3 }));

    const names = Array.from(
      root.querySelectorAll('[data-testid="stadium-figure"] [data-testid="stadium-labels"] text'),
    ).map((label) => label.textContent);

    expect(names).toEqual(['Main stand', 'East end', 'West end', 'Opposite stand']);
  });

  it('lists the four kinds of place beside the picture, with the places of each', async () => {
    await render();

    const kinds = Array.from(
      root.querySelectorAll<HTMLElement>('[data-testid="stadium-regions"] button'),
    );

    expect(kinds.map((kind) => kind.getAttribute('data-region'))).toEqual([
      'standing',
      'seating',
      'covered_seating',
      'vip',
    ]);
    expect(kinds[0].textContent).toContain('Standing');
    expect(kinds[0].textContent).toContain('3,000 places');
    expect(kinds[3].textContent).toContain('100 places');
    expect(kinds.every((kind) => kind.getAttribute('aria-pressed') === 'false')).toBe(true);
  });

  /** The planes of the picture that are lit, by the kind of place they belong to. */
  const lit = () =>
    Array.from(root.querySelectorAll('[data-testid="stadium-figure"] path[data-role="region"]'))
      .filter((plane) => plane.getAttribute('opacity') !== '0')
      .map((plane) => plane.getAttribute('data-region'));

  it('lights a kind of place in the picture when it is chosen, and lets go when it is chosen again', async () => {
    await render(stadium({ level: 5 }));

    const standing = root.querySelector<HTMLButtonElement>('button[data-region="standing"]')!;

    expect(lit()).toEqual([]);

    standing.click();
    await fixture.whenStable();

    expect(standing.getAttribute('aria-pressed')).toBe('true');
    expect(
      root.querySelector('[data-testid="stadium-figure"] svg')!.getAttribute('data-highlight'),
    ).toBe('standing');
    expect(lit().length).toBeGreaterThan(0);
    expect(new Set(lit())).toEqual(new Set(['standing']));

    standing.click();
    await fixture.whenStable();

    expect(standing.getAttribute('aria-pressed')).toBe('false');
    expect(lit()).toEqual([]);
  });

  it('lights a kind of place while it is pointed at or has focus', async () => {
    await render(stadium({ level: 5 }));

    const vip = root.querySelector<HTMLButtonElement>('button[data-region="vip"]')!;

    vip.dispatchEvent(new Event('pointerenter'));
    await fixture.whenStable();

    expect(new Set(lit())).toEqual(new Set(['vip']));

    vip.dispatchEvent(new Event('pointerleave'));
    await fixture.whenStable();

    expect(lit()).toEqual([]);

    vip.dispatchEvent(new Event('focus'));
    await fixture.whenStable();

    expect(new Set(lit())).toEqual(new Set(['vip']));

    vip.dispatchEvent(new Event('blur'));
    await fixture.whenStable();

    expect(lit()).toEqual([]);
  });

  it('lights the chosen kind even when another is pointed at, then goes back to it', async () => {
    await render(stadium({ level: 5 }));

    root.querySelector<HTMLButtonElement>('button[data-region="seating"]')!.click();
    await fixture.whenStable();

    root
      .querySelector<HTMLButtonElement>('button[data-region="vip"]')!
      .dispatchEvent(new Event('pointerenter'));
    await fixture.whenStable();

    expect(new Set(lit())).toEqual(new Set(['vip']));

    root
      .querySelector<HTMLButtonElement>('button[data-region="vip"]')!
      .dispatchEvent(new Event('pointerleave'));
    await fixture.whenStable();

    expect(new Set(lit())).toEqual(new Set(['seating']));
  });

  it('draws the level gallery lighter than the picture, and without names', async () => {
    await render(stadium({ level: 8, capacity: 37_000 }));

    const figure = root.querySelector('[data-testid="stadium-figure"] svg')!;
    const thumbnail = root.querySelector('[data-testid="stadium-levels"] [data-level="8"] svg')!;

    expect(thumbnail.querySelectorAll('path').length).toBeLessThan(
      figure.querySelectorAll('path').length,
    );
    expect(thumbnail.querySelector('[data-testid="stadium-labels"]')).toBeNull();
    expect(thumbnail.getAttribute('aria-hidden')).toBe('true');
  });

  it('changes the picture when the level changes', async () => {
    await render(stadium({ level: 1 }));
    const first = root.querySelector('[data-testid="stadium-figure"] svg')!.innerHTML.length;

    fixture.destroy();
    await render(stadium({ level: 6, capacity: 27_000 }));
    const sixth = root.querySelector('[data-testid="stadium-figure"] svg')!.innerHTML.length;

    expect(sixth).toBeGreaterThan(first);
  });

  it('lists the ten levels, marking the one the ground is at', async () => {
    await render(stadium({ level: 3, capacity: 12_000 }));

    const levels = Array.from(root.querySelectorAll('[data-testid="stadium-levels"] > li'));

    expect(levels).toHaveLength(10);
    expect(levels.map((li) => li.getAttribute('data-state'))).toEqual([
      'Built',
      'Built',
      'Current',
      'Ahead',
      'Ahead',
      'Ahead',
      'Ahead',
      'Ahead',
      'Ahead',
      'Ahead',
    ]);
    expect(levels[2].getAttribute('aria-current')).toBe('true');
    expect(levels[1].textContent).toContain('5,001');
  });

  it('shows all four kinds of place with their price, cost and fill', async () => {
    await render();

    expect(root.querySelectorAll('[data-testid="stadium-stands"] > li')).toHaveLength(4);

    const standing = stand('standing');

    expect(standing.querySelector('h3')?.textContent).toContain('Standing');
    expect(standing.querySelector('[data-testid="stand-seats"]')?.textContent).toContain('3,000');
    expect(standing.querySelector('[data-testid="stand-price"]')?.textContent).toContain('8.00');
    expect(standing.querySelector('[data-testid="stand-cost"]')?.textContent).toContain('300.00');
    expect(standing.querySelector('[data-testid="stand-fill"]')?.textContent).toContain('100%');

    expect(stand('vip').querySelector('[data-testid="stand-fill"]')?.textContent).toContain('40%');
    expect(stand('covered_seating').querySelector('h3')?.textContent).toContain('Covered seating');
  });

  it('shows what an order costs and what the ground would hold, and enables Build', async () => {
    await render();
    await type('seating', '200');

    const summary = text('[data-testid="order-summary-seating"]')!;

    expect(summary).toContain('200 seats cost');
    expect(summary).toContain('120,000.00');
    expect(summary).toContain('would hold 5,200');
    expect(buildButton('seating').disabled).toBe(false);
  });

  it('fills the order box from a quick amount', async () => {
    await render();

    const quick = Array.from(
      stand('standing').querySelectorAll<HTMLButtonElement>('button[type="button"]'),
    ).find((button) => button.textContent?.trim() === '500')!;

    quick.click();
    await fixture.whenStable();

    expect(stand('standing').querySelector<HTMLInputElement>('input')!.value).toBe('500');
    expect(text('[data-testid="order-summary-standing"]')).toContain('500 standing places cost');
  });

  it('refuses an order the club cannot pay for and says how many it could', async () => {
    await render(stadium({ availableMinor: 1_000_000 }));
    await type('vip', '10');

    expect(stand('vip').textContent).toContain('can pay for 2 of these');
    expect(buildButton('vip').disabled).toBe(true);
  });

  it('refuses an order the ground has no room for', async () => {
    await render(
      stadium({ level: 10, capacity: 49_990, seatsToNextLevel: 0, availableMinor: 900_000_000 }),
    );
    await type('standing', '50');

    expect(stand('standing').textContent).toContain('room for 10 more places');
    expect(buildButton('standing').disabled).toBe(true);
  });

  it('disables ordering in read-only mode', async () => {
    canMutate.set(false);
    await render();

    expect(stand('standing').querySelector<HTMLInputElement>('input')!.disabled).toBe(true);
    expect(buildButton('standing').disabled).toBe(true);
  });

  it('places an order against the version it read and shows the ground that results', async () => {
    api.build.mockReturnValue(
      of(stadium({ version: 8, capacity: 5_200, availableMinor: 44_000_000 })),
    );

    await render();
    await type('seating', '200');
    buildButton('seating').click();
    await fixture.whenStable();

    expect(api.build).toHaveBeenCalledWith({ stand: 'seating', count: 200 }, 7);
    expect(text('[data-testid="stadium-capacity"]')).toBe('5,200');
    expect(text('[data-testid="stadium-built"]')).toBe('200 seats built.');
    expect(stand('seating').querySelector<HTMLInputElement>('input')!.value).toBe('');
  });

  it('announces a new level when an order lifts the ground to it', async () => {
    api.build.mockReturnValue(
      of(stadium({ version: 8, level: 2, capacity: 5_001, seatsToNextLevel: 5_000 })),
    );

    await render();
    await type('standing', '1');
    buildButton('standing').click();
    await fixture.whenStable();

    expect(text('[data-testid="stadium-built"]')).toBe(
      '1 standing place built. The stadium is now level 2.',
    );
    expect(
      root.querySelector('[data-testid="stadium-figure"] svg')?.getAttribute('data-level'),
    ).toBe('2');
  });

  it('labels each Build button with what it builds', async () => {
    await render();

    expect(buildButton('vip').getAttribute('aria-label')).toBe('Build VIP seats');
    expect(buildButton('standing').getAttribute('aria-label')).toBe('Build standing places');
  });
});
