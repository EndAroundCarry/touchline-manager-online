import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { deal, sponsorPage } from '../../core/finance/finance-test-data';
import { SponsorStore } from '../../core/finance/sponsor-store';
import { SponsorPage } from '../../core/finance/sponsor.models';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { SponsorScreen } from './sponsor';
import { configurePresentation, resetPresentation } from '../../core/world/presentation';

// The short form of an amount follows the manager's locale, so the specs pin one.
beforeEach(() => configurePresentation({ locale: 'en-GB', timeZone: 'UTC' }));
afterEach(() => resetPresentation());

/**
 * The sponsor page.
 *
 * What it must get right: three offers beside an objective picker that starts at the board's realistic pick (or
 * the signed deal's); the bonus on every card follows the objective and is the server's own figure; a signing goes
 * to the store as the offer and the objective; the page says when the deadline is, and when the deal is fixed.
 */
describe('SponsorScreen', () => {
  let fixture: ComponentFixture<SponsorScreen>;
  let root: HTMLElement;
  let page: WritableSignal<SponsorPage | null>;
  let signError: WritableSignal<string | null>;
  let signedMessage: WritableSignal<string | null>;
  let sign: ReturnType<typeof vi.fn>;
  let canMutate: WritableSignal<boolean>;

  beforeEach(async () => {
    page = signal<SponsorPage | null>(sponsorPage());
    signError = signal<string | null>(null);
    signedMessage = signal<string | null>(null);
    canMutate = signal(true);
    sign = vi.fn();

    await TestBed.configureTestingModule({
      imports: [SponsorScreen],
      providers: [
        provideRouter([]),
        { provide: MaintenanceStore, useValue: { canMutate } },
        {
          provide: SponsorStore,
          useValue: {
            load: vi.fn(),
            sign,
            page,
            loading: signal(false),
            error: signal(null),
            signing: signal(false),
            signError,
            signedMessage,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SponsorScreen);
    await fixture.whenStable();

    root = fixture.nativeElement as HTMLElement;
  });

  const offer = (kind: string) => root.querySelector<HTMLElement>(`[data-testid="offer-${kind}"]`)!;
  const bonus = (kind: string) =>
    offer(kind)
      .querySelector('[data-testid="bonus-met"]')
      ?.textContent?.replace(/\s+/g, ' ')
      .trim();
  const radio = (value: string) =>
    root.querySelector<HTMLInputElement>(`input[name="objective"][value="${value}"]`)!;
  const signButton = (kind: string) => offer(kind).querySelector<HTMLButtonElement>('button')!;

  it('shows the three offers, steady first', () => {
    const kinds = Array.from(root.querySelectorAll<HTMLElement>('[data-testid="offers"] > li')).map(
      (card) => card.getAttribute('data-testid'),
    );

    expect(kinds).toEqual(['offer-steady', 'offer-balanced', 'offer-performance']);
  });

  it('starts the picker at the objective the board thinks fits, and marks it', () => {
    expect(radio('top_six').checked).toBe(true);
    expect(root.querySelector('[data-testid="objective-picker"]')!.textContent).toContain(
      "Board's pick",
    );
  });

  it('quotes every offer at the picked objective with the figures the server sent', () => {
    expect(bonus('steady')).toContain('500k');
    expect(bonus('balanced')).toContain('1m');
    expect(bonus('performance')).toContain('2m');
  });

  it('re-quotes the offers when a harder objective is picked, without working anything out', async () => {
    radio('top_three').click();
    await fixture.whenStable();

    expect(bonus('balanced')).toContain('2m');
    expect(bonus('performance')).toContain('4m');
  });

  it('signs the offer at the picked objective', async () => {
    radio('top_half').click();
    await fixture.whenStable();
    signButton('performance').click();

    expect(sign).toHaveBeenCalledWith({ offer: 'performance', objective: 'top_half' });
  });

  it('gives the deadline and what the board does if nothing is signed', () => {
    const text = root.querySelector('[data-testid="sponsor-deadline"]')!.textContent!;

    expect(text).toContain('Choose before');
    expect(text).toContain('the board signs the');
    expect(text).toContain('balanced offer');
  });

  it('starts at a signed deal and marks its offer', async () => {
    page.set(sponsorPage({ deal: deal({ kind: 'steady', objective: 'top_three' }), version: 1 }));
    await fixture.whenStable();

    expect(radio('top_three').checked).toBe(true);
    expect(offer('steady').textContent).toContain('Signed');
    expect(signButton('steady').disabled).toBe(true);
    expect(signButton('balanced').textContent).toContain('Switch to this offer');
    expect(root.querySelector('[data-testid="sponsor-deadline"]')!.textContent).toContain(
      'You can change the deal until',
    );
  });

  it('is fixed once the deadline has gone, and no card can be signed', async () => {
    page.set(sponsorPage({ deal: deal(), canSign: false, version: 1 }));
    await fixture.whenStable();

    expect(root.querySelector('[data-testid="sponsor-deadline"]')!.textContent).toContain(
      'The deal is fixed for the season',
    );
    expect(signButton('steady').disabled).toBe(true);
    expect(radio('top_half').disabled).toBe(true);
  });

  it('cannot sign while writes are refused', async () => {
    canMutate.set(false);
    await fixture.whenStable();

    expect(signButton('steady').disabled).toBe(true);
  });

  it('shows what the server said when a signing is refused or goes through', async () => {
    signError.set('Your deal changed. Nothing was signed.');
    signedMessage.set('Signed with balanced sponsor.');
    await fixture.whenStable();

    expect(root.querySelector('[data-testid="sponsor-sign-error"]')!.getAttribute('role')).toBe(
      'alert',
    );
    expect(root.querySelector('[data-testid="sponsor-signed"]')!.textContent).toContain(
      'Signed with balanced sponsor.',
    );
  });
});
