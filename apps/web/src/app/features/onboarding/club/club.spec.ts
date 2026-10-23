import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../../core/api/api-error';
import { MaintenanceStore } from '../../../core/maintenance/maintenance-store';
import { OnboardingStore } from '../../../core/world/onboarding-store';
import { AvailableClubs, TakeoverOption, TakeoverPreview } from '../../../core/world/world.models';
import { ClubChoice } from './club';

/**
 * The club step of onboarding.
 *
 * Taking a club over is a choice of start: keep it as it is, or a clean start. Both are read before the claim and
 * shown side by side, and nothing is claimed until one of them is chosen. "Take over" only opens the choice.
 */
describe('ClubChoice', () => {
  const clubs: AvailableClubs = {
    countryId: 'country-1',
    divisionId: 'division-1',
    divisionName: 'Premier Division',
    tierNumber: 1,
    clubs: [
      {
        id: 'club-1',
        name: 'Ashvale United',
        shortName: 'ASH',
        city: 'Ashvale',
        region: 'North',
        badgeSeed: 'a',
        reputation: 50,
        tierNumber: 1,
        divisionName: 'Premier Division',
        isAvailable: true,
      },
    ],
    serverTime: '2026-09-01T00:00:00Z',
  };

  function option(overrides: Partial<TakeoverOption> = {}): TakeoverOption {
    return {
      start: 'keep',
      players: 22,
      squadStrength: 11.4,
      seasonWageMinor: 700_000_000,
      cashMinor: 500_000_000,
      debtMinor: 0,
      financialState: 'healthy',
      groundPlaces: 18_000,
      groundLevel: 4,
      snackBarLevel: 3,
      shopLevel: 2,
      seasonIncomeForecastMinor: 1_200_000_000,
      wageBudgetMinor: 840_000_000,
      overdraftLimitMinor: 300_000_000,
      ...overrides,
    };
  }

  function preview(): TakeoverPreview {
    return {
      clubId: 'club-1',
      clubName: 'Ashvale United',
      tierNumber: 1,
      keep: option({
        cashMinor: -150_000_000,
        debtMinor: 150_000_000,
        financialState: 'in_the_red',
      }),
      cleanStart: option({
        start: 'clean_start',
        players: 24,
        squadStrength: 10.2,
        groundPlaces: 5_000,
        groundLevel: 1,
        snackBarLevel: 1,
        shopLevel: 1,
      }),
    };
  }

  let store: {
    availableClubs: ReturnType<typeof signal<AvailableClubs | null>>;
    provisioningSettled: ReturnType<typeof signal<number>>;
    loadAvailableClubs: ReturnType<typeof vi.fn>;
    loadTakeoverPreview: ReturnType<typeof vi.fn>;
    claimClub: ReturnType<typeof vi.fn>;
    startProvisioningPoll: ReturnType<typeof vi.fn>;
    stopProvisioningPoll: ReturnType<typeof vi.fn>;
  };
  let fixture: ComponentFixture<ClubChoice>;
  let root: HTMLElement;
  let navigate: ReturnType<typeof vi.spyOn>;
  let canMutate: ReturnType<typeof signal<boolean>>;

  beforeEach(async () => {
    canMutate = signal(true);
    store = {
      availableClubs: signal<AvailableClubs | null>(clubs),
      provisioningSettled: signal(0),
      loadAvailableClubs: vi.fn().mockReturnValue(of(clubs)),
      loadTakeoverPreview: vi.fn().mockReturnValue(of(preview())),
      claimClub: vi.fn().mockReturnValue(of({})),
      startProvisioningPoll: vi.fn(),
      stopProvisioningPoll: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [ClubChoice],
      providers: [
        provideRouter([]),
        { provide: OnboardingStore, useValue: store },
        { provide: MaintenanceStore, useValue: { canMutate } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({ countryId: 'country-1' }) } },
        },
      ],
    }).compileComponents();

    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(ClubChoice);
    root = fixture.nativeElement as HTMLElement;

    await fixture.whenStable();
  });

  const button = (name: string | RegExp) =>
    Array.from(root.querySelectorAll<HTMLButtonElement>('button')).find((candidate) =>
      typeof name === 'string'
        ? candidate.textContent?.trim() === name
        : name.test(candidate.textContent?.trim() ?? ''),
    );

  async function openChoice(): Promise<void> {
    root.querySelector<HTMLButtonElement>('button[aria-label="Take over Ashvale United"]')!.click();
    await fixture.whenStable();
  }

  it('groups the clubs of both tiers under a heading each, best tier first', async () => {
    const second: AvailableClubs = {
      ...clubs,
      clubs: [
        {
          ...clubs.clubs[0],
          id: 'club-2',
          name: 'Bramford City',
          tierNumber: 2,
          divisionName: 'Second Division',
        },
        clubs.clubs[0],
      ],
    };
    store.availableClubs.set(second);
    fixture.detectChanges();

    const headings = Array.from(
      root.querySelectorAll('[data-testid="clubs-table"] [data-testid="tier-heading"]'),
    ).map((heading) => heading.textContent?.replace(/\s+/g, ' ').trim());

    expect(headings).toEqual(['Premier Division · tier 1', 'Second Division · tier 2']);
    expect(
      root.querySelectorAll('[data-testid="clubs-table"] button[aria-label^="Take over"]'),
    ).toHaveLength(2);
  });

  it('claims nothing when "Take over" is pressed, and opens the choice of start instead', async () => {
    await openChoice();

    expect(store.claimClub).not.toHaveBeenCalled();
    expect(store.loadTakeoverPreview).toHaveBeenCalledWith('club-1');
    expect(root.querySelector('[data-testid="start-choice"]')).not.toBeNull();
    expect(root.querySelector('[data-testid="start-choice"] h2')!.textContent).toContain(
      'How do you want to take over Ashvale United?',
    );
  });

  it('shows both starts side by side as the server would leave the club', async () => {
    await openChoice();

    const keep = root.querySelector<HTMLElement>('[data-testid="start-keep"]')!;
    const clean = root.querySelector<HTMLElement>('[data-testid="start-clean"]')!;

    expect(keep.textContent).toContain('Keep it as it is');
    expect(keep.textContent).toContain('18,000 places (level 4)');
    expect(keep.textContent).toContain('In the red');
    expect(keep.textContent).toContain('Debt');
    expect(clean.textContent).toContain('Clean start');
    expect(clean.textContent).toContain('5,000 places (level 1)');
    expect(clean.textContent).toContain('Healthy');
    expect(clean.textContent).not.toContain('Debt');
    expect(clean.textContent).toContain('It cannot be undone');
  });

  it('keeps the club as it is on the first button', async () => {
    await openChoice();
    button('Keep the club as it is')!.click();

    expect(store.claimClub).toHaveBeenCalledWith('club-1', 'keep');
    expect(navigate).toHaveBeenCalledWith(['/onboarding/colours']);
  });

  it('begins again on the second button', async () => {
    await openChoice();
    button('Start clean')!.click();

    expect(store.claimClub).toHaveBeenCalledWith('club-1', 'clean_start');
    expect(navigate).toHaveBeenCalledWith(['/onboarding/colours']);
  });

  it('goes back to the clubs without claiming', async () => {
    await openChoice();
    button('Back to the clubs')!.click();
    await fixture.whenStable();

    expect(store.claimClub).not.toHaveBeenCalled();
    expect(root.querySelector('[data-testid="start-choice"]')).toBeNull();
    expect(root.querySelector('[data-testid="clubs-table"]')).not.toBeNull();
  });

  it('says so when the two starts could not be read, and claims nothing', async () => {
    store.loadTakeoverPreview.mockReturnValue(
      throwError(
        () => new ApiError(409, 'CLUB_ALREADY_CLAIMED', 'Somebody took the club.', null, new Map()),
      ),
    );
    await openChoice();

    expect(root.querySelector('[role="alert"]')!.textContent).toContain('Somebody took the club.');
    expect(button('Keep the club as it is')).toBeUndefined();
    expect(store.claimClub).not.toHaveBeenCalled();
  });

  it('stays on the choice and says why when the claim is refused', async () => {
    store.claimClub.mockReturnValue(
      throwError(
        () => new ApiError(409, 'CLUB_ALREADY_CLAIMED', 'Somebody took the club.', null, new Map()),
      ),
    );
    await openChoice();
    button('Start clean')!.click();
    await fixture.whenStable();

    expect(root.textContent).toContain('Somebody took the club.');
    expect(navigate).not.toHaveBeenCalled();
    expect(button('Start clean')!.disabled).toBe(false);
  });

  it('cannot claim while writes are refused', async () => {
    await openChoice();
    canMutate.set(false);
    await fixture.whenStable();

    expect(button('Keep the club as it is')!.disabled).toBe(true);
    expect(button('Start clean')!.disabled).toBe(true);
  });
});
