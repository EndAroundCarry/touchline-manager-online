import {
  TEAM_PLAN,
  effectiveDateLabel,
  focusFamilyLabel,
  focusOptions,
  intensityLabel,
  intensityOptions,
  teamFocusLabel,
  teamFocusOptions,
} from './training-presentation';

/**
 * The training presentation helpers.
 *
 * These are what turn the server's stable codes into words a manager reads. The important behaviours are
 * that every code the server can send has a label, that an unknown code falls back to itself rather than
 * showing a blank, and that the individual-focus select offers the "team plan" clear first — the empty
 * value that sends a null family (`TRN-2`).
 */
describe('training presentation', () => {
  describe('labels', () => {
    it('names every focus and intensity the server can send, and falls back to the code', () => {
      expect(teamFocusLabel('balanced')).toBe('Balanced');
      expect(teamFocusLabel('recovery')).toBe('Recovery');
      expect(teamFocusLabel('tactical')).toBe('Tactical');
      expect(teamFocusLabel('pressing')).toBe('pressing');

      expect(intensityLabel('light')).toBe('Light');
      expect(intensityLabel('intense')).toBe('Intense');
      expect(intensityLabel('brutal')).toBe('brutal');

      expect(focusFamilyLabel('technical')).toBe('Technical');
      expect(focusFamilyLabel('goalkeeping')).toBe('Goalkeeping');
      expect(focusFamilyLabel('setpieces')).toBe('setpieces');
    });
  });

  describe('options', () => {
    it('labels the focus codes in the order the server sent them', () => {
      expect(teamFocusOptions(['balanced', 'fitness', 'tactical'])).toEqual([
        { value: 'balanced', label: 'Balanced' },
        { value: 'fitness', label: 'Fitness' },
        { value: 'tactical', label: 'Tactical' },
      ]);

      expect(
        intensityOptions(['light', 'normal', 'intense']).map((option) => option.label),
      ).toEqual(['Light', 'Normal', 'Intense']);
    });

    it('puts the team plan first, as the empty value that clears a focus', () => {
      const options = focusOptions(['technical', 'mental']);

      expect(options[0]).toEqual({ value: TEAM_PLAN, label: 'Team plan' });
      expect(options.map((option) => option.value)).toEqual(['', 'technical', 'mental']);
    });
  });

  describe('effectiveDateLabel', () => {
    it('renders the plan date in the viewer locale', () => {
      expect(effectiveDateLabel('2026-09-25', 'en-GB')).toBe('25 September 2026');
    });

    it('shows the raw value rather than an invalid date', () => {
      expect(effectiveDateLabel('not-a-date', 'en-GB')).toBe('not-a-date');
    });
  });
});
