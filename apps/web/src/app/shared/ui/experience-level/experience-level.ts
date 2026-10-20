import { Component, computed, input } from '@angular/core';
import { PlayerExperience } from '../../../core/squad/squad.models';

/** The top of the experience scale, as the points are written: "47.5 / 100". */
const SCALE_POINTS = 100;

/**
 * A player's experience: one pip per level, the points, and what the level adds in matches.
 *
 * The server sends the level and the bonus, so nothing here repeats the rule. The pips are decoration; the
 * words say the same thing, and a full sentence is read out to assistive technology, so the level is never
 * carried by the filled pips alone.
 *
 * The bonus is on the skills a player plays with in a match; the attributes shown on the 1–20 scale do not change.
 */
@Component({
  selector: 'app-experience-level',
  templateUrl: './experience-level.html',
  host: { class: 'block' },
})
export class ExperienceLevel {
  /** The experience to show. */
  readonly experience = input.required<PlayerExperience>();

  /** Shows one line (pips and level) instead of the level with its bonus and the next step beneath. */
  readonly compact = input(false);

  /** One entry per level, true for those reached. */
  protected readonly pips = computed(() => {
    const { level, maxLevel } = this.experience();

    return Array.from({ length: maxLevel }, (_, index) => index < level);
  });

  /** The level and the points, such as `Level 2 · 47.5 / 100`. */
  protected readonly headline = computed(() => {
    const { level, points } = this.experience();
    const written = `${formatPoints(points)} / ${SCALE_POINTS}`;

    return level === 0 ? `No level yet · ${written}` : `Level ${level} · ${written}`;
  });

  /** What the level adds in a match, or an empty string before the first level. */
  protected readonly bonus = computed(() => {
    const { skillBonusPercent } = this.experience();

    return skillBonusPercent === 0 ? '' : `+${skillBonusPercent}% skills in matches`;
  });

  /** The next step, or an empty string at the highest level. */
  protected readonly nextStep = computed(() => {
    const { nextLevelPoints } = this.experience();

    return nextLevelPoints === null ? '' : `Next level at ${formatPoints(nextLevelPoints)}`;
  });

  /** The whole picture as one sentence for assistive technology. */
  protected readonly spoken = computed(() => {
    const { level, maxLevel, points, skillBonusPercent, nextLevelPoints } = this.experience();
    const start =
      level === 0
        ? `Experience: no level yet, ${formatPoints(points)} of ${SCALE_POINTS} points.`
        : `Experience: level ${level} of ${maxLevel}, ${formatPoints(points)} of ${SCALE_POINTS} points, ` +
          `plus ${skillBonusPercent} per cent to skills in matches.`;

    return nextLevelPoints === null
      ? start
      : `${start} The next level is at ${formatPoints(nextLevelPoints)} points.`;
  });
}

/** Writes points without a pointless decimal: `12`, `12.5`. */
function formatPoints(points: number): string {
  return Number.isInteger(points) ? String(points) : points.toFixed(1);
}
