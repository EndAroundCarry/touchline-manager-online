import { sampleTrainingHistory } from './training-history';

describe('sampleTrainingHistory', () => {
  it('is stable for a player, so the placeholder does not change between visits', () => {
    expect(sampleTrainingHistory('player-1', 3)).toEqual(sampleTrainingHistory('player-1', 3));
  });

  it('lists the weeks newest first, each with every column worded', () => {
    const history = sampleTrainingHistory('player-1', 3, 6);

    expect(history.map((entry) => entry.week)).toEqual([6, 5, 4, 3, 2, 1]);

    for (const entry of history) {
      expect(entry.seasonNumber).toBe(3);
      expect(entry.teamFocus.length).toBeGreaterThan(0);
      expect(entry.individualFocus.length).toBeGreaterThan(0);
      expect(entry.intensity.length).toBeGreaterThan(0);
      expect(entry.outcome.length).toBeGreaterThan(0);
    }
  });
});
