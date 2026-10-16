/** The colours one side's commentary banner is drawn in: a background and the ink that reads on it. */
export interface BannerColours {
  readonly background: string;
  readonly ink: string;
}

/** The banner colours of both sides, and the neutral ones for a line that belongs to neither. */
export interface BannerPalette {
  readonly home: BannerColours;
  readonly away: BannerColours;
  readonly neutral: BannerColours;
}

/** How far apart two backgrounds must be to tell the sides apart at a glance (redmean distance, 0 to ~765). */
export const MIN_BANNER_DISTANCE = 150;

/** Above this relative luminance a background is light enough that white ink would be lost on it. */
const LIGHT_BACKGROUND = 0.6;

const NEUTRAL: BannerColours = { background: '#1e293b', ink: '#ffffff' };

/**
 * Chooses the banner colours for the two sides.
 *
 * Each side plays in its kit's primary colour. When the two primaries are too close to tell apart, the
 * secondary of one side (the away side first, then the home side) stands in, so the manager can always see
 * whose line it is. When no pairing is far enough apart, the one that is furthest apart is used.
 */
export function bannerPalette(
  homePrimary: string,
  homeSecondary: string,
  awayPrimary: string,
  awaySecondary: string,
): BannerPalette {
  const pairs: readonly (readonly [string, string])[] = [
    [homePrimary, awayPrimary],
    [homePrimary, awaySecondary],
    [homeSecondary, awayPrimary],
    [homeSecondary, awaySecondary],
  ];

  let best = pairs[0];
  let bestDistance = -1;

  for (const pair of pairs) {
    const apart = colourDistance(pair[0], pair[1]);

    if (apart >= MIN_BANNER_DISTANCE) {
      best = pair;
      break;
    }

    if (apart > bestDistance) {
      best = pair;
      bestDistance = apart;
    }
  }

  return {
    home: coloursOn(best[0]),
    away: coloursOn(best[1]),
    neutral: NEUTRAL,
  };
}

/** The background and the readable ink for it: white, or near-black where the background is light. */
export function coloursOn(background: string): BannerColours {
  const rgb = parseHex(background);

  if (rgb === null) {
    return NEUTRAL;
  }

  const luminance = (0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]) / 255;

  return { background, ink: luminance > LIGHT_BACKGROUND ? '#0f172a' : '#ffffff' };
}

/** A perceptual distance between two colours (the "redmean" approximation); an unreadable colour is far from all. */
export function colourDistance(first: string, second: string): number {
  const one = parseHex(first);
  const other = parseHex(second);

  if (one === null || other === null) {
    return Number.POSITIVE_INFINITY;
  }

  const red = (one[0] + other[0]) / 2;
  const deltaRed = one[0] - other[0];
  const deltaGreen = one[1] - other[1];
  const deltaBlue = one[2] - other[2];

  return Math.sqrt(
    (2 + red / 256) * deltaRed * deltaRed +
      4 * deltaGreen * deltaGreen +
      (2 + (255 - red) / 256) * deltaBlue * deltaBlue,
  );
}

function parseHex(colour: string): readonly [number, number, number] | null {
  const match = /^#?([0-9a-f]{6})$/i.exec(colour.trim());

  if (match === null) {
    return null;
  }

  const value = Number.parseInt(match[1], 16);

  return [(value >> 16) & 255, (value >> 8) & 255, value & 255];
}
