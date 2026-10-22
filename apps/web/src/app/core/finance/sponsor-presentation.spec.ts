import { deal, offers } from './finance-test-data';
import {
  SEASON_OBJECTIVES,
  bonusFor,
  objectiveLabel,
  objectiveWithPlace,
  offerLabel,
  offerOf,
  ordinalOf,
  outcomeLabel,
  placeOrBetter,
  signedByLabel,
} from './sponsor-presentation';

/** The sponsor page's words. The server owns the codes and every amount. */
describe('sponsor presentation', () => {
  it('lists the objectives hardest first', () => {
    expect(SEASON_OBJECTIVES).toEqual([
      'win_league',
      'top_three',
      'top_six',
      'top_half',
      'twelfth',
      'stay_up',
    ]);
  });

  it('names an objective with the place it asks for', () => {
    expect(objectiveWithPlace('win_league', 1)).toBe('Win the league');
    expect(objectiveWithPlace('top_three', 3)).toBe('Finish in the top 3 (3rd or better)');
    expect(objectiveWithPlace('stay_up', 15)).toBe('Stay up (15th or better)');
  });

  it('falls back to the code for an objective it does not know', () => {
    expect(objectiveLabel('cup_run')).toBe('cup_run');
  });

  it.each([
    [1, '1st'],
    [2, '2nd'],
    [3, '3rd'],
    [4, '4th'],
    [11, '11th'],
    [12, '12th'],
    [13, '13th'],
    [21, '21st'],
    [22, '22nd'],
  ])('writes %i as %s', (value, text) => {
    expect(ordinalOf(value)).toBe(text);
  });

  it('names the offers and the place they ask for', () => {
    expect(offerLabel('steady')).toBe('Steady');
    expect(offerLabel('performance')).toBe('Performance');
    expect(placeOrBetter(6)).toBe('6th or better');
  });

  it('reads the bonus an offer pays at an objective, from the server quote', () => {
    const balanced = offerOf(offers(), 'balanced')!;

    expect(bonusFor(balanced, 'top_six')).toEqual({
      metMinor: 100_000_000,
      exceededMinor: 200_000_000,
    });
    expect(bonusFor(balanced, 'top_three')!.metMinor).toBe(200_000_000);
  });

  it('says who signed, and how the season ended against the objective', () => {
    expect(signedByLabel('manager')).toBe('You signed it');
    expect(signedByLabel('board')).toBe('The board signed it at the deadline');
    expect(outcomeLabel(deal())).toBeNull();
    expect(outcomeLabel(deal({ outcome: 'exceeded' }))).toBe('Objective exceeded');
    expect(outcomeLabel(deal({ outcome: 'missed' }))).toBe('Objective missed');
  });
});
