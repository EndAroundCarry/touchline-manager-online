/**
 * The sponsor page's transport mirror (plan toasty-scott section 3; `SPN-1`…`SPN-6`, `OBJ-1`…`OBJ-4`).
 *
 * Hand-written to match `Tactical11Manager.Contracts.Finance`. Amounts are minor units; the bonus beside every
 * objective is the server's own, so the page prints it and never computes it (`INT-1`).
 */

/** The three offers a club's sponsors make, from the safest to the riskiest. */
export type SponsorOfferKind = 'steady' | 'balanced' | 'performance';

/** The season objectives, hardest first. */
export type SeasonObjective =
  'win_league' | 'top_three' | 'top_six' | 'top_half' | 'eleventh' | 'stay_up';

/** What one objective pays under one offer. */
export interface SponsorObjectiveQuote {
  readonly objective: SeasonObjective;
  readonly targetPlace: number;
  readonly bonusMetMinor: number;
  readonly bonusExceededMinor: number;
}

/** One of the three offers a club's sponsors make for the season. */
export interface SponsorOffer {
  readonly kind: SponsorOfferKind;
  readonly sponsorName: string;
  readonly fixedSeasonMinor: number;
  readonly fixedMatchdayMinor: number;
  readonly bonusMultiplierBp: number;
  readonly objectives: readonly SponsorObjectiveQuote[];
}

/** The deal a club signed for the season and, once the season is over, how it turned out. */
export interface SponsorDeal {
  readonly kind: SponsorOfferKind;
  readonly sponsorName: string;
  readonly objective: SeasonObjective;
  readonly targetPlace: number;
  readonly fixedSeasonMinor: number;
  readonly fixedMatchdayMinor: number;
  readonly bonusMetMinor: number;
  readonly bonusExceededMinor: number;

  /** Who signed: the `manager`, the `board` at the deadline, or `ai`. */
  readonly signedBy: string;
  readonly signedAt: string;

  /** How the season ended against the objective (`missed`, `met` or `exceeded`), or null before it is settled. */
  readonly outcome: string | null;
  readonly bonusPaidMinor: number | null;
  readonly finalPlace: number | null;
}

/** The club's sponsor and objective for the season. */
export interface SponsorPage {
  readonly seasonNumber: number;

  /** When the board signs for the club if it has not: the fourth matchday's team-sheet lock. */
  readonly deadline: string;

  /** The objective the board thinks fits the squad. */
  readonly realisticObjective: SeasonObjective;
  readonly matchdaysPlayed: number;
  readonly offers: readonly SponsorOffer[];
  readonly deal: SponsorDeal | null;

  /** Whether the manager can sign or change the deal now. */
  readonly canSign: boolean;

  /** The deal's version, sent back in `If-Match`: 0 before there is a deal. */
  readonly version: number;
  readonly serverTime: string;
}

/** The choice a manager signs. */
export interface SignSponsorPayload {
  readonly offer: SponsorOfferKind;
  readonly objective: SeasonObjective;
}
