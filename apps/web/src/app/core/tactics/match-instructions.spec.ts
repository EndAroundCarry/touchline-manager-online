import {
  DEFAULT_BAD_RATING,
  DEFAULT_TIRED_CONDITION,
  EVENT_CODES,
  MATCH_SUBSTITUTION_LIMIT,
  MAX_MATCH_INSTRUCTIONS,
  MAX_RULE_SUBSTITUTIONS,
  blankInstruction,
  blankSubstitution,
  hasUnplaceableIncoming,
  inertInstructions,
  instructionProblems,
  namedSwaps,
  placedButLeaving,
  plannedSubstitutions,
  withAddedSubstitution,
  withEvent,
  withFormationChoice,
  withMentality,
  withMentalitySteps,
  withOffSelector,
  withOnSelector,
  withPlacement,
  withoutOrphanedPlacements,
  withoutPlacements,
} from './match-instructions';
import {
  instructionIssueMessage,
  instructionSummary,
  isInstructionIssue,
} from './match-instructions-presentation';
import type { MatchInstruction } from './tactics.models';

/**
 * Match instructions as pure data: the edits, the templates and the checks that mirror the server's validator.
 *
 * These are the rules the editor leans on that a rendered page would reach only through many clicks: that changing the kind of event
 * clears what the old kind left behind, that a placement replaces rather than doubles up, that every template is a rule the server
 * would accept, and that the summary a card shows reads the way a manager would say it.
 */

const NO_NAMES: ReadonlyMap<string, string> = new Map();

const LINEUP = new Set(['p1', 'p2', 'p3']);
const BENCH = new Set(['b1', 'b2']);

function problemCodes(rules: readonly MatchInstruction[]): string[] {
  return instructionProblems(rules, LINEUP, BENCH).map((problem) => problem.code);
}

describe('withEvent', () => {
  it('clears the family and threshold the old kind used, and gives the new kind its default', () => {
    const booked: MatchInstruction = {
      ...blankInstruction(),
      event: EVENT_CODES.ownBooked,
      eventFamily: 'defence',
    };

    const badly = withEvent(booked, EVENT_CODES.playingBadly);

    expect(badly.eventFamily).toBeNull();
    expect(badly.eventThreshold).toBe(DEFAULT_BAD_RATING);
    expect(withEvent(badly, EVENT_CODES.tired).eventThreshold).toBe(DEFAULT_TIRED_CONDITION);

    const sentOff = withEvent(badly, EVENT_CODES.ownSentOff);

    expect(sentOff.eventThreshold).toBeNull();
    expect(sentOff.eventFamily).toBeNull();
  });

  it('gives an opponent-mentality event a mentality number the server accepts', () => {
    const atLeast = withEvent(blankInstruction(), EVENT_CODES.opponentMentalityAtLeast);
    const atMost = withEvent(blankInstruction(), EVENT_CODES.opponentMentalityAtMost);

    expect(problemCodes([atLeast, atMost])).toEqual([]);
    expect(atLeast.eventThreshold).toBeGreaterThan(atMost.eventThreshold ?? 99);
  });

  it('turns a take-off of "the player the event is about" back into a named player when the event has no subject', () => {
    const booked = withEvent(blankInstruction(), EVENT_CODES.ownBooked);
    const withSubject = withAddedSubstitution(booked);

    expect(withSubject.substitutions[0].off).toBe('subject');

    const sentOff = withEvent(withSubject, EVENT_CODES.ownSentOff);

    expect(sentOff.substitutions[0].off).toBe('player');
    expect(sentOff.substitutions[0].offFamily).toBeNull();
  });

  it('produces every event kind with parameters the validator accepts', () => {
    for (const event of Object.values(EVENT_CODES)) {
      expect(problemCodes([withEvent(blankInstruction(), event)]), event).toEqual([]);
    }
  });
});

describe('mentality edits', () => {
  it('lets an absolute mentality replace a step, and a step replace an absolute mentality', () => {
    const stepped = withMentalitySteps(blankInstruction(), -1);

    expect(stepped.changes.mentalitySteps).toBe(-1);

    const absolute = withMentality(stepped, 'attacking');

    expect(absolute.changes).toMatchObject({ mentality: 'attacking', mentalitySteps: 0 });
    expect(withMentalitySteps(absolute, 2).changes).toMatchObject({
      mentality: null,
      mentalitySteps: 2,
    });
  });
});

describe('substitution selectors', () => {
  it('clears what the old way of choosing used, and defaults the family a family selector needs', () => {
    const named = { ...blankSubstitution(), offPlayerId: 'p1', onPlayerId: 'b1' };
    const byFamily = withOnSelector(
      withOffSelector(named, 'most_tired_in_family'),
      'best_fit_in_family',
    );

    expect(byFamily).toMatchObject({
      off: 'most_tired_in_family',
      offPlayerId: null,
      offFamily: 'midfield',
      on: 'best_fit_in_family',
      onPlayerId: null,
      onFamily: 'attack',
    });

    const backToNames = withOnSelector(withOffSelector(byFamily, 'player'), 'player');

    expect(backToNames).toMatchObject({ offFamily: null, onFamily: null });
  });

  it('adds at most five substitutions to a rule', () => {
    let rule = blankInstruction();

    for (let count = 0; count < MAX_RULE_SUBSTITUTIONS + 2; count++) {
      rule = withAddedSubstitution(rule);
    }

    expect(rule.substitutions).toHaveLength(MAX_RULE_SUBSTITUTIONS);
  });
});

describe('placements in the new shape', () => {
  const base = { ...blankInstruction(), formation: '4-3-3' };

  it('gives a player and a slot one placement each, the newest winning', () => {
    const first = withPlacement(base, 'p1', 9);
    const moved = withPlacement(first, 'p1', 10);
    const taken = withPlacement(moved, 'p2', 10);

    expect(moved.overrides).toEqual([{ playerId: 'p1', slotNumber: 10 }]);
    expect(taken.overrides).toEqual([{ playerId: 'p2', slotNumber: 10 }]);
    expect(withPlacement(taken, 'p2', null).overrides).toEqual([]);
  });

  it('gives every placement back on a reset', () => {
    expect(withoutPlacements(withPlacement(base, 'p1', 9)).overrides).toEqual([]);
  });

  it('keeps the placements that still make sense when the formation changes, and drops them with no formation', () => {
    const placed = withPlacement(withPlacement(base, 'p1', 9), 'gone', 8);
    const moved = withFormationChoice(placed, '3-4-3', LINEUP);

    expect(moved.formation).toBe('3-4-3');
    expect(moved.overrides).toEqual([{ playerId: 'p1', slotNumber: 9 }]);
    expect(withFormationChoice(moved, null, LINEUP)).toMatchObject({
      formation: null,
      overrides: [],
    });
  });
});

describe('placing a substitute the instruction brings on', () => {
  const swap = {
    ...blankSubstitution(),
    offPlayerId: 'p1',
    onPlayerId: 'b1',
  };
  const bringsOn: MatchInstruction = {
    ...blankInstruction(),
    formation: '4-3-3',
    substitutions: [swap],
    overrides: [{ playerId: 'b1', slotNumber: 9 }],
  };

  it('accepts a placement of a bench player the same instruction brings on by name', () => {
    expect(problemCodes([bringsOn])).toEqual([]);
  });

  it('refuses a placement of a bench player the instruction does not bring on, or of a stranger', () => {
    expect(problemCodes([{ ...bringsOn, substitutions: [] }])).toEqual([
      'INSTRUCTION_OVERRIDE_INVALID',
    ]);
    expect(problemCodes([{ ...bringsOn, overrides: [{ playerId: 'b2', slotNumber: 9 }] }])).toEqual(
      ['INSTRUCTION_OVERRIDE_INVALID'],
    );
  });

  it('keeps his placement when the formation changes, and drops it when the instruction stops bringing him on', () => {
    expect(withFormationChoice(bringsOn, '3-4-3', LINEUP, BENCH).overrides).toEqual(
      bringsOn.overrides,
    );
    expect(withFormationChoice(bringsOn, '3-4-3', LINEUP).overrides).toEqual([]);
    expect(
      withoutOrphanedPlacements({ ...bringsOn, substitutions: [] }, LINEUP, BENCH).overrides,
    ).toEqual([]);
    expect(withoutOrphanedPlacements(bringsOn, LINEUP, BENCH)).toBe(bringsOn);
  });

  it('leaves a placement of a player who is neither a starter nor on the bench for the problem list to show', () => {
    const stranger = { ...bringsOn, overrides: [{ playerId: 'gone', slotNumber: 9 }] };

    expect(withoutOrphanedPlacements(stranger, LINEUP, BENCH)).toBe(stranger);
  });

  it('knows which substitutions name both players, and which bring a substitute on whom nobody can place yet', () => {
    expect(namedSwaps(bringsOn, LINEUP, BENCH)).toEqual([{ offPlayerId: 'p1', onPlayerId: 'b1' }]);
    expect(hasUnplaceableIncoming(bringsOn, BENCH)).toBe(false);

    const bySelector = {
      ...bringsOn,
      substitutions: [
        { ...swap, off: 'most_tired_in_family', offPlayerId: null, offFamily: 'midfield' },
      ],
    };

    expect(namedSwaps(bySelector, LINEUP, BENCH)).toEqual([]);
    expect(hasUnplaceableIncoming(bySelector, BENCH)).toBe(true);
  });

  it('finds a placed player the same instruction takes off by name', () => {
    expect(placedButLeaving(bringsOn)).toEqual([]);
    expect(
      placedButLeaving({ ...bringsOn, overrides: [{ playerId: 'p1', slotNumber: 9 }] }),
    ).toEqual(['p1']);
  });
});

describe('instructionProblems', () => {
  it('finds nothing wrong with a blank instruction', () => {
    expect(problemCodes([blankInstruction()])).toEqual([]);
  });

  it('flags a name, minutes and a margin the server would refuse', () => {
    const rules: MatchInstruction[] = [
      { ...blankInstruction(), name: '  ' },
      { ...blankInstruction(), fromMinute: 70, untilMinute: 60 },
      { ...blankInstruction(), fromMinute: 91 },
      { ...blankInstruction(), score: 'leading', scoreMargin: 6 },
    ];

    expect(instructionProblems(rules, LINEUP, BENCH).map((p) => [p.ordinal, p.code])).toEqual([
      [1, 'INSTRUCTION_NAME_INVALID'],
      [2, 'INSTRUCTION_MINUTE_RANGE'],
      [3, 'INSTRUCTION_MINUTE_RANGE'],
      [4, 'INSTRUCTION_SCORE_MARGIN'],
    ]);
  });

  it('flags an event with a parameter it does not take, or without one it needs', () => {
    expect(
      problemCodes([
        { ...blankInstruction(), event: EVENT_CODES.ownSentOff, eventThreshold: 5_000 },
        { ...blankInstruction(), event: EVENT_CODES.tired },
        { ...blankInstruction(), event: EVENT_CODES.playingBadly },
        { ...blankInstruction(), event: EVENT_CODES.opponentMentalityAtLeast, eventThreshold: 9 },
        { ...blankInstruction(), event: EVENT_CODES.ownBooked, eventThreshold: 100 },
      ]),
    ).toEqual(new Array(5).fill('INSTRUCTION_EVENT_PARAMETER'));
  });

  it('accepts a bad-play event that names only a family', () => {
    expect(
      problemCodes([
        {
          ...blankInstruction(),
          event: EVENT_CODES.playingBadly,
          eventThreshold: null,
          eventFamily: 'attack',
        },
      ]),
    ).toEqual([]);
  });

  it('flags a named player the plan does not field, and a selector with a missing part', () => {
    const rule: MatchInstruction = {
      ...blankInstruction(),
      substitutions: [
        { ...blankSubstitution(), offPlayerId: 'stranger', onPlayerId: 'b9' },
        blankSubstitution(),
        { ...blankSubstitution(), off: 'subject', on: 'best_fit_in_family' },
      ],
    };

    expect(instructionProblems([rule], LINEUP, BENCH).map((problem) => problem.code)).toEqual([
      'INSTRUCTION_OFF_PLAYER_NOT_IN_LINEUP',
      'INSTRUCTION_ON_PLAYER_NOT_ON_BENCH',
      'INSTRUCTION_SUBSTITUTION_SELECTOR',
      'INSTRUCTION_SUBSTITUTION_SELECTOR',
      'INSTRUCTION_SUBSTITUTION_SELECTOR',
      'INSTRUCTION_SUBSTITUTION_SELECTOR',
    ]);
  });

  it('flags the rule that takes the planned substitutions past five, once, and ignores rules that are off', () => {
    const three = {
      ...blankInstruction(),
      substitutions: Array.from({ length: 3 }, () => ({
        ...blankSubstitution(),
        offPlayerId: 'p1',
        onPlayerId: 'b1',
      })),
    };
    const rules = [three, { ...three, name: 'Second' }, { ...three, name: 'Third' }];

    expect(instructionProblems(rules, LINEUP, BENCH)).toEqual([
      { code: 'INSTRUCTIONS_SUBSTITUTION_LIMIT', ordinal: 2, playerId: null },
    ]);
    expect(plannedSubstitutions(rules)).toBe(9);
    expect(MATCH_SUBSTITUTION_LIMIT).toBe(5);

    const quietened = [three, { ...three, isEnabled: false }, { ...three, isEnabled: false }];

    expect(instructionProblems(quietened, LINEUP, BENCH)).toEqual([]);
  });

  it('flags placements with no new shape, for a stranger, for the keeper slot, or repeated', () => {
    const base = { ...blankInstruction(), formation: '4-3-3' };

    expect(
      problemCodes([{ ...blankInstruction(), overrides: [{ playerId: 'p1', slotNumber: 9 }] }]),
    ).toEqual(['INSTRUCTION_OVERRIDE_INVALID']);
    expect(
      problemCodes([
        {
          ...base,
          overrides: [
            { playerId: 'p1', slotNumber: 1 },
            { playerId: 'stranger', slotNumber: 9 },
            { playerId: 'p2', slotNumber: 10 },
            { playerId: 'p3', slotNumber: 10 },
          ],
        },
      ]),
    ).toEqual(new Array(3).fill('INSTRUCTION_OVERRIDE_INVALID'));
  });

  it('refuses more than ten instructions', () => {
    const rules = Array.from({ length: MAX_MATCH_INSTRUCTIONS + 1 }, () => blankInstruction());

    expect(problemCodes(rules)).toEqual(['TOO_MANY_INSTRUCTIONS']);
  });
});

describe('a new instruction', () => {
  it('is the blank rule: valid, with a name, and doing nothing yet', () => {
    const rule = blankInstruction();

    expect(instructionProblems([rule], LINEUP, BENCH)).toEqual([]);
    expect(rule.name.length).toBeGreaterThan(0);
    expect(inertInstructions([rule])).toEqual([1]);
  });

  it('reads a protect-the-lead rule as the manager would say it', () => {
    const rule: MatchInstruction = {
      ...blankInstruction('Protect a 2-goal lead'),
      fromMinute: 70,
      score: 'leading',
      scoreMargin: 2,
      changes: { ...blankInstruction().changes, mentalitySteps: -1, timeWasting: 'situational' },
    };

    expect(instructionSummary(rule, NO_NAMES)).toBe(
      "From 70', if leading by 2 or more → one step more defensive, time wasting situational",
    );
  });
});

describe('instructionSummary', () => {
  it('reads the example from the plan: losing from 60, a new shape, instructions and a named substitute', () => {
    const rule: MatchInstruction = {
      ...blankInstruction('Chase'),
      fromMinute: 60,
      score: 'trailing',
      scoreMargin: 1,
      formation: '4-2-4',
      changes: {
        ...blankInstruction().changes,
        mentality: 'attacking',
        pressing: 'high_press',
      },
      substitutions: [
        {
          off: 'most_tired_in_family',
          offPlayerId: null,
          offFamily: 'midfield',
          on: 'player',
          onPlayerId: 'b1',
          onFamily: null,
        },
      ],
    };

    expect(instructionSummary(rule, new Map([['b1', 'Okafor']]))).toBe(
      "From 60', if losing by 1 or more → 4-2-4, All-out attack, high press, Okafor on for the most tired midfielder",
    );
  });

  it('reads each kind of event', () => {
    const summary = (event: Partial<MatchInstruction>): string =>
      instructionSummary({ ...blankInstruction(), ...event }, NO_NAMES);

    expect(summary({})).toBe('From kick-off → changes nothing');
    expect(summary({ event: EVENT_CODES.ownSentOff })).toBe(
      'If one of your players is sent off → changes nothing',
    );
    expect(summary({ event: EVENT_CODES.opponentSentOff, fromMinute: 30, untilMinute: 80 })).toBe(
      "From 30' to 80', if an opponent is sent off → changes nothing",
    );
    expect(summary({ event: EVENT_CODES.ownBooked, eventFamily: 'defence' })).toBe(
      'If one of your defenders is booked → changes nothing',
    );
    expect(summary({ event: EVENT_CODES.playingBadly, eventThreshold: 5_700 })).toBe(
      'If a player is rated below 5.7 → changes nothing',
    );
    expect(
      summary({ event: EVENT_CODES.playingBadly, eventThreshold: null, eventFamily: 'attack' }),
    ).toBe('If the lowest-rated forward is playing badly → changes nothing');
    expect(summary({ event: EVENT_CODES.tired, eventThreshold: 6_000, untilMinute: 75 })).toBe(
      "Until 75', if a player's condition drops below 60% → changes nothing",
    );
    expect(summary({ event: EVENT_CODES.opponentMentalityAtLeast, eventThreshold: 3 })).toBe(
      "If the opponent's mentality is at least Attacking → changes nothing",
    );
    expect(summary({ score: 'not_losing', event: EVENT_CODES.none })).toBe(
      'If not losing → changes nothing',
    );
  });

  it('names the subject of a take-off after the event, and a fresh player of a family on the other side', () => {
    const rule: MatchInstruction = {
      ...blankInstruction(),
      event: EVENT_CODES.ownBooked,
      eventFamily: 'defence',
      substitutions: [
        {
          off: 'subject',
          offPlayerId: null,
          offFamily: null,
          on: 'best_fit_in_family',
          onPlayerId: null,
          onFamily: 'defence',
        },
      ],
    };

    expect(instructionSummary(rule, NO_NAMES)).toBe(
      'If one of your defenders is booked → a fresh defender on for the booked player',
    );
  });

  it('reads the counter-attack and the relative mentality', () => {
    const rule: MatchInstruction = {
      ...blankInstruction(),
      changes: { ...blankInstruction().changes, mentalitySteps: 2, counterAttack: false },
    };

    expect(instructionSummary(rule, NO_NAMES)).toBe(
      'From kick-off → two steps more attacking, counter-attack off',
    );
  });
});

describe('instruction issue wording', () => {
  it('recognises the issues that are about match instructions, and names a player when it can', () => {
    expect(isInstructionIssue('INSTRUCTION_MINUTE_RANGE')).toBe(true);
    expect(isInstructionIssue('INSTRUCTIONS_SUBSTITUTION_LIMIT')).toBe(true);
    expect(isInstructionIssue('TOO_MANY_INSTRUCTIONS')).toBe(true);
    expect(isInstructionIssue('DUPLICATE_PLAYER')).toBe(false);

    expect(
      instructionIssueMessage(
        { code: 'INSTRUCTION_OFF_PLAYER_NOT_IN_LINEUP', playerId: 'p1' },
        new Map([['p1', 'Okafor']]),
      ),
    ).toContain('Okafor');
  });
});
