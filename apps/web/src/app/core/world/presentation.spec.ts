import { formatFunds, formatInstant, preferredLocale } from './presentation';

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
});
