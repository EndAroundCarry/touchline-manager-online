import { categoryLabel, messageLink } from './inbox-presentation';

/**
 * The inbox's presentation helpers (`F-41`, `§11.1`).
 *
 * The point is that a category always reads as words and a message links to the thing it is about, so a
 * message is never a dead end and never only a colour.
 */
describe('inbox presentation', () => {
  it('names every category the server can send, and falls back to the code', () => {
    expect(categoryLabel('result')).toBe('Results');
    expect(categoryLabel('table')).toBe('Table');
    expect(categoryLabel('discipline')).toBe('Discipline');
    expect(categoryLabel('injury')).toBe('Injuries');
    expect(categoryLabel('squad')).toBe('Your squad');
    expect(categoryLabel('occupancy')).toBe('Your club');
    expect(categoryLabel('reminder')).toBe('Reminders');
    expect(categoryLabel('system')).toBe('Messages');
    expect(categoryLabel('future')).toBe('future');
  });

  it('links a message to the entity it is about', () => {
    expect(messageLink('result', 'match-1')).toBe('/matches/match-1');
    expect(messageLink('injury', 'player-1')).toBe('/players/player-1');
    expect(messageLink('discipline', 'player-2')).toBe('/players/player-2');
    expect(messageLink('squad', 'fixture-1')).toBe('/fixtures/fixture-1/prepare');
  });

  it('links nowhere when the message names no entity or a category with no destination', () => {
    expect(messageLink('table', 'anything')).toBeNull();
    expect(messageLink('result', null)).toBeNull();
    expect(messageLink('future', 'id')).toBeNull();
  });
});
