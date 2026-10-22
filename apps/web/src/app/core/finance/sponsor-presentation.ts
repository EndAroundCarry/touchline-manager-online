/**
 * Client-side presentation helpers for the sponsor page (plan toasty-scott section 3; `SPN-1`…`SPN-6`, `OBJ-1`…`OBJ-4`).
 *
 * Small and pure. The server owns the codes, the places and every amount; these helpers name them in words.
 */
import { SeasonObjective, SponsorDeal, SponsorOffer, SponsorOfferKind } from './sponsor.models';

const OFFER_LABELS: Record<SponsorOfferKind, string> = {
  steady: 'Steady',
  balanced: 'Balanced',
  performance: 'Performance',
};

const OFFER_BLURBS: Record<SponsorOfferKind, string> = {
  steady: 'All of it fixed. A small bonus if you meet the objective.',
  balanced: 'Most of it fixed, with a bonus that rewards meeting the objective.',
  performance: 'Less fixed money and a big bonus. Pays most to a club that beats its objective.',
};

const OBJECTIVE_LABELS: Record<SeasonObjective, string> = {
  win_league: 'Win the league',
  top_three: 'Finish in the top 3',
  top_six: 'Finish in the top 6',
  top_half: 'Finish in the top half',
  eleventh: 'Finish 11th or better',
  stay_up: 'Stay up',
};

/** The objectives a manager can choose, hardest first. */
export const SEASON_OBJECTIVES: readonly SeasonObjective[] = [
  'win_league',
  'top_three',
  'top_six',
  'top_half',
  'eleventh',
  'stay_up',
];

/** Names an offer. */
export function offerLabel(kind: SponsorOfferKind): string {
  return OFFER_LABELS[kind];
}

/** Says in a sentence how an offer splits its money between the fixed part and the bonus. */
export function offerBlurb(kind: SponsorOfferKind): string {
  return OFFER_BLURBS[kind];
}

/** Names an objective, falling back to the code so a new one still renders. */
export function objectiveLabel(objective: string): string {
  return OBJECTIVE_LABELS[objective as SeasonObjective] ?? objective;
}

/** Names an objective with the place it asks for: "Finish in the top 3 (3rd or better)". */
export function objectiveWithPlace(objective: SeasonObjective, targetPlace: number): string {
  return objective === 'win_league'
    ? objectiveLabel(objective)
    : `${objectiveLabel(objective)} (${placeOrBetter(targetPlace)})`;
}

/** "3rd or better". */
export function placeOrBetter(place: number): string {
  return `${ordinalOf(place)} or better`;
}

/** An ordinal in English: 1st, 2nd, 3rd, 4th, 11th, 22nd. */
export function ordinalOf(value: number): string {
  const lastTwo = value % 100;
  const suffix =
    lastTwo >= 11 && lastTwo <= 13 ? 'th' : (['th', 'st', 'nd', 'rd'][value % 10] ?? 'th');

  return `${value}${suffix}`;
}

/** Finds the offer of a kind. */
export function offerOf(
  offers: readonly SponsorOffer[],
  kind: SponsorOfferKind,
): SponsorOffer | undefined {
  return offers.find((offer) => offer.kind === kind);
}

/** What an offer pays if the objective is met, or null when the offer does not quote it. */
export function bonusFor(
  offer: SponsorOffer,
  objective: SeasonObjective,
): { readonly metMinor: number; readonly exceededMinor: number } | null {
  const quote = offer.objectives.find((candidate) => candidate.objective === objective);

  return quote === undefined
    ? null
    : { metMinor: quote.bonusMetMinor, exceededMinor: quote.bonusExceededMinor };
}

/** Who signed the deal, in words. */
export function signedByLabel(signedBy: string): string {
  switch (signedBy) {
    case 'manager':
      return 'You signed it';
    case 'board':
      return 'The board signed it at the deadline';
    case 'ai':
      return 'Signed for you';
    default:
      return signedBy;
  }
}

/** How the season ended against the objective, in words, or null before it is settled. */
export function outcomeLabel(deal: SponsorDeal): string | null {
  switch (deal.outcome) {
    case 'met':
      return 'Objective met';
    case 'exceeded':
      return 'Objective exceeded';
    case 'missed':
      return 'Objective missed';
    default:
      return null;
  }
}
