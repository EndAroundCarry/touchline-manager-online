import type { PlayerAttributes } from '../squad/squad.models';
import type { Training, TrainingPlayer, TrainingProgramme } from './training.models';

/**
 * Builders for the training specs: a small programme catalogue, a player with a full attribute set, and a
 * training read. Shared by the store, presentation, and screen specs so each states only what it varies.
 * Not part of the application: nothing outside a spec imports it.
 */

/** A set of twenty-eight attributes where every value is `value` unless overridden family by family. */
export function attributes(
  value: number,
  overrides: { [F in keyof PlayerAttributes]?: Partial<PlayerAttributes[F]> } = {},
): PlayerAttributes {
  return {
    technical: {
      finishing: value,
      passing: value,
      crossing: value,
      dribbling: value,
      firstTouch: value,
      tackling: value,
      marking: value,
      heading: value,
      technique: value,
      setPieces: value,
      ...overrides.technical,
    },
    mental: {
      decisions: value,
      vision: value,
      positioning: value,
      composure: value,
      anticipation: value,
      workRate: value,
      aggression: value,
      leadership: value,
      ...overrides.mental,
    },
    physical: {
      pace: value,
      acceleration: value,
      stamina: value,
      strength: value,
      agility: value,
      jumpingReach: value,
      ...overrides.physical,
    },
    goalkeeping: {
      handling: value,
      reflexes: value,
      oneOnOnes: value,
      aerialAbility: value,
      ...overrides.goalkeeping,
    },
  };
}

/** A three-programme catalogue: one goalkeeper, one striker programme with all three weights, and recovery. */
export const PROGRAMMES: readonly TrainingProgramme[] = [
  {
    code: 'goalkeeper',
    label: 'Goalkeeper',
    description: 'For goalkeepers.',
    attributes: [
      { name: 'handling', family: 'goalkeeping', weight: 3 },
      { name: 'reflexes', family: 'goalkeeping', weight: 3 },
    ],
  },
  {
    code: 'forward',
    label: 'Forward',
    description: 'For strikers.',
    attributes: [
      { name: 'finishing', family: 'technical', weight: 3 },
      { name: 'composure', family: 'mental', weight: 2 },
      { name: 'pace', family: 'physical', weight: 1 },
    ],
  },
  { code: 'recovery', label: 'Recovery', description: 'Rest and recover.', attributes: [] },
];

/** A striker on the position default, with finishing 17, composure 5 and pace 10. */
export function player(overrides: Partial<TrainingPlayer> = {}): TrainingPlayer {
  return {
    id: 'p1',
    fullName: 'Alaric Alderwick',
    shortName: 'ALD',
    primaryPosition: 'st',
    positionFamily: 'attack',
    age: 24,
    state: { condition: 100, fatigue: 0, morale: 50, matchSharpness: 50 },
    attributes: attributes(10, { technical: { finishing: 17 }, mental: { composure: 5 } }),
    attributeProgress: {},
    programme: 'forward',
    isDefaultProgramme: true,
    defaultProgramme: 'forward',
    focusVersion: null,
    ...overrides,
  };
}

/** A training read for an unconfigured club with a striker and a goalkeeper who has chosen recovery. */
export function training(overrides: Partial<Training> = {}): Training {
  return {
    clubId: 'club-1',
    clubName: 'Ashvale United',
    clubShortName: 'ASH',
    countryCode: 'ENG',
    seasonNumber: 1,
    intensity: 'normal',
    effectiveDate: '2026-09-01',
    version: 0,
    isConfigured: false,
    intensityOptions: ['light', 'normal', 'intense'],
    programmes: PROGRAMMES,
    players: [
      player(),
      player({
        id: 'p2',
        fullName: 'Bramwell Brambleby',
        shortName: 'BRA',
        primaryPosition: 'gk',
        positionFamily: 'goalkeeper',
        programme: 'recovery',
        isDefaultProgramme: false,
        defaultProgramme: 'goalkeeper',
        focusVersion: 7,
      }),
    ],
    serverTime: '2026-09-25T00:00:00Z',
    ...overrides,
  };
}
