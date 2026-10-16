import { MIN_BANNER_DISTANCE, bannerPalette, colourDistance, coloursOn } from './commentary-banner';

describe('commentary banner colours', () => {
  it('uses each side’s primary colour when the two are easy to tell apart', () => {
    const palette = bannerPalette('#1d4ed8', '#ffffff', '#d62828', '#000000');

    expect(palette.home.background).toBe('#1d4ed8');
    expect(palette.away.background).toBe('#d62828');
  });

  it('switches the away side to its secondary colour when the primaries are close', () => {
    const palette = bannerPalette('#d62828', '#ffffff', '#c81e1e', '#12284c');

    expect(palette.home.background).toBe('#d62828');
    expect(palette.away.background).toBe('#12284c');
    expect(colourDistance(palette.home.background, palette.away.background)).toBeGreaterThan(
      MIN_BANNER_DISTANCE,
    );
  });

  it('falls back to the home secondary when the away secondary is no help', () => {
    const palette = bannerPalette('#d62828', '#12284c', '#c81e1e', '#d02020');

    expect(palette.home.background).toBe('#12284c');
    expect(palette.away.background).toBe('#c81e1e');
  });

  it('keeps the furthest pairing when no pairing is far enough apart', () => {
    const palette = bannerPalette('#d62828', '#d02020', '#c81e1e', '#cc2222');
    const chosen = colourDistance(palette.home.background, palette.away.background);

    expect(chosen).toBeGreaterThanOrEqual(colourDistance('#d62828', '#c81e1e'));
  });

  it('writes white on dark backgrounds and black on white or other light ones', () => {
    expect(coloursOn('#12284c').ink).toBe('#ffffff');
    expect(coloursOn('#d62828').ink).toBe('#ffffff');
    expect(coloursOn('#ffffff').ink).toBe('#0f172a');
    expect(coloursOn('#fcd116').ink).toBe('#0f172a');
  });

  it('falls back to the neutral colours for a colour it cannot read', () => {
    expect(coloursOn('not-a-colour').background).toBe('#1e293b');
  });
});
