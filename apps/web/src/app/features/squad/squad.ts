import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TableModule } from 'primeng/table';
import { ApiError } from '../../core/api/api-error';
import {
  POSITION_FAMILY_OPTIONS,
  SquadFilter,
  availabilityLabel,
  filterSquad,
  positionLabel,
  squadStatusLabel,
  stateBand,
} from '../../core/squad/squad-presentation';
import { SquadStore } from '../../core/squad/squad-store';
import { SquadPlayer } from '../../core/squad/squad.models';
import { formatFunds } from '../../core/world/presentation';
import { OnboardingStore } from '../../core/world/onboarding-store';
import {
  CHECKBOX_INPUT,
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  SECONDARY_BUTTON,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';

/**
 * The squad screen (master plan §11.1, F-16).
 *
 * A PrimeNG table because §11.1 asks for one and because sorting is the one table behaviour worth not
 * reimplementing: PrimeNG owns the sort model, including the `aria-sort` state a screen reader announces.
 * Filtering is ours and client-side, because the response is bounded to twenty-five players (§10.3) and a
 * round trip per keystroke would be the worse trade.
 *
 * Condition and fatigue are shown with their band word beside the number, so the colour is decoration
 * rather than the message (§11.3).
 */
@Component({
  selector: 'app-squad',
  imports: [RouterLink, TableModule],
  templateUrl: './squad.html',
})
export class Squad {
  private readonly store = inject(SquadStore);
  private readonly onboarding = inject(OnboardingStore);

  protected readonly squad = this.store.squad;
  protected readonly contracts = this.store.contracts;
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  protected readonly nameFilter = signal('');
  protected readonly positionFilter = signal('');
  protected readonly availableOnly = signal(false);

  protected readonly familyOptions = POSITION_FAMILY_OPTIONS;

  /** The rows on screen: the squad with the filter applied. Sorting is the table's own. */
  protected readonly players = computed<SquadPlayer[]>(() => {
    const filter: SquadFilter = {
      name: this.nameFilter(),
      positionFamily: this.positionFilter(),
      availableOnly: this.availableOnly(),
    };

    // A fresh mutable array, because the table's `value` input takes one.
    return [...filterSquad(this.squad()?.players ?? [], filter)];
  });

  /** Whether any filter is narrowing the table, so the count is worth showing. */
  protected readonly filtersActive = computed(
    () =>
      this.nameFilter().trim().length > 0 ||
      this.positionFilter().length > 0 ||
      this.availableOnly(),
  );

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK;
  protected readonly textInputClass = TEXT_INPUT;
  protected readonly checkboxClass = CHECKBOX_INPUT;

  constructor() {
    this.load();
  }

  /** Reads the squad, then the contract list, for the club the account currently holds. */
  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);

    this.onboarding.loadState().subscribe({
      next: (state) => {
        const clubId = state.tenure?.clubId;

        if (clubId === undefined) {
          this.loading.set(false);
          this.loadError.set('You do not manage a club, so there is no squad to show.');

          return;
        }

        this.store.loadSquad(clubId).subscribe({
          next: () => this.loadContracts(),
          error: (error: unknown) => {
            this.loading.set(false);
            this.loadError.set(
              error instanceof ApiError ? error.detail : 'Your squad could not be loaded.',
            );
          },
        });
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'Your club could not be identified.',
        );
      },
    });
  }

  /** Loads the contract list. The squad is the screen's subject, so a failure here only drops that block. */
  protected loadContracts(): void {
    this.store.loadContracts().subscribe({
      next: () => this.loading.set(false),
      error: () => this.loading.set(false),
    });
  }

  /** Applies a name filter as the manager types. */
  protected onNameInput(event: Event): void {
    this.nameFilter.set((event.target as HTMLInputElement).value);
  }

  /** Applies a position-family filter. */
  protected onPositionChange(event: Event): void {
    this.positionFilter.set((event.target as HTMLSelectElement).value);
  }

  /** Applies the "available only" filter. */
  protected onAvailableChange(event: Event): void {
    this.availableOnly.set((event.target as HTMLInputElement).checked);
  }

  /** Resets every filter to its default. */
  protected clearFilters(): void {
    this.nameFilter.set('');
    this.positionFilter.set('');
    this.availableOnly.set(false);
  }

  /** Formats an amount for display. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Names a position code. */
  protected position(code: string): string {
    return positionLabel(code);
  }

  /** Names a squad status. */
  protected squadStatus(code: string): string {
    return squadStatusLabel(code);
  }

  /** Describes an injury or suspension in fixtures. */
  protected availability(type: string, remainingFixtures: number): string {
    return availabilityLabel(type, remainingFixtures);
  }

  /** The tint for a state value where higher is better. */
  protected conditionClass(value: number): string {
    return stateBand(value, true).className;
  }

  /** The band word for a state value where higher is better. */
  protected conditionWord(value: number): string {
    return stateBand(value, true).label;
  }

  /** The tint for a state value where lower is better, which is fatigue. */
  protected fatigueClass(value: number): string {
    return stateBand(value, false).className;
  }

  /** The band word for a state value where lower is better. */
  protected fatigueWord(value: number): string {
    return stateBand(value, false).label;
  }
}
