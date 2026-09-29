import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { averageRatingLabel } from '../../core/competition/competition-presentation';
import {
  attributeGroups,
  availabilityLabel,
  footLabel,
  positionLabel,
  seasonStatRows,
  seasonsPlayedLabel,
  squadStatusLabel,
  stateRows,
} from '../../core/squad/squad-presentation';
import { SquadStore } from '../../core/squad/squad-store';
import { formatFunds, formatInstant } from '../../core/world/presentation';
import { AttributeValue } from '../../shared/ui/attribute-value/attribute-value';
import {
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';

/**
 * The player profile (master plan §11.1, F-17).
 *
 * The attribute grid is the reason this screen exists, and it is rendered through `AttributeValue` so
 * every attribute carries its number *and* the word for its band — §11.3 forbids a colour being the only
 * signal, and a test asserts both halves render.
 *
 * The season summary is the player's line of the division leaderboard's projection (`STA-2`), read with
 * the profile rather than recomputed; a player who has not taken the pitch has none, and the screen says so
 * rather than showing a row of zeros.
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

  /** This season's summary lines, or an empty list before the player has appeared (`STA-2`). */
  protected readonly seasonStats = computed(() => {
    const stats = this.player()?.seasonStats;

    return stats === null || stats === undefined ? [] : seasonStatRows(stats);
  });

  /** The player's career totals as summary lines, or an empty list before they have ever appeared (`STA-2`). */
  protected readonly careerTotals = computed(() => {
    const career = this.player()?.careerStats;

    return career === null || career === undefined ? [] : seasonStatRows(career.totals);
  });

  /** The player's seasons, most recent first, or an empty list (`STA-2`). */
  protected readonly careerSeasons = computed(() => this.player()?.careerStats?.seasons ?? []);

  /** How many seasons the player has appeared in, as a phrase (`STA-2`). */
  protected readonly seasonsPlayed = computed(() => {
    const career = this.player()?.careerStats;

    return career === null || career === undefined ? '' : seasonsPlayedLabel(career.seasonsPlayed);
  });

  /** The renewal quote last requested, or null (`CON-3`). */
  protected readonly renewalQuote = this.store.renewalQuote;

  /** The term the manager is asking for, in seasons (`CON-1`). */
  protected readonly renewalSeasons = signal(3);

  /** The lengths a manager may offer. */
  protected readonly termOptions = [1, 2, 3];

  protected readonly quoting = signal(false);
  protected readonly signing = signal(false);
  protected readonly renewalError = signal<string | null>(null);
  protected readonly renewalMessage = signal<string | null>(null);

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly inputClass = TEXT_INPUT;
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

  /** Formats a career season's average rating to one decimal, or a dash before there is one (`TRN-8`). */
  protected rating(value: number | null): string {
    return averageRatingLabel(value);
  }

  /** Describes an injury or suspension in fixtures. */
  protected availability(type: string, remainingFixtures: number): string {
    return availabilityLabel(type, remainingFixtures);
  }

  /** Changes the offered term and forgets any quote for the old one. */
  protected setSeasons(value: number): void {
    this.renewalSeasons.set(value);
    this.store.clearRenewalQuote();
    this.renewalError.set(null);
    this.renewalMessage.set(null);
  }

  /** Asks the server for the deterministic quote for the chosen term (`CON-3`). */
  protected quoteRenewal(): void {
    const contract = this.player()?.contract;

    if (contract === null || contract === undefined || this.quoting()) {
      return;
    }

    this.quoting.set(true);
    this.renewalError.set(null);
    this.renewalMessage.set(null);

    this.store.quoteRenewal(contract.id, this.renewalSeasons()).subscribe({
      next: () => this.quoting.set(false),
      error: (error: unknown) => {
        this.quoting.set(false);
        this.renewalError.set(
          error instanceof ApiError ? error.detail : 'A renewal quote could not be requested.',
        );
      },
    });
  }

  /** Accepts the quoted renewal (`CON-4`), conditional on the version the quote carried. */
  protected signRenewal(): void {
    const player = this.player();
    const quote = this.renewalQuote();

    if (player === null || player.contract === null || quote === null || this.signing()) {
      return;
    }

    this.signing.set(true);
    this.renewalError.set(null);
    this.renewalMessage.set(null);

    this.store
      .renewContract(player.contract.id, player.id, this.renewalSeasons(), quote.contractVersion)
      .subscribe({
        next: () => {
          this.signing.set(false);
          this.renewalMessage.set(`${player.fullName} has been re-signed.`);
        },
        error: (error: unknown) => {
          this.signing.set(false);

          // A stale quote is dropped rather than shown, so the manager asks again against current state.
          if (error instanceof ApiError && error.isPreconditionFailed) {
            this.store.clearRenewalQuote();
          }

          this.renewalError.set(
            error instanceof ApiError ? error.detail : 'The renewal could not be signed.',
          );
        },
      });
  }
}
