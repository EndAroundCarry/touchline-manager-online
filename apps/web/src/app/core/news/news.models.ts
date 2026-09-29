/** One item in the division news feed (`COM-1`, master plan §10.7). */
export interface NewsItem {
  readonly id: string;
  readonly category: string;
  readonly countryId: string | null;
  readonly divisionId: string | null;
  readonly title: string;
  readonly body: string;
  readonly publishedAt: string;
}

/** One page of the news feed. */
export interface NewsPage {
  readonly items: readonly NewsItem[];
  readonly nextCursor: string | null;
  readonly serverTime: string;
}
