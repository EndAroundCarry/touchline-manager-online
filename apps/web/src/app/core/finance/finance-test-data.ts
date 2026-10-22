import { FinanceOutlook, FinanceSummary, WageImpact } from './finance.models';
import { SponsorDeal, SponsorOffer, SponsorPage } from './sponsor.models';

/** Builders for the specs that read the finances and the sponsor. Not imported by the app. */

/** A healthy tier-1 club part-way through the season, on the starting shape of the economy. */
export function outlook(overrides: Partial<FinanceOutlook> = {}): FinanceOutlook {
  return {
    state: 'healthy',
    embargo: false,
    administrationMatchdaysLeft: null,
    matchdaysPlayed: 10,
    matchdaysInSeason: 34,
    seasonWageMinor: 700_000_000,
    wageBudgetMinor: 840_000_000,
    wageShareBp: 5_800,
    forecastMinor: 1_200_000_000,
    overdraftLimitMinor: 300_000_000,
    overBudget: false,
    streams: [
      { category: 'league_tv', forecastMinor: 400_000_000 },
      { category: 'sponsor_matchday', forecastMinor: 350_000_000 },
      { category: 'gate_receipt', forecastMinor: 180_000_000 },
      { category: 'snack_bar', forecastMinor: 30_000_000 },
      { category: 'merchandise', forecastMinor: 40_000_000 },
      { category: 'wages', forecastMinor: -700_000_000 },
      { category: 'operating_cost', forecastMinor: -120_000_000 },
      { category: 'structure_upkeep', forecastMinor: -20_000_000 },
      { category: 'ground_upkeep', forecastMinor: -10_000_000 },
    ],
    prize: { place: 7, clubs: 18, amountMinor: 300_000_000 },
    ...overrides,
  };
}

/** The club's money as the finance read returns it. */
export function summary(overrides: Partial<FinanceSummary> = {}): FinanceSummary {
  return {
    cashMinor: 500_000_000,
    reservedMinor: 50_000_000,
    availableMinor: 450_000_000,
    weeklyWageMinor: 61_764_705,
    contractedPlayers: 22,
    weeklySponsorshipMinor: 35_294_117,
    weeklyOperatingCostMinor: 10_588_235,
    tierNumber: 1,
    seasonLabel: '2026/27',
    warnings: [],
    totals: [
      { category: 'league_tv', amountMinor: 117_000_000 },
      { category: 'sponsor_matchday', amountMinor: 103_000_000 },
      { category: 'wages', amountMinor: -205_000_000 },
      { category: 'transfer_payment', amountMinor: -80_000_000 },
      { category: 'position_award', amountMinor: 20_000_000 },
    ],
    serverTime: '2026-10-22T00:00:00Z',
    outlook: outlook(),
    ...overrides,
  };
}

/** What a signing would do to the club's wages. */
export function wageImpact(overrides: Partial<WageImpact> = {}): WageImpact {
  return {
    level: 'fits',
    addedSeasonWageMinor: 90_000_000,
    wageBillAfterMinor: 790_000_000,
    wageBudgetMinor: 840_000_000,
    wageShareBp: 6_600,
    seasonEndCashMinor: 200_000_000,
    redFromMatchday: null,
    needsConfirmation: false,
    ...overrides,
  };
}

function offer(kind: SponsorOffer['kind'], fixedSeasonMinor: number, bonus: number): SponsorOffer {
  return {
    kind,
    sponsorName: `${kind} sponsor`,
    fixedSeasonMinor,
    fixedMatchdayMinor: Math.floor(fixedSeasonMinor / 34),
    bonusMultiplierBp: 10_000,
    objectives: [
      { objective: 'win_league', targetPlace: 1, bonusMetMinor: bonus * 3, bonusExceededMinor: 0 },
      {
        objective: 'top_three',
        targetPlace: 3,
        bonusMetMinor: bonus * 2,
        bonusExceededMinor: bonus * 3,
      },
      { objective: 'top_six', targetPlace: 6, bonusMetMinor: bonus, bonusExceededMinor: bonus * 2 },
      {
        objective: 'top_half',
        targetPlace: 8,
        bonusMetMinor: bonus / 2,
        bonusExceededMinor: bonus,
      },
      {
        objective: 'eleventh',
        targetPlace: 11,
        bonusMetMinor: bonus / 4,
        bonusExceededMinor: bonus / 2,
      },
      {
        objective: 'stay_up',
        targetPlace: 13,
        bonusMetMinor: bonus / 8,
        bonusExceededMinor: bonus / 4,
      },
    ],
  };
}

/** The three offers, steady first. */
export function offers(): readonly SponsorOffer[] {
  return [
    offer('steady', 350_000_000, 50_000_000),
    offer('balanced', 300_000_000, 100_000_000),
    offer('performance', 230_000_000, 200_000_000),
  ];
}

/** A signed deal at the balanced offer and top-six objective. */
export function deal(overrides: Partial<SponsorDeal> = {}): SponsorDeal {
  return {
    kind: 'balanced',
    sponsorName: 'balanced sponsor',
    objective: 'top_six',
    targetPlace: 6,
    fixedSeasonMinor: 300_000_000,
    fixedMatchdayMinor: 8_823_529,
    bonusMetMinor: 100_000_000,
    bonusExceededMinor: 200_000_000,
    signedBy: 'manager',
    signedAt: '2026-10-05T10:00:00Z',
    outcome: null,
    bonusPaidMinor: null,
    finalPlace: null,
    ...overrides,
  };
}

/** The sponsor page, unsigned by default. */
export function sponsorPage(overrides: Partial<SponsorPage> = {}): SponsorPage {
  return {
    seasonNumber: 1,
    deadline: '2026-10-12T17:00:00Z',
    realisticObjective: 'top_six',
    matchdaysPlayed: 1,
    offers: offers(),
    deal: null,
    canSign: true,
    version: 0,
    serverTime: '2026-10-06T00:00:00Z',
    ...overrides,
  };
}
