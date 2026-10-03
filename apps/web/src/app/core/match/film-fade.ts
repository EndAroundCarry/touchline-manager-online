import { FilmCut } from './film-timeline';
import { PlaylistWindow } from './match-playback';

/**
 * The dark dip the viewer fades over a cut or a jump (`replay-v4`).
 *
 * The film cuts in two places — the kick-off after a goal and the second half — because the players cannot
 * walk to their new places in a believable time, and the highlights reel jumps from one clip to the next. In
 * both a picture that simply changed would read as a glitch, so the screen dips to dark and back over about
 * 200 ms of *real* time. It is real time rather than film time because the eye measures the dip, not the
 * match: at 8x the same dip spans eight times the film.
 *
 * Everything here is a pure function of the playhead, so it can be asserted without a canvas or a clock.
 */

/** How long a dip lasts, from the screen starting to darken to it being clear again, in real milliseconds. */
export const FADE_MILLISECONDS = 200;

/** A moment the picture is dipped around. */
export interface FadeEdge {
  readonly filmMilliseconds: number;
  /**
   * `cut` darkens up to the moment and clears after it; `leave` only darkens up to it, because the playhead
   * is about to jump away; `enter` only clears after it, because the playhead has just jumped in.
   */
  readonly kind: 'cut' | 'leave' | 'enter';
}

/**
 * The edges a playlist has: every cut in the film, and — when it plays more than one window — every place the
 * playhead jumps from the end of a window to the start of the next.
 */
export function fadeEdgesFor(
  cuts: readonly FilmCut[],
  windows: readonly PlaylistWindow[],
): readonly FadeEdge[] {
  const edges: FadeEdge[] = cuts.map((cut) => ({
    filmMilliseconds: cut.startMilliseconds,
    kind: 'cut',
  }));

  windows.forEach((window, index) => {
    if (index > 0) {
      edges.push({ filmMilliseconds: window.startMilliseconds, kind: 'enter' });
    }

    if (index < windows.length - 1) {
      edges.push({ filmMilliseconds: window.endMilliseconds, kind: 'leave' });
    }
  });

  return edges;
}

/**
 * How dark the picture is at a film moment, 0 (clear) to 1 (black).
 *
 * @param edges The playlist's edges.
 * @param filmMilliseconds The playhead.
 * @param filmPerRealMilliseconds How much film a real millisecond plays, which is the playback speed.
 */
export function fadeAlpha(
  edges: readonly FadeEdge[],
  filmMilliseconds: number,
  filmPerRealMilliseconds: number,
): number {
  const reach = (FADE_MILLISECONDS / 2) * Math.max(0.01, filmPerRealMilliseconds);
  let alpha = 0;

  for (const edge of edges) {
    const offset = filmMilliseconds - edge.filmMilliseconds;
    const distance =
      edge.kind === 'cut' ? Math.abs(offset) : edge.kind === 'leave' ? -offset : offset;

    if (distance < 0 || distance >= reach) {
      continue;
    }

    alpha = Math.max(alpha, 1 - distance / reach);
  }

  return alpha;
}
