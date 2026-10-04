/** One item in the division news feed (`COM-1`, master plan §10.7). */
export interface NewsItem {
  readonly id: string;
  readonly category: string;
  readonly countryId: string | null;
  readonly divisionId: string | null;
  readonly title: string;
  readonly body: string;
  readonly publishedAt: string;
  /** What the title and body hold back because it gives a match away: the score. Null when nothing is held back. */
  readonly spoiler: string | null;
  /** The match a result item reports, which remembers a shown result under it. Null when it names none. */
  readonly matchId: string | null;
}

/** One page of the news feed. */
export interface NewsPage {
  readonly items: readonly NewsItem[];
  readonly nextCursor: string | null;
  readonly serverTime: string;
}
