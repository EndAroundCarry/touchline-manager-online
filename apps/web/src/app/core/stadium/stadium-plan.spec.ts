import {
  AISLE,
  APRON,
  LEVELS,
  PITCH,
  SECTOR_WIDTH,
  SIDES,
  StandPlan,
  StandSide,
  clampLevel,
  depthOf,
  heightOf,
  kindsOf,
  planOf,
  sectorsOf,
  spansOf,
} from './stadium-plan';

/**
 * The ten stadium levels as a plan.
 *
 * What matters is that a level is always the one before it with something added (so a manager watches one ground
 * grow), that every level has a region of every kind of place the club owns (places are never removed, and every club
 * opens with all four kinds), and that a stand's sectors and roof cut cleanly.
 */

const ALL_LEVELS = Array.from({ length: LEVELS }, (_, index) => index + 1);

describe('stadium plan', () => {
  it('has ten levels', () => {
    expect(LEVELS).toBe(10);
  });

  it('clamps a level it has no plan for rather than drawing nothing', () => {
    expect(clampLevel(0)).toBe(1);
    expect(clampLevel(99)).toBe(10);
    expect(clampLevel(Number.NaN)).toBe(1);
    expect(clampLevel(3.6)).toBe(4);
    expect(planOf(0)).toBe(planOf(1));
    expect(planOf(99)).toBe(planOf(10));
  });

  it('draws every kind of place the club owns at every level', () => {
    for (const level of ALL_LEVELS) {
      expect(kindsOf(level), `level ${level}`).toEqual([
        'standing',
        'seating',
        'covered_seating',
        'vip',
      ]);
    }
  });

  it('only ever adds to a stand as the level rises', () => {
    for (const level of ALL_LEVELS.slice(1)) {
      const before = planOf(level - 1);
      const after = planOf(level);

      for (const side of SIDES) {
        const was: StandPlan = before[side];
        const now: StandPlan = after[side];

        expect(now.lower, `${side} at ${level}`).toBeGreaterThanOrEqual(was.lower);
        expect(now.upper).toBeGreaterThanOrEqual(was.upper);
        expect(now.roof).toBeGreaterThanOrEqual(was.roof);

        // A stand that is built stays what it is: a terrace does not become seats, and the boxes do not go.
        if (was.lower > 0) {
          expect(now.lowerStanding).toBe(was.lowerStanding);
        }

        if (was.vip) {
          expect(now.vip).toBe(true);
        }
      }

      expect(after.masts).toBeGreaterThanOrEqual(before.masts);

      if (before.ring) {
        expect(after.ring).toBe(true);
      }

      if (before.flag) {
        expect(after.flag).toBe(true);
      }
    }
  });

  it('builds more at every level than at the one before', () => {
    // The room the rows take: each stand's length times its rows.
    const rows = (level: number): number =>
      spansOf(planOf(level)).reduce(
        (total, span) => total + (span.to - span.from) * (span.plan.lower + span.plan.upper),
        0,
      );

    for (const level of ALL_LEVELS.slice(1)) {
      expect(rows(level), `level ${level}`).toBeGreaterThan(rows(level - 1));
    }
  });

  it('starts as a village ground and ends as a closed two-tier bowl', () => {
    const first = planOf(1);
    const last = planOf(10);

    expect(spansOf(first).map((span) => span.side)).toEqual(['main', 'opposite']);
    expect(first.masts).toBe(0);
    expect(first.ring).toBe(false);
    expect(first.flag).toBe(false);

    expect(spansOf(last)).toHaveLength(4);

    for (const side of SIDES) {
      expect(last[side].lower).toBe(7);
      expect(last[side].upper).toBe(5);
    }

    expect(last.masts).toBe(6);
    expect(last.ring).toBe(true);
    expect(last.flag).toBe(true);
  });

  it('roofs only seats: a terrace has a roof only over the seated tier above it', () => {
    for (const level of ALL_LEVELS) {
      for (const side of SIDES) {
        const stand = planOf(level)[side];

        if (stand.lowerStanding && stand.roof > 0) {
          expect(stand.upper, `${side} at level ${level}`).toBeGreaterThan(0);
        }
      }
    }
  });

  it('puts the floodlights up at level three', () => {
    expect(planOf(2).masts).toBe(0);
    expect(planOf(3).masts).toBe(2);
  });

  it('measures a stand: its depth and its height', () => {
    const none = planOf(1).east;
    const one = planOf(1).main;
    const two = planOf(10).main;

    expect(depthOf(none)).toBe(0);
    expect(heightOf(none)).toBe(0);
    expect(depthOf(two)).toBeGreaterThan(depthOf(one));
    expect(heightOf(two)).toBeGreaterThan(heightOf(one));
  });

  it('lays the long stands past the pitch to close the corners beside an end stand', () => {
    const spans = spansOf(planOf(10));
    const side = (name: StandSide) => spans.find((span) => span.side === name)!;

    expect(side('main').from).toBe(-APRON - depthOf(planOf(10).west));
    expect(side('main').to).toBe(PITCH.width + APRON + depthOf(planOf(10).east));
    expect(side('east').from).toBe(-APRON);
    expect(side('east').to).toBe(PITCH.height + APRON);

    // With no end stands the long ones stop at the apron.
    expect(spansOf(planOf(1))[0].from).toBe(-APRON);
    expect(spansOf(planOf(1))[0].to).toBe(PITCH.width + APRON);
  });
});

describe('sectors', () => {
  it('cuts a stand into sectors of about the width aimed for, split by aisles', () => {
    const sectors = sectorsOf(0, 340, 0);

    expect(sectors.length).toBe(Math.round(340 / SECTOR_WIDTH));

    for (const sector of sectors) {
      expect(sector.to - sector.from).toBeGreaterThan(0);
    }

    for (let index = 1; index < sectors.length; index++) {
      expect(sectors[index].from - sectors[index - 1].to).toBeCloseTo(AISLE, 6);
    }

    expect(sectors[0].from).toBe(0);
    expect(sectors[sectors.length - 1].to).toBeCloseTo(340, 6);
  });

  it('gives a short stand at least two sectors', () => {
    expect(sectorsOf(0, 20, 0).length).toBe(2);
  });

  it('covers no sector with no roof and every sector with a full one', () => {
    expect(sectorsOf(0, 300, 0).some((sector) => sector.covered)).toBe(false);
    expect(sectorsOf(0, 300, 1).every((sector) => sector.covered)).toBe(true);
  });

  it('roofs the middle of the stand in one run, and leaves the ends open', () => {
    const sectors = sectorsOf(0, 300, 0.5);
    const covered = sectors.map((sector) => sector.covered);

    expect(covered.some(Boolean)).toBe(true);
    expect(covered[0]).toBe(false);
    expect(covered[covered.length - 1]).toBe(false);

    const first = covered.indexOf(true);
    const last = covered.lastIndexOf(true);

    expect(covered.slice(first, last + 1).every(Boolean)).toBe(true);
  });

  it('never loses a small roof to rounding', () => {
    expect(sectorsOf(0, 200, 0.05).filter((sector) => sector.covered)).toHaveLength(1);
  });
});
