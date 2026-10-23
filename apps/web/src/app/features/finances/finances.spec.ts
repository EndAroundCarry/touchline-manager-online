import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { FinanceStore } from '../../core/finance/finance-store';
import { deal, outlook, sponsorPage, summary } from '../../core/finance/finance-test-data';
import { FinanceSummary } from '../../core/finance/finance.models';
import { SponsorStore } from '../../core/finance/sponsor-store';
import { SponsorPage } from '../../core/finance/sponsor.models';
import { Finances } from './finances';
import { configurePresentation, resetPresentation } from '../../core/world/presentation';

// The short form of an amount follows the manager's locale, so the specs pin one.
beforeEach(() => configurePresentation({ locale: 'en-GB', timeZone: 'UTC' }));
afterEach(() => resetPresentation());

/**
 * The finances screen.
 *
 * What it must get right: the figures are the server's and are shown short with the whole amount on hover; a club
 * in the red or in administration is told so in words; the wages are shown against the budget; the prize at the
 * present place is said as "if the season ended today"; and the sponsor deal, or the lack of one, is on the card.
 */
describe('Finances', () => {
  let fixture: ComponentFixture<Finances>;
  let root: HTMLElement;
  let current: WritableSignal<FinanceSummary | null>;
  let sponsor: WritableSignal<SponsorPage | null>;
  let loadSponsor: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    current = signal<FinanceSummary | null>(summary());
    sponsor = signal<SponsorPage | null>(sponsorPage({ deal: deal(), version: 1 }));
    loadSponsor = vi.fn();

    await TestBed.configureTestingModule({
      imports: [Finances],
      providers: [
        provideRouter([]),
        {
          provide: FinanceStore,
          useValue: {
            load: vi.fn(),
            loadMore: vi.fn(),
            summary: current,
            ledger: signal([]),
            hasMore: signal(false),
            loading: signal(false),
            loadingMore: signal(false),
            error: signal(null),
          },
        },
        {
          provide: SponsorStore,
          useValue: { load: loadSponsor, page: sponsor, loading: signal(false) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Finances);
    await fixture.whenStable();

    root = fixture.nativeElement as HTMLElement;
  });

  async function show(value: FinanceSummary): Promise<void> {
    current.set(value);
    await fixture.whenStable();
  }

  /** The text a sighted reader sees: the screen-reader copy of each amount is left out. */
  function visible(element: Element | null): string | undefined {
    if (element === null) {
      return undefined;
    }

    const clone = element.cloneNode(true) as HTMLElement;

    clone.querySelectorAll('.sr-only').forEach((node) => node.remove());

    return clone.textContent?.replace(/\s+/g, ' ').trim();
  }

  const text = (selector: string) => visible(root.querySelector(selector));

  it('reads the sponsor beside the finances', () => {
    expect(loadSponsor).toHaveBeenCalledOnce();
  });

  it('shows the cash and the forecast short, with the whole amount on hover', () => {
    const cash = root.querySelector<HTMLElement>('[data-testid="cash"] [data-testid="money"]')!;

    expect(cash.textContent).toContain('5m');
    expect(cash.getAttribute('title')).toBe('5,000,000.00');
    expect(text('[data-testid="forecast"]')).toContain('12m');
    expect(text('dl')).toContain('10 of 30 matchdays played');
  });

  it('says nothing about the state while the club is healthy', () => {
    expect(root.querySelector('[data-testid="state-banner"]')).toBeNull();
  });

  it('warns a club in the red in words, and shows the overdraft limit it is held to', async () => {
    await show(
      summary({
        cashMinor: -20_000_000,
        outlook: outlook({ state: 'in_the_red', embargo: true }),
      }),
    );

    const banner = root.querySelector<HTMLElement>('[data-testid="state-banner"]')!;

    expect(banner.getAttribute('role')).toBe('alert');
    expect(banner.textContent).toContain('The club is in the red');
    expect(text('[data-testid="cash"]')).toContain('−200k');
    expect(root.textContent).toContain('Overdraft limit');
  });

  it('counts the matchdays left to a club in administration', async () => {
    await show(
      summary({
        outlook: outlook({
          state: 'administration',
          embargo: true,
          administrationMatchdaysLeft: 20,
        }),
      }),
    );

    expect(text('[data-testid="state-banner"]')).toContain('in administration');
    expect(text('[data-testid="state-banner"]')).toContain('in 20 matchdays');
  });

  it('draws the wages against the budget and says whether the bill is over it', async () => {
    expect(text('[data-testid="wage-summary"]')).toContain('7m');
    expect(text('[data-testid="wage-summary"]')).toContain('8.4m budget');
    expect(root.querySelector('[data-testid="wage-gauge"]')!.getAttribute('aria-label')).toContain(
      'Within the wage budget',
    );

    await show(summary({ outlook: outlook({ overBudget: true, wageShareBp: 8_000 }) }));

    expect(root.querySelector('[data-testid="wage-gauge"]')!.getAttribute('aria-label')).toContain(
      'Over the wage budget',
    );
  });

  it('says what the league would pay if the season ended today', () => {
    expect(text('[data-testid="prize-today-amount"]')).toBe(
      'If the season ended today: 7th of 16 → 3m',
    );
  });

  it('says the table has not opened when there is no place yet', async () => {
    await show(summary({ outlook: outlook({ prize: null }) }));

    expect(root.querySelector('[data-testid="prize-today-amount"]')).toBeNull();
    expect(root.textContent).toContain('The table opens with the first matchday');
  });

  it('sets each stream of the forecast against what the ledger has taken so far', () => {
    const rows = Array.from(
      root.querySelectorAll<HTMLElement>('[data-testid="forecast-table"] tbody tr'),
    ).map((row) =>
      Array.from(row.children)
        .map((cell) => visible(cell))
        .join(' | '),
    );

    expect(rows).toContain('League TV money | +4m | +1.17m');
    expect(rows).toContain('Wages | −7m | −2.05m');
    expect(rows.some((row) => row?.startsWith('Money in'))).toBe(true);
    expect(rows.some((row) => row?.startsWith('Money out'))).toBe(true);
  });

  it('keeps the movements outside the forecast apart, in words', () => {
    expect(root.textContent).toContain('Other money in');
    expect(root.textContent).toContain('League prize money');
    expect(root.textContent).toContain('Transfer fees paid');
  });

  it('shows the signed deal and its objective', () => {
    const card = text('[data-testid="sponsor-deal"]')!;

    expect(card).toContain('balanced sponsor · Balanced');
    expect(card).toContain('Finish in the top 6 (6th or better)');
    expect(card).toContain('Bonus if met');
    expect(text('[data-testid="sponsor-link"]')).toBe('Change the deal');
  });

  it('tells a club with no deal when the board will sign for it', async () => {
    sponsor.set(sponsorPage());
    await fixture.whenStable();

    expect(text('[data-testid="sponsor-unsigned"]')).toContain('No sponsor is signed yet');
    expect(text('[data-testid="sponsor-link"]')).toBe('Choose a sponsor');
  });

  it('shows how a settled deal ended', async () => {
    sponsor.set(
      sponsorPage({
        deal: deal({ outcome: 'exceeded', bonusPaidMinor: 150_000_000 }),
        canSign: false,
        version: 2,
      }),
    );
    await fixture.whenStable();

    expect(text('[data-testid="sponsor-deal"]')).toContain('Objective exceeded');
    expect(text('[data-testid="sponsor-deal"]')).toContain('1.5m paid');
    expect(text('[data-testid="sponsor-link"]')).toBe('See the offers');
  });

  it('still shows the money of a club that has no season yet', async () => {
    await show(summary({ outlook: null }));

    expect(root.querySelector('[data-testid="forecast-table"]')).toBeNull();
    expect(root.textContent).toContain('Available once the season starts');
    expect(root.textContent).toContain('Cash');
  });
});
