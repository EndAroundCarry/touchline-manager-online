/**
 * The world and onboarding transport shapes.
 *
 * These mirror `TouchlineManager.Contracts.World` on the server. They are handwritten for now, like the
 * auth shapes, because the generated client arrives with the OpenAPI pipeline (`tools/openapi-client`);
 * when it lands these interfaces are replaced by its output rather than kept in step by hand.
 */

/** One season's real-time window. */
export interface SeasonSummary {
  readonly id: string;
  readonly sequenceNumber: number;
  readonly displayLabel: string;
  readonly gameYear: number;
  readonly status: string;
  readonly ruleSetVersion: string;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly rolloverEndsAt: string;
}

/** The world a manager is onboarding into. */
export interface WorldSummary {
  readonly id: string;
  readonly name: string;
  readonly status: string;
  readonly ruleSetVersion: string;
  readonly currentSeasonNumber: number;
  readonly acceptsClaims: boolean;
  readonly currentSeason: SeasonSummary | null;
  readonly serverTime: string;
}

/** One of the world's countries. */
export interface CountrySummary {
  readonly id: string;
  readonly code: string;
  readonly displayName: string;
  readonly locale: string;
  readonly sortOrder: number;
}

/** The state of a country's next-tier generation. */
export interface ProvisioningStatus {
  readonly requestId: string;
  readonly targetTier: number;
  readonly status: string;
  readonly requestedAt: string;
  readonly startedAt: string | null;
  readonly completedAt: string | null;
  readonly pollAfterSeconds: number;
}

/** How full a country's pyramid is, measured at its lowest active tier. */
export interface CountryCapacity {
  readonly countryId: string;
  readonly lowestActiveTier: number;
  readonly lowestActiveTierDivisionId: string;
  readonly lowestActiveTierName: string;
  readonly clubsInLowestTier: number;
  readonly humanOccupiedClubs: number;
  readonly availableClubs: number;
  readonly lowestTierIsFull: boolean;
  readonly targetTierForExpansion: number;
  readonly provisioning: ProvisioningStatus | null;
  readonly serverTime: string;
}

/** One club offered to a manager. */
export interface AvailableClub {
  readonly id: string;
  readonly name: string;
  readonly shortName: string;
  readonly city: string;
  readonly region: string;
  readonly badgeSeed: string;
  readonly reputation: number;
  readonly stadiumBaseline: number;
  readonly isAvailable: boolean;
}

/** The clubs of a country's lowest active tier. */
export interface AvailableClubs {
  readonly countryId: string;
  readonly divisionId: string;
  readonly divisionName: string;
  readonly tierNumber: number;
  readonly clubs: readonly AvailableClub[];
  readonly serverTime: string;
}

/** A manager profile. */
export interface ManagerProfile {
  readonly id: string;
  readonly reputation: number;
  readonly takeoverCooldownUntil: string | null;
  readonly locale: string;
  readonly timeZone: string;
  readonly version: number;
}

/** The club a manager controls. */
export interface ClubTenureSummary {
  readonly id: string;
  readonly clubId: string;
  readonly clubName: string;
  readonly clubShortName: string;
  readonly countryDisplayName: string;
  readonly divisionName: string;
  readonly tierNumber: number;
  readonly controlStatus: string;
  readonly startedAt: string;
  readonly lastActiveAt: string;
  readonly version: number;
}

/** Where an account stands in onboarding. */
export interface OnboardingState {
  readonly manager: ManagerProfile | null;
  readonly tenure: ClubTenureSummary | null;
  readonly serverTime: string;
}

/** A club's identity and standing. */
export interface ClubSummary {
  readonly id: string;
  readonly name: string;
  readonly shortName: string;
  readonly slug: string;
  readonly city: string;
  readonly region: string;
  readonly badgeSeed: string;
  readonly foundingGameYear: number;
  readonly status: string;
  readonly reputation: number;
  readonly stadiumBaseline: number;
}

/** A tier of a country's pyramid. */
export interface DivisionSummary {
  readonly id: string;
  readonly tierNumber: number;
  readonly displayName: string;
  readonly status: string;
  readonly capacity: number;
}

/** Who controls a club. `ai` means no human holds it. */
export interface ClubControl {
  readonly status: string;
  readonly tenureId: string | null;
  readonly startedAt: string | null;
  readonly lastActiveAt: string | null;
}

/** A club's money, in minor units. */
export interface ClubFinances {
  readonly cashMinor: number;
  readonly reservedMinor: number;
  readonly availableMinor: number;
}

/** The inherited-club dashboard. */
export interface ClubDashboard {
  readonly club: ClubSummary;
  readonly country: CountrySummary;
  readonly division: DivisionSummary;
  readonly season: SeasonSummary;
  readonly control: ClubControl;
  readonly finances: ClubFinances;
  readonly serverTime: string;
}

/** Request bodies. */
export interface CreateManagerProfilePayload {
  readonly locale: string;
  readonly timeZone: string;
}

export interface ClaimClubPayload {
  readonly clubId: string;
}
