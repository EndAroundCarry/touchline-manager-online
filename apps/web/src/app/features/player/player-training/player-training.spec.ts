import { Component, input, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PlayerTrainingStore } from '../../../core/training/player-training-store';
import type { PlayerTraining } from '../../../core/training/training.models';
import type { ChartSeries } from '../../../core/training/training-chart';
import { LineChart } from '../../../shared/ui/line-chart/line-chart';
import { PlayerTraining as PlayerTrainingTab } from './player-training';

/**
 * The player page's Training tab.
 *
 * jsdom has no canvas, so the chart is replaced by a stub that records what it is given. The behaviours that
 * matter: the regime card says what the player trains now; the controls reshape the chart's data; every
 * value the chart draws is also in a table; an empty history says that it builds up; and a refusal is shown.
 */

@Component({ selector: 'app-line-chart', template: '' })
class StubLineChart {
  readonly labels = input.required<readonly string[]>();
  readonly series = input.required<readonly ChartSeries[]>();
  readonly ariaLabel = input.required<string>();
  readonly yTitle = input('Points');
}

const populated: PlayerTraining = {
  playerId: 'p1',
  regime: {
    programme: 'winger',
    label: 'Winger',
    description: 'For wide attackers.',
    isDefaultProgramme: false,
    intensity: 'intense',
    attributes: [
      { name: 'pace', family: 'physical', weight: 3 },
      { name: 'firstTouch', family: 'technical', weight: 1 },
    ],
  },
  days: [
    {
      day: '2026-10-01',
      programme: 'forward',
      intensity: 'normal',
      growth: 0.4,
      pointsGained: 1,
      pointsLost: 0,
      attributeChanges: [{ attribute: 'finishing', delta: 1 }],
    },
    {
      day: '2026-10-02',
      programme: 'winger',
      intensity: 'intense',
      growth: 0.3,
      pointsGained: 0,
      pointsLost: 0,
      attributeChanges: [],
    },
    {
      day: '2026-10-03',
      programme: 'winger',
      intensity: 'intense',
      growth: -0.1,
      pointsGained: 0,
      pointsLost: 1,
      attributeChanges: [{ attribute: 'stamina', delta: -1 }],
    },
  ],
  summary: [
    { programme: 'forward', label: 'Forward', days: 1, pointsGained: 1, pointsLost: 0, net: 1 },
    { programme: 'winger', label: 'Winger', days: 2, pointsGained: 0, pointsLost: 1, net: -1 },
  ],
  serverTime: '2026-10-04T00:00:00Z',
};

describe('PlayerTraining tab', () => {
  let fixture: ComponentFixture<PlayerTrainingTab>;
  let root: HTMLElement;
  let store: {
    history: ReturnType<typeof signal<PlayerTraining | null>>;
    loading: ReturnType<typeof signal<boolean>>;
    error: ReturnType<typeof signal<string | null>>;
    load: ReturnType<typeof vi.fn>;
  };

  async function open(history: PlayerTraining | null, error: string | null = null): Promise<void> {
    store = {
      history: signal(history),
      loading: signal(false),
      error: signal(error),
      load: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [PlayerTrainingTab],
      providers: [provideRouter([]), { provide: PlayerTrainingStore, useValue: store }],
    })
      .overrideComponent(PlayerTrainingTab, {
        remove: { imports: [LineChart] },
        add: { imports: [StubLineChart] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(PlayerTrainingTab);
    fixture.componentRef.setInput('playerId', 'p1');
    await fixture.whenStable();

    root = fixture.nativeElement as HTMLElement;
  }

  const chart = () =>
    fixture.debugElement.query((element) => element.name === 'app-line-chart')
      ?.componentInstance as StubLineChart | undefined;

  const select = (label: string) =>
    Array.from(root.querySelectorAll('label'))
      .find((element) => element.textContent?.includes(label))!
      .querySelector('select')!;

  async function choose(label: string, value: string): Promise<void> {
    const element = select(label);

    element.value = value;
    element.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }

  it('reads the player’s history when the tab opens', async () => {
    await open(populated);

    expect(store.load).toHaveBeenCalledWith('p1');
  });

  describe('with history', () => {
    beforeEach(async () => {
      await open(populated);
    });

    it('says what the player trains now: programme, whose choice, intensity and skills by weight', () => {
      const card = root.querySelector('[data-testid="training-regime"]')!;

      expect(card.textContent).toContain('Winger');
      expect(card.textContent).toContain('chosen by you');
      expect(card.textContent).toContain('Intense intensity');
      expect(card.textContent).toContain('For wide attackers.');

      const skills = Array.from(card.querySelectorAll('ul li'));

      expect(skills.map((skill) => skill.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
        'Pace •••, core focus',
        'First touch •, supporting focus',
      ]);
      expect(skills[0].classList.contains('bg-sky-300')).toBe(true);
      expect(skills[1].classList.contains('bg-sky-100')).toBe(true);
      expect(card.querySelector('a')?.getAttribute('href')).toBe('/training');
    });

    it('flags a position default as such', async () => {
      store.history.set({
        ...populated,
        regime: { ...populated.regime, isDefaultProgramme: true },
      });
      await fixture.whenStable();

      expect(root.querySelector('[data-testid="training-regime"]')?.textContent).toContain(
        'the default for this position',
      );
    });

    it('draws one series per regime, with the chart summarised for a screen reader', () => {
      expect(
        chart()!
          .series()
          .map((line) => line.label),
      ).toEqual(['Forward', 'Winger']);
      expect(chart()!.labels()).toHaveLength(3);
      expect(chart()!.ariaLabel()).toContain('Daily attribute growth');
      expect(chart()!.yTitle()).toBe('Net points per day');
    });

    it('offers all regimes, then each one the player has history for', () => {
      expect(
        Array.from(select('Regime').options).map((option) => option.textContent?.trim()),
      ).toEqual(['All regimes', 'Forward', 'Winger']);
    });

    it('narrows the chart to the chosen regime', async () => {
      await choose('Regime', 'winger');

      expect(
        chart()!
          .series()
          .map((line) => line.label),
      ).toEqual(['Winger']);
      expect(chart()!.labels()).toHaveLength(2);
    });

    it('switches between daily and cumulative, and says which is pressed', async () => {
      const buttons = Array.from(root.querySelectorAll<HTMLButtonElement>('[aria-pressed]'));

      expect(buttons.map((button) => button.getAttribute('aria-pressed'))).toEqual([
        'true',
        'false',
      ]);

      buttons[1].click();
      await fixture.whenStable();

      expect(buttons.map((button) => button.getAttribute('aria-pressed'))).toEqual([
        'false',
        'true',
      ]);

      const winger = chart()!
        .series()
        .find((line) => line.programme === 'winger')!;

      expect(winger.data).toEqual([null, 0.3, 0.2]);
      expect(chart()!.yTitle()).toBe('Net points so far');
    });

    it('offers the three ranges, last 90 days first chosen', () => {
      const range = select('Range');

      expect(Array.from(range.options).map((option) => option.value)).toEqual(['30', '90', 'all']);
      expect(range.value).toBe('90');
    });

    it('lists a summary line per regime with days, gained, lost and signed net', () => {
      const rows = Array.from(
        root.querySelectorAll('[data-testid="training-summary"] tbody tr'),
      ).map((row) => Array.from(row.children).map((cell) => cell.textContent?.trim()));

      expect(rows).toEqual([
        ['Forward', '1', '1', '0', '+1'],
        ['Winger', '2', '0', '1', '−1'],
      ]);
    });

    it('lists every plotted point in a table, with the attributes that moved', () => {
      const rows = Array.from(
        root.querySelectorAll('[data-testid="training-points"] tbody tr'),
      ).map((row) => Array.from(row.children).map((cell) => cell.textContent?.trim()));

      expect(rows).toEqual([
        ['2026-10-01', 'Forward', '0.400', '+1 Finishing'],
        ['2026-10-02', 'Winger', '0.300', ''],
        ['2026-10-03', 'Winger', '-0.100', '−1 Stamina'],
      ]);
    });

    it('keeps the points table in step with the controls', async () => {
      await choose('Regime', 'forward');

      expect(root.querySelectorAll('[data-testid="training-points"] tbody tr')).toHaveLength(1);
    });
  });

  it('says so when the chosen range holds no days for the regime, and draws no chart', async () => {
    await open({
      ...populated,
      days: [{ ...populated.days[0], day: '2026-05-01' }, ...populated.days.slice(1)],
    });

    await choose('Range', '30');
    await choose('Regime', 'forward');

    expect(root.querySelector('[data-testid="training-no-days"]')).not.toBeNull();
    expect(chart()).toBeUndefined();
  });

  it('says history builds up as daily training runs, when there is none yet', async () => {
    await open({ ...populated, days: [], summary: [] });

    expect(root.querySelector('[data-testid="training-empty"]')?.textContent).toContain(
      'History builds up as daily training runs.',
    );
    expect(root.querySelector('[data-testid="training-regime"]')).not.toBeNull();
    expect(chart()).toBeUndefined();
    expect(root.querySelector('[data-testid="training-controls"]')).toBeNull();
  });

  it('names a programme that trains nothing', async () => {
    await open({
      ...populated,
      days: [],
      summary: [],
      regime: { ...populated.regime, programme: 'recovery', label: 'Recovery', attributes: [] },
    });

    expect(root.textContent).toContain('This programme trains no attributes.');
  });

  it('ignores a history that is for another player', async () => {
    await open({ ...populated, playerId: 'someone-else' });

    expect(root.querySelector('[data-testid="training-regime"]')).toBeNull();
  });

  it('shows a refusal in an alert', async () => {
    await open(null, 'That player is not at your club.');

    expect(root.querySelector('[role="alert"]')?.textContent).toContain(
      'That player is not at your club.',
    );
  });
});
