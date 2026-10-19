import { DecimalPipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { SkillGroup } from '../../../core/squad/position-ratings';
import { attributeBand } from '../../../core/squad/squad-presentation';

/**
 * A player's skills as the hover card draws them: the name, where they play, and all twenty-eight skills in
 * their four families with each family's average, the skills their position weights most marked.
 *
 * It only draws what it is given, so the tactics board (which weights for the slot a manager has selected)
 * and every other screen's player name (which weights for the player's own position) share one picture.
 */
@Component({
  selector: 'app-player-skills-card',
  imports: [DecimalPipe],
  templateUrl: './player-skills-card.html',
})
export class PlayerSkillsCard {
  /** The player's name. */
  readonly name = input.required<string>();

  /** The words beside the name: position, age, and the club when one is known. */
  readonly subtitle = input.required<string>();

  /** What the averages are weighted for, such as "a centre back". */
  readonly basisLabel = input.required<string>();

  /** The skills, grouped by family. */
  readonly groups = input.required<readonly SkillGroup[]>();

  /** The tint for an attribute or an average, on the 1–20 scale. */
  protected skillClass(value: number | null): string {
    return value === null ? 'text-muted' : attributeBand(value).className;
  }
}
