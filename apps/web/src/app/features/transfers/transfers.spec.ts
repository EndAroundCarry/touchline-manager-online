import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { SquadStore } from '../../core/squad/squad-store';
import { TransfersStore } from '../../core/transfers/transfers-store';
import { TransferHistoryEntry, TransferListing } from '../../core/transfers/transfers.models';
import { PlayerCardService } from '../../shared/ui/player-card/player-card.service';
import { Transfers } from './transfers';

/**
 * The transfers screen's two tabs.
 *
 * The clubs' listings are the tab a manager lands on; the weekly free-agent pool is the second. A free agent
 * has no selling club, so every place that names a seller has to say so instead of linking to nothing (`FA-5`).
 */

function listing(id: string, overrides: Partial<TransferListing> = {}): TransferListing {
  return {
    listingId: id,
    playerId: `player-${id}`,
    playerName: `Player ${id}`,
    playerShortName: id.toUpperCase(),
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

function freeAgent(id: string, overrides: Partial<TransferListing> = {}): TransferListing {
  return listing(id, {
    kind: 'free_agent',
    sellerClubId: null,
    sellerClubName: null,
    ability: 8,
    age: 27,
    primaryPosition: 'cb',
    minimumFeeMinor: 250_000,
    minimumAcceptableBidMinor: 250_000,
    ...overrides,
  });
}

describe('Transfers', () => {
  let fixture: ComponentFixture<Transfers>;
  let root: HTMLElement;
  let clubListings: WritableSignal<readonly TransferListing[]>;
  let freeAgents: WritableSignal<readonly TransferListing[]>;
  let history: WritableSignal<readonly TransferHistoryEntry[]>;
  let freeAgentFamily: WritableSignal<string>;
  let store: {
    load: ReturnType<typeof vi.fn>;
    bid: ReturnType<typeof vi.fn>;
    setFreeAgentFamily: ReturnType<typeof vi.fn>;
    loadMoreFreeAgents: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    clubListings = signal([listing('l1')]);
    freeAgents = signal([freeAgent('f1', { leadingAmountMinor: 300_000, bidderCount: 2 })]);
    history = signal([]);
    freeAgentFamily = signal('');

    store = {
      load: vi.fn(),
      bid: vi.fn(),
      setFreeAgentFamily: vi.fn((family: string) => freeAgentFamily.set(family)),
      loadMoreFreeAgents: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [Transfers],
      providers: [
        provideRouter([]),
        {
          provide: TransfersStore,
          useValue: {
            ...store,
            listings: clubListings,
            freeAgents,
            freeAgentFamily,
            myListings: signal([]),
            myBids: signal([]),
            history,
            loading: signal(false),
            loadingMore: signal(false),
            hasMore: signal(false),
            hasMoreFreeAgents: signal(true),
            freeAgentsLoading: signal(false),
            freeAgentsLoadingMore: signal(false),
            error: signal(null),
          },
        },
        {
          provide: SquadStore,
          useValue: {
            contracts: signal({ contracts: [] }),
            loadContracts: () => of({ contracts: [] }),
          },
        },
        { provide: MaintenanceStore, useValue: { canMutate: signal(true) } },
        {
          provide: PlayerCardService,
          useValue: { card: signal(null), show: vi.fn(), hide: vi.fn() },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Transfers);
    await fixture.whenStable();

    root = fixture.nativeElement as HTMLElement;
  });

  async function openTab(name: string): Promise<void> {
    const tab = Array.from(root.querySelectorAll<HTMLButtonElement>('[role="tab"]')).find(
      (button) => button.textContent?.trim() === name,
    );

    tab?.click();
    await fixture.whenStable();
  }

  it('offers the transfer list and the free agents, with the clubs’ listings open first', () => {
    const tabs = Array.from(root.querySelectorAll<HTMLElement>('[role="tab"]'));

    expect(tabs.map((tab) => tab.textContent?.trim())).toEqual(['Transfer list', 'Free agents']);
    expect(tabs[0].getAttribute('aria-selected')).toBe('true');
    expect(root.querySelector('[data-testid="listings-table"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="free-agents-table"]')).toBeNull();
  });

  it('shows a club listing’s seller as a link to the club', () => {
    const link = root.querySelector<HTMLAnchorElement>(
      '[data-testid="listings-table"] a[href="/clubs/club-1"]',
    );

    expect(link?.textContent?.trim()).toBe('Somewhere FC');
  });

  it('lists the free agents with their position, age, ability, wage, minimum and leading bid', async () => {
    await openTab('Free agents');

    const table = root.querySelector<HTMLElement>('[data-testid="free-agents-table"]')!;
    const headers = Array.from(table.querySelectorAll('th')).map((th) => th.textContent?.trim());

    expect(headers).toEqual([
      'Player',
      'Position',
      'Age',
      'Ability',
      'Wage a season',
      'Minimum',
      'Leading',
      'Bids',
      'Closes',
      'Your bid',
    ]);

    const cells = Array.from(table.querySelectorAll('tbody tr td')).map((td) =>
      td.textContent?.replace(/\s+/g, ' ').trim(),
    );

    expect(cells[0]).toBe('Player f1');
    expect(cells[1]).toBe('Centre back');
    expect(cells[2]).toBe('27');
    expect(cells[3]).toBe('8');
    expect(cells[4]).toContain('2 seasons');
    expect(cells[7]).toBe('2');
  });

  it('links a free agent to his page without naming a club', async () => {
    await openTab('Free agents');

    const table = root.querySelector<HTMLElement>('[data-testid="free-agents-table"]')!;

    expect(table.querySelector('a[href="/players/player-f1"]')).not.toBeNull();
    expect(table.querySelector('a[href^="/clubs/"]')).toBeNull();
  });

  it('bids on a free agent from his own row', async () => {
    await openTab('Free agents');

    const row = root.querySelector<HTMLElement>('[data-testid="free-agents-table"] tbody tr')!;
    const input = row.querySelector<HTMLInputElement>('input[name="amount"]')!;

    input.value = '400000';
    row.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));

    expect(store.bid).toHaveBeenCalledWith('f1', 400_000);
  });

  it('narrows the pool by position through the store', async () => {
    await openTab('Free agents');

    const select = root.querySelector<HTMLSelectElement>('#free-agent-family')!;

    select.value = 'goalkeeper';
    select.dispatchEvent(new Event('change'));

    expect(store.setFreeAgentFamily).toHaveBeenCalledWith('goalkeeper');
  });

  it('reads the next page of free agents on request', async () => {
    await openTab('Free agents');

    Array.from(root.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.trim() === 'Load more free agents')!
      .click();

    expect(store.loadMoreFreeAgents).toHaveBeenCalledOnce();
  });

  it('says when the pool is empty and when the next batch opens', async () => {
    freeAgents.set([]);
    await openTab('Free agents');

    expect(root.textContent).toContain('No free agents are listed right now');
    expect(root.textContent).toContain('Monday at 12:00 UTC');
  });

  it('moves between the tabs with the arrow keys', async () => {
    const first = root.querySelector<HTMLButtonElement>('#transfers-tab-club')!;

    first.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await fixture.whenStable();

    expect(root.querySelector('#transfers-tab-free-agents')!.getAttribute('aria-selected')).toBe(
      'true',
    );
    expect(root.querySelector('[role="tabpanel"]')!.id).toBe('transfers-panel-free-agents');
  });

  it('names a free agent’s previous seller “Free agent” in the recent transfers, with no club link', async () => {
    history.set([
      {
        listingId: 'h1',
        playerId: 'player-h1',
        playerName: 'Signed Man',
        sellerClubId: null,
        sellerClubName: null,
        buyerClubId: 'club-2',
        buyerClubName: 'Vale Rovers',
        feeMinor: 300_000,
        resolvedAt: '2026-10-05T12:00:00Z',
      },
    ]);
    await fixture.whenStable();

    const row = Array.from(root.querySelectorAll<HTMLElement>('tbody tr')).find((tr) =>
      tr.textContent?.includes('Signed Man'),
    )!;
    const cells = Array.from(row.querySelectorAll('td')).map((td) => td.textContent?.trim());

    expect(cells[1]).toBe('Free agent');
    expect(cells[2]).toBe('Vale Rovers');
    expect(row.querySelectorAll('a[href^="/clubs/"]')).toHaveLength(1);
  });
});
