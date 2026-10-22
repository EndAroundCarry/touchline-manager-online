import { ComponentFixture, TestBed } from '@angular/core/testing';
import { wageImpact } from '../../../core/finance/finance-test-data';
import { TransferListing } from '../../../core/transfers/transfers.models';
import { BidForm } from './bid-form';

/**
 * The bid form.
 *
 * A bid is placed from the amount field. When the server says the signing puts the club in danger it also asks
 * for a confirm tick, and the bid waits for it; the server never refuses a bid for it, so the tick is the form's
 * own courtesy and nothing else is held back.
 */
function listing(overrides: Partial<TransferListing> = {}): TransferListing {
  return {
    listingId: 'l1',
    playerId: 'player-l1',
    playerName: 'Player l1',
    playerShortName: 'L1',
    primaryPosition: 'st',
    age: 24,
    ability: 12,
    kind: 'club',
    sellerClubId: 'club-1',
    sellerClubName: 'Somewhere FC',
    minimumFeeMinor: 1_000_000,
    generatedBuyerSeasonWageMinor: 50_000,
    generatedContractSeasons: 2,
    opensAt: '2026-09-28T12:00:00Z',
    endsAt: '2026-10-05T12:00:00Z',
    status: 'open',
    leadingAmountMinor: null,
    bidderCount: 0,
    minimumAcceptableBidMinor: 1_000_000,
    yourBidMinor: null,
    version: 1,
    ...overrides,
  };
}

describe('BidForm', () => {
  let fixture: ComponentFixture<BidForm>;
  let root: HTMLElement;
  let bids: number[];

  function render(value: TransferListing, inputs: Record<string, unknown> = {}): void {
    fixture = TestBed.createComponent(BidForm);
    bids = [];
    fixture.componentInstance.bid.subscribe((amount) => bids.push(amount));
    fixture.componentRef.setInput('listing', value);

    for (const [name, input] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, input);
    }

    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const amount = () => root.querySelector<HTMLInputElement>('input[name="amount"]')!;
  const submit = () => root.querySelector<HTMLButtonElement>('button[type="submit"]')!;
  const tick = () => root.querySelector<HTMLInputElement>('input[type="checkbox"]');

  function press(): void {
    root.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
  }

  it('starts at the smallest bid the listing accepts', () => {
    render(listing({ minimumAcceptableBidMinor: 1_250_000 }));

    expect(amount().value).toBe('1250000');
  });

  it('places the bid as the amount typed', () => {
    render(listing());
    amount().value = '1400000';
    press();

    expect(bids).toEqual([1_400_000]);
  });

  it('gives the amount field an id from the prefix, so a row and a card never share one', () => {
    render(listing(), { idPrefix: 'bid-mobile-' });

    expect(amount().id).toBe('bid-mobile-l1');
  });

  it('shows no panel and asks for no tick when the server sent no wage impact', () => {
    render(listing());

    expect(root.querySelector('app-wage-impact')).toBeNull();
    expect(tick()).toBeNull();
  });

  it('shows the wage impact folded when the signing fits, and bids without a tick', () => {
    render(listing({ wageImpact: wageImpact() }));

    expect(root.querySelector('app-wage-impact')).not.toBeNull();
    expect(tick()).toBeNull();
    press();

    expect(bids).toEqual([1_000_000]);
  });

  it('waits for the confirm tick when the club would run out of cash', () => {
    render(
      listing({
        wageImpact: wageImpact({ level: 'danger', needsConfirmation: true, redFromMatchday: 20 }),
      }),
    );

    expect(tick()).not.toBeNull();
    expect(submit().disabled).toBe(true);
    press();
    expect(bids).toEqual([]);

    tick()!.click();
    fixture.detectChanges();

    expect(submit().disabled).toBe(false);
    press();
    expect(bids).toEqual([1_000_000]);
  });

  it('asks for the tick at administration risk too', () => {
    render(
      listing({
        wageImpact: wageImpact({ level: 'administration_risk', needsConfirmation: true }),
      }),
    );

    expect(tick()).not.toBeNull();
    expect(submit().disabled).toBe(true);
  });

  it('does not ask for a tick when the signing only stretches the budget', () => {
    render(listing({ wageImpact: wageImpact({ level: 'stretching' }) }));

    expect(tick()).toBeNull();
    expect(submit().disabled).toBe(false);
  });

  it('is disabled while writes are refused', () => {
    render(listing(), { disabled: true });

    expect(submit().disabled).toBe(true);
  });
});
