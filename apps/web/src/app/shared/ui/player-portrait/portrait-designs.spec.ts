import {
  FALLBACK_KIT,
  PortraitKit,
  SvgNode,
  kitColour,
  portraitLook,
  portraitScene,
  portraitSeed,
} from './portrait-designs';

/**
 * The portrait generator: one face per player id, bent by age, in the club's colours.
 */

const KIT: PortraitKit = {
  primary: '#b11f2a',
  secondary: '#f3efe6',
  crestShape: 'round',
  crestPattern: 'stripes',
};

function flatten(node: SvgNode): SvgNode[] {
  return [node, ...(node.children ?? []).flatMap(flatten)];
}

const ids = Array.from(
  { length: 120 },
  (_, index) => `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`,
);

describe('portrait designs', () => {
  it('draws the same face for the same player and age every time', () => {
    const id = ids[3];

    expect(portraitScene(portraitLook(id, 24), KIT, 'a')).toEqual(
      portraitScene(portraitLook(id, 24), KIT, 'a'),
    );
    expect(portraitSeed(id)).toBe(portraitSeed(id));
  });

  it('gives a squad of players different skin tones, hair and face shapes', () => {
    const looks = ids.map((id) => portraitLook(id, 24));

    expect(new Set(looks.map((look) => look.skinIndex)).size).toBe(6);
    expect(new Set(looks.map((look) => look.hairStyle)).size).toBe(4);
    expect(new Set(looks.map((look) => look.hair)).size).toBeGreaterThanOrEqual(5);
    expect(new Set(looks.map((look) => look.geometry.jw.toFixed(0))).size).toBeGreaterThanOrEqual(
      8,
    );
  });

  it('ages a face without turning it into someone else', () => {
    for (const id of ids.slice(0, 20)) {
      const young = portraitLook(id, 19);
      const old = portraitLook(id, 36);

      expect(old.skinIndex).toBe(young.skinIndex);
      expect(old.hairStyle).toBe(young.hairStyle);
      expect(old.eye).toBe(young.eye);
      expect(old.geometry.eyeDx).toBe(young.geometry.eyeDx);
      expect(old.ageing).toBeGreaterThan(young.ageing);
      expect(old.geometry.hairline).toBeGreaterThan(young.geometry.hairline);
      expect(old.greying).toBeGreaterThanOrEqual(young.greying);
    }
  });

  it('keeps a teenager smooth and an older player lined', () => {
    const id = ids[7];
    const lineOpacity = (age: number): number =>
      flatten(portraitScene(portraitLook(id, age), KIT, 'a'))
        .filter(
          (node) =>
            node.tag === 'path' &&
            String(node.attrs['d']).includes('M') &&
            node.attrs['stroke-width'] === 2.4,
        )
        .map((node) => Number(node.attrs['opacity']))
        .reduce((sum, value) => sum + value, 0);

    expect(portraitLook(id, 18).ageing).toBe(0);
    expect(portraitLook(id, 18).youth).toBeGreaterThan(0);
    expect(lineOpacity(18)).toBe(0);
    expect(lineOpacity(35)).toBeGreaterThan(0.3);
  });

  it('gives a teenager a lighter beard than the same player at 25', () => {
    const bearded = ids.find((id) => portraitLook(id, 25).beard > 0.8);

    expect(bearded).toBeDefined();
    expect(portraitLook(bearded!, 17).beard).toBeLessThan(portraitLook(bearded!, 25).beard);
  });

  it('dresses the player in the club’s main colour with the second colour on the collar', () => {
    const nodes = flatten(portraitScene(portraitLook(ids[1], 24), KIT, 'a'));

    expect(nodes.some((node) => node.tag === 'path' && node.attrs['fill'] === '#b11f2a')).toBe(
      true,
    );
    expect(
      nodes.some(
        (node) =>
          node.tag === 'path' &&
          node.attrs['stroke'] === '#f3efe6' &&
          node.attrs['stroke-width'] === 7,
      ),
    ).toBe(true);
  });

  it('never lets a malformed colour through to the drawing', () => {
    const nodes = flatten(
      portraitScene(
        portraitLook(ids[1], 24),
        { ...KIT, primary: 'red" onload="x', secondary: '' },
        'a',
      ),
    );
    const colours = nodes.flatMap((node) => [node.attrs['fill'], node.attrs['stroke']]);

    expect(colours).toContain(FALLBACK_KIT.primary);
    expect(colours).toContain(FALLBACK_KIT.secondary);
    expect(colours.some((value) => String(value).includes('onload'))).toBe(false);
    expect(kitColour('#ABCDEF', '#000000')).toBe('#abcdef');
  });

  it('prefixes every id with the portrait’s key, so two portraits on a page never share one', () => {
    const nodes = flatten(portraitScene(portraitLook(ids[1], 24), KIT, 'k9'));
    const defined = nodes.map((node) => node.attrs['id']).filter((value) => value !== undefined);

    expect(defined.length).toBeGreaterThan(10);
    expect(defined.every((value) => String(value).startsWith('k9-'))).toBe(true);
    expect(new Set(defined).size).toBe(defined.length);
  });
});
