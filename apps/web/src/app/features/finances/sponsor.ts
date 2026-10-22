import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import {
  SEASON_OBJECTIVES,
  bonusFor,
  objectiveLabel,
  objectiveWithPlace,
  offerBlurb,
  offerLabel,
  outcomeLabel,
  placeOrBetter,
  signedByLabel,
} from '../../core/finance/sponsor-presentation';
import { SponsorStore } from '../../core/finance/sponsor-store';
import { SeasonObjective, SponsorOffer, SponsorOfferKind } from '../../core/finance/sponsor.models';
import { formatDeadline } from '../../core/world/presentation';
import {
  CHECKBOX_ROW,
  FORM_ERROR,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';
import { Money } from '../../shared/ui/money/money';

/**
 * The sponsor page (plan toasty-scott section 3; `SPN-1`…`SPN-6`, `OBJ-1`…`OBJ-4`).
 *
 * Three offers that differ in how much of the money is fixed, and an objective to aim for. The bonus beside every
 * choice is the server's own, worked out from the club's fans and standing, so changing the objective changes the
 * figures on the cards without the page working anything out (`INT-1`). The manager signs until the deadline; if
 * they do not, the board signs the balanced offer at its realistic objective.
 */
@Component({
  selector: 'app-sponsor',
  imports: [RouterLink, Money],
  templateUrl: './sponsor.html',
})
export class SponsorScreen {
  private readonly store = inject(SponsorStore);
  private readonly maintenance = inject(MaintenanceStore);

  protected readonly page = this.store.page;
  protected readonly loading = this.store.loading;
  protected readonly error = this.store.error;
  protected readonly signing = this.store.signing;
  protected readonly signError = this.store.signError;
  protected readonly signedMessage = this.store.signedMessage;

  /** Whether a write is allowed; offline or read-only a deal cannot be signed. */
  protected readonly canMutate = this.maintenance.canMutate;

  /** The objective picked in the form, or null until the manager picks one (then the deal's, or the board's). */
  private readonly picked = signal<SeasonObjective | null>(null);

  /** The objective the form shows: the manager's pick, else the signed deal's, else the board's realistic one. */
  protected readonly objective = computed<SeasonObjective>(
    () =>
      this.picked() ??
      this.page()?.deal?.objective ??
      this.page()?.realisticObjective ??
      'top_half',
  );

  protected readonly objectives = SEASON_OBJECTIVES;
  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly radioRowClass = CHECKBOX_ROW;

  constructor() {
    this.store.load();
  }

  /** Picks the objective the offers are quoted for. */
  protected pick(objective: SeasonObjective): void {
    this.picked.set(objective);
  }

  /** Signs an offer at the picked objective. */
  protected sign(kind: SponsorOfferKind): void {
    this.store.sign({ offer: kind, objective: this.objective() });
  }

  /** The bonus an offer pays at the picked objective. */
  protected bonus(offer: SponsorOffer) {
    return bonusFor(offer, this.objective());
  }

  /** Whether an offer is the one the club signed. */
  protected isSigned(kind: SponsorOfferKind): boolean {
    return this.page()?.deal?.kind === kind;
  }

  protected offerName(kind: SponsorOfferKind): string {
    return offerLabel(kind);
  }

  protected blurb(kind: SponsorOfferKind): string {
    return offerBlurb(kind);
  }

  protected objectiveName(objective: string): string {
    return objectiveLabel(objective);
  }

  protected objectiveWithPlace(objective: SeasonObjective, place: number): string {
    return objectiveWithPlace(objective, place);
  }

  /** "3rd or better" for the place an objective asks for. */
  protected target(place: number): string {
    return placeOrBetter(place);
  }

  /** The place an objective asks for, read from the first offer's quotes. */
  protected targetPlaceOf(objective: SeasonObjective): number | null {
    return (
      this.page()?.offers[0]?.objectives.find((quote) => quote.objective === objective)
        ?.targetPlace ?? null
    );
  }

  protected signedBy(value: string): string {
    return signedByLabel(value);
  }

  protected outcome(): string | null {
    const deal = this.page()?.deal;

    return deal === null || deal === undefined ? null : outcomeLabel(deal);
  }

  /** Formats the deadline, a moment the manager acts against. */
  protected deadline(value: string): string {
    return formatDeadline(value);
  }
}
