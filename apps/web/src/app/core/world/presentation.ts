/**
 * Client-side presentation helpers for onboarding.
 *
 * Small and mostly pure, so they can be unit tested without a component and so the same formatting is
 * used by every screen that shows a date, a deadline, or an amount.
 *
 * The manager's chosen locale and time zone are held in two module signals rather than passed through
 * every call site. Formatting is needed by a dozen features that do not otherwise know about the manager
 * profile, and threading a preference signal through all of them would put the same argument in every
 * template for no benefit. `configurePresentation` seeds it from the manager profile when it loads
 * (`CAL-4`).
 */
import { signal } from '@angular/core';

/** A locale-shaped tag: a language and a region, e.g. `en-GB`. */
const LOCALE_PATTERN = /^[a-z]{2,3}-[A-Z]{2}$/;

const configuredLocale = signal<string | null>(null);
const configuredTimeZone = signal<string | null>(null);

/**
 * Sets the manager's formatting preferences.
 *
 * Called when the manager profile loads, when the manager changes it, and cleared when the session ends.
 * A null value means "no preference", which falls back to the browser.
 */
export function configurePresentation(preferences: {
  readonly locale?: string | null;
  readonly timeZone?: string | null;
}): void {
  if (preferences.locale !== undefined) {
    configuredLocale.set(preferences.locale);
  }

  if (preferences.timeZone !== undefined) {
    configuredTimeZone.set(preferences.timeZone);
  }
}

/** Clears the configured preferences. Called when the session ends. */
export function resetPresentation(): void {
  configuredLocale.set(null);
  configuredTimeZone.set(null);
}

/**
 * The locale to format with.
 *
 * The manager's stored locale when there is one; otherwise the browser's, falling back to `en-GB` when
 * the browser offers only a bare language. The server rejects a tag without a region, so a stored value
 * always has the shape the formatter needs.
 */
export function preferredLocale(): string {
  const configured = configuredLocale();

  if (configured !== null && LOCALE_PATTERN.test(configured)) {
    return configured;
  }

  const language = typeof navigator === 'undefined' ? '' : navigator.language;

  return LOCALE_PATTERN.test(language) ? language : 'en-GB';
}

/**
 * The IANA time zone to render instants in.
 *
 * The manager's stored zone when there is one — travelling, or a device set to a different zone, must not
 * move every deadline (`CAL-4`) — otherwise the browser's.
 */
export function preferredTimeZone(): string {
  const configured = configuredTimeZone();

  if (configured !== null && configured.length > 0) {
    return configured;
  }

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

/**
 * Formats an instant in the manager's time zone (`CAL-4`).
 *
 * UTC stays the authority on the server; this only decides where the moment is shown. It renders the moment
 * compactly and without naming the zone, which is what a timestamp wants; a deadline, which is a moment a
 * manager acts against, is rendered by {@link formatDeadline} instead.
 */
export function formatInstant(
  instant: string,
  locale = preferredLocale(),
  timeZone = preferredTimeZone(),
): string {
  return new Intl.DateTimeFormat(locale, {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone,
  }).format(new Date(instant));
}

/**
 * Formats a deadline as an absolute moment in the manager's zone, naming that zone (`VOI-4`, `CAL-4`).
 *
 * A deadline is the one instant a manager acts against, so it is stated with the zone it falls in rather
 * than as a bare local time (`VOI-4`). That is also why the components are named explicitly rather than
 * reusing {@link formatInstant}: `Intl` refuses `timeZoneName` alongside `dateStyle`/`timeStyle`.
 */
export function formatDeadline(
  instant: string,
  locale = preferredLocale(),
  timeZone = preferredTimeZone(),
): string {
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZoneName: 'short',
    timeZone,
  }).format(new Date(instant));
}
