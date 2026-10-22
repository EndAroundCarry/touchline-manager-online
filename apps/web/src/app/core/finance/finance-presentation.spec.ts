import {
  categoryLabel,
  creditsOf,
  debitsOf,
  placeLabel,
  redFromSentence,
  stateBanner,
  streamRows,
  wageGauge,
  wageImpactHeadline,
  wageImpactTone,
} from './finance-presentation';
import { outlook, wageImpact } from './finance-test-data';

/**
 * The finance screen's presentation helpers.
 *
 * Small and pure: the server owns every figure, and these name them, order them and weigh a tone, always in
 * words so a colour is never the only signal (`§11.3`).
 */
describe('categoryLabel', () => {
  it.each([
    ['league_tv', 'League TV money'],
    ['sponsor_matchday', 'Sponsor'],
    ['sponsor_bonus', 'Sponsor bonus'],
    ['parachute', 'Parachute payment'],
    ['ground_upkeep', 'Ground upkeep'],
    ['clean_start', 'Clean start'],
    ['position_award', 'League prize money'],
  ])('names %s', (code, label) => {
    expect(categoryLabel(code)).toBe(label);
  });

  it('falls back to the code so a new category still renders', () => {
    expect(categoryLabel('something_new')).toBe('something_new');
  });
});

describe('stateBanner', () => {
  it('says nothing about a healthy club, or one without a season', () => {
    expect(stateBanner(outlook())).toBeNull();
    expect(stateBanner(null)).toBeNull();
  });

  it('tells a club in the red what it stops and what ends it, without frightening it about the fans', () => {
    const banner = stateBanner(outlook({ state: 'in_the_red', embargo: true }))!;

    expect(banner.tone).toBe('warn');
    expect(banner.title).toBe('The club is in the red');
    expect(banner.body).toContain('bids for players and new building are stopped');
    expect(banner.body).toContain('the fans do not mind');
  });

  it('counts the matchdays a club in administration has left', () => {
    const banner = stateBanner(
      outlook({ state: 'administration', embargo: true, administrationMatchdaysLeft: 12 }),
    )!;

    expect(banner.tone).toBe('bad');
    expect(banner.body).toContain('in 12 matchdays');
  });

  it('uses the singular for the last matchday', () => {
    const banner = stateBanner(
      outlook({ state: 'administration', embargo: true, administrationMatchdaysLeft: 1 }),
    )!;

    expect(banner.body).toContain('in 1 matchday,');
  });
});

describe('streamRows', () => {
  it('sets the forecast against what the ledger has moved under the same category', () => {
    const rows = streamRows(outlook(), [
      { category: 'league_tv', amountMinor: 117_000_000 },
      { category: 'wages', amountMinor: -205_000_000 },
    ]);

    const tv = rows.find((row) => row.category === 'league_tv')!;
    const wages = rows.find((row) => row.category === 'wages')!;

    expect(tv).toEqual({
      category: 'league_tv',
      label: 'League TV money',
      forecastMinor: 400_000_000,
      actualMinor: 117_000_000,
    });
    expect(wages.forecastMinor).toBe(-700_000_000);
    expect(wages.actualMinor).toBe(-205_000_000);
  });

  it('reads a stream the ledger has not touched as nothing taken yet', () => {
    const gate = streamRows(outlook(), []).find((row) => row.category === 'gate_receipt')!;

    expect(gate.actualMinor).toBe(0);
  });

  it('leaves out a stream with neither a forecast nor a movement, such as an unsigned sponsor', () => {
    const rows = streamRows(
      outlook({
        streams: [
          { category: 'sponsor_matchday', forecastMinor: 0 },
          { category: 'league_tv', forecastMinor: 400_000_000 },
        ],
      }),
      [],
    );

    expect(rows.map((row) => row.category)).toEqual(['league_tv']);
  });

  it('keeps the server order, income first and then cost', () => {
    const rows = streamRows(outlook(), []);
    const firstCost = rows.findIndex((row) => row.forecastMinor < 0);

    expect(rows.slice(0, firstCost).every((row) => row.forecastMinor >= 0)).toBe(true);
    expect(rows.slice(firstCost).every((row) => row.forecastMinor < 0)).toBe(true);
  });
});

describe('creditsOf and debitsOf', () => {
  const totals = [
    { category: 'position_award', amountMinor: 5 },
    { category: 'transfer_payment', amountMinor: -3 },
    { category: 'bid_reservation', amountMinor: 0 },
  ];

  it('split the totals into money in and money out and drop a category that moved nothing', () => {
    expect(creditsOf(totals).map((row) => row.label)).toEqual(['League prize money']);
    expect(debitsOf(totals).map((row) => row.label)).toEqual(['Transfer fees paid']);
  });
});

describe('placeLabel', () => {
  it.each([
    [1, '1st of 18'],
    [2, '2nd of 18'],
    [3, '3rd of 18'],
    [7, '7th of 18'],
    [11, '11th of 18'],
    [12, '12th of 18'],
    [13, '13th of 18'],
    [18, '18th of 18'],
  ])('writes place %i as %s', (place, text) => {
    expect(placeLabel(place, 18)).toBe(text);
  });
});

describe('wageGauge', () => {
  it('puts the budget marker at the budget and fills the bar to the bill', () => {
    const gauge = wageGauge(outlook());

    expect(gauge.sharePercent).toBe(58);
    expect(gauge.budgetPercent).toBeGreaterThan(gauge.fillPercent);
    expect(gauge.tone).toBe('good');
    expect(gauge.caption).toBe('Within the wage budget');
  });

  it('turns amber over the budget and red once the bill is more than the income', () => {
    const over = wageGauge(outlook({ overBudget: true, wageShareBp: 8_000 }));
    const broke = wageGauge(outlook({ overBudget: true, wageShareBp: 11_000 }));

    expect(over.tone).toBe('warn');
    expect(over.caption).toBe('Over the wage budget');
    expect(over.fillPercent).toBeGreaterThan(over.budgetPercent);
    expect(broke.tone).toBe('bad');
  });

  it('never draws past the end of the bar', () => {
    const gauge = wageGauge(outlook({ overBudget: true, wageShareBp: 40_000 }));

    expect(gauge.fillPercent).toBeLessThanOrEqual(100);
    expect(gauge.budgetPercent).toBeLessThanOrEqual(100);
  });

  it('copes with a club that has no forecast yet', () => {
    const gauge = wageGauge(outlook({ forecastMinor: 0, wageBudgetMinor: 0, wageShareBp: 0 }));

    expect(gauge.fillPercent).toBe(0);
    expect(gauge.budgetPercent).toBe(0);
  });
});

describe('wage impact words', () => {
  it.each([
    ['fits', 'Fits your budget', 'good'],
    ['stretching', 'Stretches your budget', 'warn'],
    ['danger', 'Danger: you would run out of cash', 'bad'],
    ['administration_risk', 'Administration risk', 'bad'],
  ] as const)('says %s in words and weighs it %s', (level, headline, tone) => {
    expect(wageImpactHeadline(level)).toBe(headline);
    expect(wageImpactTone(level)).toBe(tone);
  });

  it('adds when the club would first be in the red, and says more when it ends past its limit', () => {
    expect(redFromSentence(wageImpact())).toBeNull();
    expect(redFromSentence(wageImpact({ level: 'danger', redFromMatchday: 21 }))).toBe(
      'You would be in the red from about matchday 21.',
    );
    expect(
      redFromSentence(wageImpact({ level: 'administration_risk', redFromMatchday: 14 })),
    ).toContain('past your overdraft limit');
  });
});
