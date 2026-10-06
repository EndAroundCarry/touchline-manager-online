import {
  fillPercent,
  levelProgress,
  levelRange,
  orderCost,
  placesPhrase,
  standLabel,
} from './stadium-presentation';
import { StadiumStand } from './stadium.models';

function stand(overrides: Partial<StadiumStand> = {}): StadiumStand {
  return {
    stand: 'standing',
    seats: 3_000,
    ticketPriceMinor: 800,
    buildCostMinor: 30_000,
    expectedSold: 3_000,
    ...overrides,
  };
}

describe('stadium presentation', () => {
  it('names every kind of place and falls back to the code for one it does not know', () => {
    expect(standLabel('standing').name).toBe('Standing');
    expect(standLabel('seating').name).toBe('Seating');
    expect(standLabel('covered_seating').name).toBe('Covered seating');
    expect(standLabel('vip').name).toBe('VIP');
    expect(standLabel('skybox').name).toBe('skybox');
  });

  it('says one place or several', () => {
    expect(placesPhrase('seating', 1)).toBe('1 seat');
    expect(placesPhrase('seating', 10)).toBe('10 seats');
    expect(placesPhrase('standing', 1)).toBe('1 standing place');
  });

  it('gives the places a level covers', () => {
    expect(levelRange(1, 5_000)).toEqual({ from: 1, to: 5_000 });
    expect(levelRange(2, 5_000)).toEqual({ from: 5_001, to: 10_000 });
    expect(levelRange(10, 5_000)).toEqual({ from: 45_001, to: 50_000 });
  });

  it('multiplies the quoted cost by the places wanted, and a bad count costs nothing', () => {
    expect(orderCost(stand(), 10)).toBe(300_000);
    expect(orderCost(stand(), 10.9)).toBe(300_000);
    expect(orderCost(stand(), 0)).toBe(0);
    expect(orderCost(stand(), -4)).toBe(0);
    expect(orderCost(stand(), Number.NaN)).toBe(0);
  });

  it('reports how full a stand gets, never over a hundred and never dividing by nothing', () => {
    expect(fillPercent(stand({ seats: 1_000, expectedSold: 250 }))).toBe(25);
    expect(fillPercent(stand({ seats: 1_000, expectedSold: 1_000 }))).toBe(100);
    expect(fillPercent(stand({ seats: 0, expectedSold: 0 }))).toBe(0);
  });

  it('shows progress through a level', () => {
    expect(levelProgress(5_000, 1, 5_000)).toBe(100);
    expect(levelProgress(2_500, 1, 5_000)).toBe(50);
    expect(levelProgress(5_001, 2, 5_000)).toBe(0);
    expect(levelProgress(7_500, 2, 5_000)).toBe(50);
    expect(levelProgress(50_000, 10, 5_000)).toBe(100);
  });
});
