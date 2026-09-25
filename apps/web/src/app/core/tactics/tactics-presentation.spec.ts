import {
  INSTRUCTION_FIELDS,
  clampPitchCoordinate,
  familyLabel,
  issueMessage,
  pitchStyle,
  roleLabel,
  rolesForFamily,
} from './tactics-presentation';
import { TacticalPlanIssue } from './tactics.models';

/**
 * The tactics presentation helpers.
 *
 * The two things worth pinning here are the ones a screen reader and a keyboard depend on: that every
 * stable code has a word, and that a validation issue is described with its slot or player rather than as
 * an opaque code. The pitch geometry is pinned because the numbers the engine will hash are the numbers
 * the board draws.
 */

const names = new Map([['p1', 'Alaric Alderwick']]);

function issue(
  code: string,
  slotNumber: number | null = null,
  playerId: string | null = null,
): TacticalPlanIssue {
  return { code, slotNumber, playerId };
}

describe('tactics presentation', () => {
  describe('labels', () => {
    it('names every family and role, and falls back to the code rather than blank', () => {
      expect(familyLabel('goalkeeper')).toBe('Goalkeeper');
      expect(familyLabel('defence')).toBe('Defence');
      expect(familyLabel('midfield')).toBe('Midfield');
      expect(familyLabel('attack')).toBe('Attack');
      expect(familyLabel('sweeper')).toBe('sweeper');

      expect(roleLabel('centre_back')).toBe('Centre back');
      expect(roleLabel('wing_back')).toBe('Wing back');
      expect(roleLabel('unknown_role')).toBe('unknown_role');
    });

    it('offers only the roles a family may name (TAC-8)', () => {
      expect(rolesForFamily('goalkeeper').map((option) => option.value)).toEqual(['goalkeeper']);
      expect(rolesForFamily('defence').map((option) => option.value)).toEqual([
        'centre_back',
        'full_back',
        'wing_back',
      ]);
      expect(rolesForFamily('midfield')).toHaveLength(3);
      expect(rolesForFamily('attack').map((option) => option.value)).toEqual(['winger', 'striker']);
      expect(rolesForFamily('sweeper')).toEqual([]);
    });
  });

  describe('the instruction pickers', () => {
    it('lists all eight instructions with their allowed values (INS-1..INS-8)', () => {
      expect(INSTRUCTION_FIELDS.map((field) => field.key)).toEqual([
        'mentality',
        'tempo',
        'passing',
        'width',
        'pressing',
        'defensiveLine',
        'tackling',
        'timeWasting',
      ]);

      expect(INSTRUCTION_FIELDS.every((field) => field.label.length > 0)).toBe(true);
      expect(INSTRUCTION_FIELDS.every((field) => field.options.length >= 2)).toBe(true);

      const mentality = INSTRUCTION_FIELDS.find((field) => field.key === 'mentality');

      expect(mentality?.options.map((option) => option.value)).toEqual([
        'defensive',
        'cautious',
        'balanced',
        'positive',
        'attacking',
      ]);
    });
  });

  describe('pitch geometry', () => {
    it('maps normalized depth to the vertical axis and width to the horizontal (TAC-9)', () => {
      expect(pitchStyle({ normalizedX: 500, normalizedY: 5_000 })).toEqual({
        bottom: '5.00%',
        left: '50.00%',
      });
      expect(pitchStyle({ normalizedX: 10_000, normalizedY: 0 })).toEqual({
        bottom: '100.00%',
        left: '0.00%',
      });
    });

    it('clamps a dropped position back inside the pitch, rounding to a whole unit', () => {
      expect(clampPitchCoordinate(-50)).toBe(0);
      expect(clampPitchCoordinate(10_500)).toBe(10_000);
      expect(clampPitchCoordinate(1_234.6)).toBe(1_235);
      expect(clampPitchCoordinate(Number.NaN)).toBe(0);
    });
  });

  describe('validation messages', () => {
    it('names a player for a duplicate or unavailable pick', () => {
      expect(issueMessage(issue('DUPLICATE_PLAYER', 4, 'p1'), names)).toContain('Alaric Alderwick');
      expect(issueMessage(issue('PLAYER_UNAVAILABLE', 4, 'p1'), names)).toContain(
        'injured or suspended',
      );
      expect(issueMessage(issue('PLAYER_NOT_ELIGIBLE', 4, 'p9'), names)).toContain('that player');
    });

    it('names the slot for a layout problem', () => {
      expect(issueMessage(issue('COORDINATE_OUT_OF_BOUNDS', 7), names)).toContain('Slot 7');
      expect(issueMessage(issue('OVERLAPPING_SLOTS', 3), names)).toContain('Slot 3');
      expect(issueMessage(issue('ROLE_FAMILY_MISMATCH', 2), names)).toContain(
        'role does not match',
      );
    });

    it('explains the pitch-wide rules without a slot', () => {
      expect(issueMessage(issue('SELECTION_INCOMPLETE'), names)).toContain('all eleven players');
      expect(issueMessage(issue('SLOT_COUNT'), names)).toContain('eleven slots');
    });

    it('has words for every code the validator can raise', () => {
      const codes = [
        'SLOT_COUNT',
        'SLOT_NUMBER',
        'DUPLICATE_SLOT_NUMBER',
        'COORDINATE_OUT_OF_BOUNDS',
        'OVERLAPPING_SLOTS',
        'ROLE_FAMILY_MISMATCH',
        'DUPLICATE_PLAYER',
        'PLAYER_NOT_ELIGIBLE',
        'PLAYER_UNAVAILABLE',
        'SELECTION_INCOMPLETE',
      ];

      for (const code of codes) {
        expect(issueMessage(issue(code, 1, 'p1'), names).length).toBeGreaterThan(0);
      }
    });
  });
});
