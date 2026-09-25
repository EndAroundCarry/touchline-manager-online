import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import {
  attributeGroups,
  availabilityLabel,
  footLabel,
  positionLabel,
  squadStatusLabel,
  stateRows,
} from '../../core/squad/squad-presentation';
import { SquadStore } from '../../core/squad/squad-store';
import { formatFunds, formatInstant } from '../../core/world/presentation';
import { AttributeValue } from '../../shared/ui/attribute-value/attribute-value';
import { FORM_ERROR, LINK, PAGE_HEADING, SECONDARY_BUTTON } from '../../shared/forms/control-styles';

/**
 * The player profile (master plan §11.1, F-17).
 *
 * The attribute grid is the reason this screen exists, and it is rendered through `AttributeValue` so
 * every attribute carries its number *and* the word for its band — §11.3 forbids a colour being the only
 * signal, and a test asserts both halves render.
 *
 * History and season statistics are §11.1's other two items and are absent on purpose: neither exists
 * before a match has been played, and the stages that produce them add their cards here rather than
 * showing permanently-empty tables now.
 */
@Component({
  selector: 'app-player',
  imports: [RouterLink, AttributeValue],
  templateUrl: './player.html',
})
export class PlayerProfile implements OnInit {
  /** The player identity, bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  private readonly store = inject(SquadStore);

  protected readonly player = this.store.player;
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  /** The attribute families, in the order the profile shows them. */
  protected readonly attributeFamilies = computed(() => {
    const player = this.player();

    return player === null ? [] : attributeGroups(player.attributes);
  });

  /** The four state measures with their bands resolved. */
  protected readonly stateMeasures = computed(() => {
    const player = this.player();

    return player === null ? [] : stateRows(player.state);
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK;

  /** Reads the profile for the player the route names. */
  ngOnInit(): void {
    const playerId = this.id();

    this.loading.set(true);
    this.loadError.set(null);

    this.store.loadPlayer(playerId).subscribe({
      next: () => this.loading.set(false),
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'That player could not be loaded.',
        );
      },
    });
  }

  /** Formats an amount for display. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Formats an instant in the viewer's local time. */
  protected instant(value: string): string {
    return formatInstant(value);
  }

  /** Names a position code. */
  protected position(code: string): string {
    return positionLabel(code);
  }

  /** Names a squad status. */
  protected squadStatus(code: string): string {
    return squadStatusLabel(code);
  }

  /** Names a preferred foot. */
  protected foot(code: string): string {
    return footLabel(code);
  }

  /** Describes an injury or suspension in fixtures. */
  protected availability(type: string, remainingFixtures: number): string {
    return availabilityLabel(type, remainingFixtures);
  }
}
