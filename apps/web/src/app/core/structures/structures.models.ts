/**
 * The structures' transport mirror (`STRC-1`…`STRC-9`).
 *
 * Hand-written to match `Tactical11Manager.Contracts.World`, in minor units of the one canonical currency.
 * Every spend, upkeep, bonus and estimate is the server's; the screen formats them and never works one out.
 */

/** The two buildings, as the server's stable codes. */
export type StructureKind = 'snack_bar' | 'merchandise';

/** What the next level of a building would change. */
export interface StructureNextLevel {
  readonly level: number;
  readonly spendMinor: number;
  readonly upkeepWeeklyMinor: number;
  readonly costMinor: number;
  readonly buildDays: number;

  /** Negative when the upkeep outgrows the takings. */
  readonly estWeeklyProfitChangeMinor: number;
}

/** An upgrade that is being built. */
export interface StructureBuilding {
  readonly targetLevel: number;
  readonly dueAt: string;
}

/** One line of a bonus table: a result or a mood, and how much it lifts the takings. */
export interface StructureBonus {
  /** A result such as `underdog_win` or `none` for the snack bar, a fan mood word such as `content` for the shop. */
  readonly reason: string;
  readonly bp: number;

  /** True for the line that applies now (the shop's mood) or applied to the last home match (the snack bar). */
  readonly current: boolean;
}

/** One week of a building's books. */
export interface StructureWeek {
  /** The first day of the week, as `yyyy-MM-dd`. */
  readonly weekOf: string;
  readonly takingsMinor: number;

  /** A positive amount. */
  readonly upkeepMinor: number;
  readonly profitMinor: number;
}

/** One home match the snack bar served. */
export interface StructureMatch {
  readonly fixtureId: string;
  readonly opponent: string;
  readonly spectators: number;
  readonly result: 'win' | 'draw' | 'loss';
  readonly goalsFor: number;
  readonly goalsAgainst: number;
  readonly bonusBp: number;
  readonly bonusReason: string;
  readonly takingsMinor: number;
}

/** One of a club's buildings. */
export interface Structure {
  readonly kind: StructureKind;
  readonly level: number;
  readonly maxLevel: number;

  /** The strong entity tag an upgrade order is made against. */
  readonly version: number;

  /** What one spectator (snack bar) or one fan per week (shop) spends at this level. */
  readonly spendMinor: number;
  readonly upkeepWeeklyMinor: number;

  /** Null at the top level. */
  readonly next: StructureNextLevel | null;
  readonly building: StructureBuilding | null;
  readonly bonuses: readonly StructureBonus[];

  /** Newest first. */
  readonly recentWeeks: readonly StructureWeek[];

  /** Newest first. Empty for the shop. */
  readonly recentMatches: readonly StructureMatch[];
  readonly seasonTakingsMinor: number;

  /** A positive amount. */
  readonly seasonUpkeepMinor: number;
}

/** A club's snack bar and merchandise shop. */
export interface Structures {
  readonly availableCashMinor: number;
  readonly snackBar: Structure;
  readonly merchandise: Structure;
  readonly serverTime: string;
}
