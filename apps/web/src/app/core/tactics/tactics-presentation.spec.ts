import {
  COUNTER_ATTACK,
  INSTRUCTION_FIELDS,
  benchPlacementRefusal,
  familyLabel,
  instructionEffect,
  isKeeperPlace,
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
    it('lists every instruction with its allowed values (INS-1..INS-8 and the pass focus)', () => {
      expect(INSTRUCTION_FIELDS.map((field) => field.key)).toEqual([
        'mentality',
        'tempo',
        'passing',
        'width',
        'passFocus',
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

  describe('the counter-attack switch', () => {
    it('says what it does and what it costs', () => {
      expect(COUNTER_ATTACK.label).toBe('Counter-attack');
      expect(COUNTER_ATTACK.effect).toContain('one in two');
      expect(COUNTER_ATTACK.effect).toContain('backfires');
    });
  });

  describe('the pass focus', () => {
    const field = INSTRUCTION_FIELDS.find((candidate) => candidate.key === 'passFocus')!;

    it('offers the centre, each flank with the centre, and the wings, beside balanced', () => {
      expect(field.options.map((option) => option.value)).toEqual([
        'balanced',
        'centre',
        'centre_left',
        'centre_right',
        'wings',
      ]);
    });

    it('says in one line what each choice does, so the manager is not choosing blind', () => {
      for (const option of field.options) {
        const line = instructionEffect(field, option.value);

        expect(line, option.value).not.toBeNull();
        expect(line!.length, option.value).toBeLessThan(140);
        expect(line, option.value).not.toMatch(/\n/);
      }
    });

    it('names the price of every choice that has one', () => {
      for (const value of ['centre', 'centre_left', 'centre_right', 'wings']) {
        expect(instructionEffect(field, value), value).toContain('but');
      }
    });

    it('names the flank that is left thin', () => {
      expect(instructionEffect(field, 'centre_left')).toContain('right flank');
      expect(instructionEffect(field, 'centre_right')).toContain('left flank');
    });

    it('has no line for a value it does not know, and none for an instruction without one', () => {
      expect(instructionEffect(field, 'sideways')).toBeNull();

      const tempo = INSTRUCTION_FIELDS.find((candidate) => candidate.key === 'tempo')!;

      expect(instructionEffect(tempo, 'high')).toBeNull();
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

    it('explains the bench rules in their own words, not the generic refusal', () => {
      const generic = issueMessage(issue('NOT_A_CODE'), names);

      expect(issueMessage(issue('BENCH_INCOMPLETE'), names)).toContain('all seven substitutes');
      expect(issueMessage(issue('BENCH_NEEDS_GOALKEEPER'), names)).toContain('goalkeeper');
      expect(issueMessage(issue('BENCH_SLOT_NUMBER', 20), names)).not.toBe(generic);
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
        'BENCH_SLOT_NUMBER',
        'BENCH_INCOMPLETE',
        'BENCH_NEEDS_GOALKEEPER',
      ];

      for (const code of codes) {
        expect(issueMessage(issue(code, 1, 'p1'), names).length).toBeGreaterThan(0);
      }
    });
  });

  describe('the bench', () => {
    it('keeps the first substitute place for a goalkeeper', () => {
      expect(isKeeperPlace(12)).toBe(true);
      expect(isKeeperPlace(13)).toBe(false);
      expect(benchPlacementRefusal(12, 'gk')).toBeNull();
      expect(benchPlacementRefusal(12, 'cb')).toContain('goalkeeper');
    });

    it('lets the other six places take anyone, a goalkeeper included', () => {
      for (const slot of [13, 14, 15, 16, 17, 18]) {
        expect(benchPlacementRefusal(slot, 'st')).toBeNull();
        expect(benchPlacementRefusal(slot, 'gk')).toBeNull();
      }
    });

    it('does not judge the starting places', () => {
      expect(benchPlacementRefusal(1, 'st')).toBeNull();
      expect(benchPlacementRefusal(9, 'gk')).toBeNull();
    });
  });
});
