const CATEGORY_LABELS: Record<string, string> = {
  division: 'Division',
  transfer: 'Transfer',
  result: 'Result',
};

/** Names the kind of event a news item reports, falling back to the code. */
export function newsCategoryLabel(code: string): string {
  return CATEGORY_LABELS[code] ?? code;
}
