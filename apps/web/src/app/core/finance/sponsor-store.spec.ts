import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { FinanceApi } from './finance-api';
import { deal, sponsorPage } from './finance-test-data';
import { SponsorStore } from './sponsor-store';

/**
 * The sponsor store's guarantees.
 *
 * Nothing is optimistic: the offers, the bonuses and the deal are the server's. A signing is made against the
 * version the server last returned, and a stale one reloads the page and says nothing was signed.
 */
function problem(status: number, code: string, detail: string): ApiError {
  return new ApiError(status, code, detail, null, new Map());
}

describe('SponsorStore', () => {
  let api: { getSponsor: ReturnType<typeof vi.fn>; signSponsor: ReturnType<typeof vi.fn> };
  let store: SponsorStore;

  beforeEach(() => {
    api = { getSponsor: vi.fn(), signSponsor: vi.fn() };

    TestBed.configureTestingModule({ providers: [{ provide: FinanceApi, useValue: api }] });

    store = TestBed.inject(SponsorStore);
  });

  it('reads the offers', () => {
    api.getSponsor.mockReturnValue(of(sponsorPage()));

    store.load();

    expect(store.page()?.offers).toHaveLength(3);
    expect(store.loaded()).toBe(true);
    expect(store.loading()).toBe(false);
  });

  it('reports a refused read instead of showing an empty page', () => {
    api.getSponsor.mockReturnValue(
      throwError(() => problem(404, 'NO_SPONSOR_OFFERS', 'The club has been made no offers.')),
    );

    store.load();

    expect(store.error()).toBe('The club has been made no offers.');
    expect(store.page()).toBeNull();
  });

  it('signs against the version it last read and adopts the page the server returns', () => {
    api.getSponsor.mockReturnValue(of(sponsorPage({ version: 0 })));
    api.signSponsor.mockReturnValue(of(sponsorPage({ version: 1, deal: deal() })));

    store.load();
    store.sign({ offer: 'balanced', objective: 'top_six' });

    expect(api.signSponsor).toHaveBeenCalledWith({ offer: 'balanced', objective: 'top_six' }, 0);
    expect(store.page()?.deal?.kind).toBe('balanced');
    expect(store.signedMessage()).toContain('Signed with balanced sponsor');
    expect(store.signing()).toBe(false);
  });

  it('reloads and says nothing was signed when the deal changed underneath', () => {
    api.getSponsor
      .mockReturnValueOnce(of(sponsorPage({ version: 0 })))
      .mockReturnValueOnce(of(sponsorPage({ version: 2, deal: deal({ signedBy: 'board' }) })));
    api.signSponsor.mockReturnValue(
      throwError(() => problem(412, 'PRECONDITION_FAILED', 'That changed.')),
    );

    store.load();
    store.sign({ offer: 'steady', objective: 'top_half' });

    expect(api.getSponsor).toHaveBeenCalledTimes(2);
    expect(store.page()?.version).toBe(2);
    expect(store.signError()).toContain('nothing was signed');
  });

  it('shows the server wording when a signing is refused', () => {
    api.getSponsor.mockReturnValue(of(sponsorPage()));
    api.signSponsor.mockReturnValue(
      throwError(() => problem(409, 'SPONSOR_ALREADY_SIGNED', 'The club has signed its sponsor.')),
    );

    store.load();
    store.sign({ offer: 'steady', objective: 'top_half' });

    expect(store.signError()).toBe('The club has signed its sponsor.');
  });

  it('signs nothing before the page has been read', () => {
    store.sign({ offer: 'steady', objective: 'top_half' });

    expect(api.signSponsor).not.toHaveBeenCalled();
  });

  it('forgets everything on clear', () => {
    api.getSponsor.mockReturnValue(of(sponsorPage()));

    store.load();
    store.clear();

    expect(store.page()).toBeNull();
    expect(store.signedMessage()).toBeNull();
  });
});
