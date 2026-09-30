import {
  configurePresentation,
  formatDeadline,
  formatFunds,
  formatInstant,
  preferredLocale,
  preferredTimeZone,
  resetPresentation,
} from './presentation';

/**
 * The presentation helpers onboarding depends on.
 *
 * The locale fallback is the one with a real failure behind it: the server rejects a tag without a region,
 * and `navigator.language` can legitimately be just `en`, so a manager whose browser reports a bare
 * language must not be handed a form their first click cannot submit.
 */
describe('preferredLocale', () => {
  it('keeps a language-and-region tag', () => {
    vi.stubGlobal('navigator', { language: 'ro-RO' });

    expect(preferredLocale()).toBe('ro-RO');
  });

  it('falls back when the browser reports a bare language', () => {
    vi.stubGlobal('navigator', { language: 'en' });

    expect(preferredLocale()).toBe('en-GB');
  });

  it('falls back when the browser reports nothing usable', () => {
    vi.stubGlobal('navigator', { language: '' });

    expect(preferredLocale()).toBe('en-GB');
  });
});

describe('formatFunds', () => {
  it('renders minor units as an amount with separators and two decimals', () => {
    // 50,000,000 minor units is the tier-1 opening balance (WorldRuleSet.OpeningCashMinorTier1).
    expect(formatFunds(50_000_000, 'en-GB')).toBe('500,000.00');
  });

  it('keeps the sign of a negative balance', () => {
    expect(formatFunds(-1_250, 'en-GB')).toBe('-12.50');
  });
});

describe('formatInstant', () => {
  it('renders an instant in the given locale', () => {
    // Deadlines are stored in UTC and only rendered locally (CAL-4).
    expect(formatInstant('2026-10-06T19:00:00Z', 'en-GB')).toContain('2026');
  });

  it('renders in the requested zone when one is passed', () => {
    const utc = formatInstant('2026-10-06T19:00:00Z', 'en-GB', 'UTC');
    const tokyo = formatInstant('2026-10-06T19:00:00Z', 'en-GB', 'Asia/Tokyo');

    // Noon in London is the next morning in Tokyo: the zone has to change the rendered time.
    expect(utc).not.toBe(tokyo);
  });
});

describe('formatDeadline', () => {
  afterEach(() => resetPresentation());

  it('states the moment and names its zone, unlike a plain instant', () => {
    // A deadline is the moment a manager acts against, so the zone is named beside it (VOI-4, CAL-4).
    const deadline = formatDeadline('2026-10-06T19:00:00Z', 'en-GB', 'Europe/Bucharest');

    expect(deadline).toContain('2026');
    expect(deadline).not.toBe(formatInstant('2026-10-06T19:00:00Z', 'en-GB', 'Europe/Bucharest'));
  });

  it('renders a different moment in a different zone', () => {
    const utc = formatDeadline('2026-10-06T19:00:00Z', 'en-GB', 'UTC');
    const tokyo = formatDeadline('2026-10-06T19:00:00Z', 'en-GB', 'Asia/Tokyo');

    expect(utc).not.toBe(tokyo);
  });

  it("uses the manager's configured zone and locale by default", () => {
    configurePresentation({ locale: 'en-GB', timeZone: 'Asia/Tokyo' });

    expect(formatDeadline('2026-10-06T19:00:00Z')).toBe(
      formatDeadline('2026-10-06T19:00:00Z', 'en-GB', 'Asia/Tokyo'),
    );
  });
});

describe('presentation preferences', () => {
  afterEach(() => resetPresentation());

  it("renders in the manager's chosen zone by default (CAL-4)", () => {
    configurePresentation({ locale: 'en-GB', timeZone: 'Asia/Tokyo' });

    expect(formatInstant('2026-10-06T19:00:00Z')).toBe(
      formatInstant('2026-10-06T19:00:00Z', 'en-GB', 'Asia/Tokyo'),
    );
  });

  it('uses the configured time zone as the default', () => {
    configurePresentation({ timeZone: 'Europe/Bucharest' });

    expect(preferredTimeZone()).toBe('Europe/Bucharest');
  });

  it('uses the configured locale for formatting', () => {
    configurePresentation({ locale: 'de-DE' });

    expect(preferredLocale()).toBe('de-DE');
  });

  it('falls back to the browser when no preference is configured', () => {
    configurePresentation({ timeZone: null, locale: null });

    expect(preferredTimeZone().length).toBeGreaterThan(0);
    expect(preferredLocale()).toMatch(/^[a-z]{2,3}-[A-Z]{2}$/);
  });

  it('ignores a configured locale that is not a language-and-region tag', () => {
    configurePresentation({ locale: 'en' });

    expect(preferredLocale()).toBe('en-GB');
  });
});
