/**
 * Client-side presentation helpers for onboarding.
 *
 * Small and pure, so they can be unit tested without a component and so the same formatting is used by
 * every screen that shows a date, a deadline, or an amount.
 */

/** A locale-shaped tag: a language and a region, e.g. `en-GB`. */
const LOCALE_PATTERN = /^[a-z]{2,3}-[A-Z]{2}$/;

/**
 * The locale to offer as the manager's default.
 *
 * The server rejects a tag without a region, and `navigator.language` can legitimately be just `en`, so a
 * bare language falls back rather than producing a form the manager has to fix before their first click.
 */
export function preferredLocale(): string {
  const language = typeof navigator === 'undefined' ? '' : navigator.language;

  return LOCALE_PATTERN.test(language) ? language : 'en-GB';
}

/** The IANA time zone to offer as the manager's default. */
export function preferredTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
  } catch {
    // A runtime without a time-zone database can still be used; every deadline is stored in UTC and only
    // rendered locally, so falling back to UTC shows the right moment in the wrong place rather than the
    // wrong moment.
    return 'UTC';
  }
}

/**
 * Formats an amount for display.
 *
 * Amounts travel as integer minor units (`FIN-1`) and are only divided here, for presentation. The
 * arithmetic that matters — balances, bids, wages — stays in whole minor units on the server.
 */
export function formatFunds(minorUnits: number, locale = preferredLocale()): string {
  return new Intl.NumberFormat(locale, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(minorUnits / 100);
}

/** Formats an instant in the viewer's local time, with the zone named (`VOI-4`). */
export function formatInstant(instant: string, locale = preferredLocale()): string {
  return new Intl.DateTimeFormat(locale, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(instant));
}
