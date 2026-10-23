import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FinanceStore } from '../../core/finance/finance-store';
import {
  creditsOf,
  debitsOf,
  moneyToneClass,
  placeLabel,
  signedAmount,
  stateBanner,
  streamRows,
  wageGauge,
} from '../../core/finance/finance-presentation';
import { SeasonObjective, SponsorDeal, SponsorOfferKind } from '../../core/finance/sponsor.models';
import { SponsorStore } from '../../core/finance/sponsor-store';
import {
  objectiveWithPlace,
  offerLabel,
  outcomeLabel,
} from '../../core/finance/sponsor-presentation';
import { formatDeadline, formatFunds, formatInstant } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK_ACTION,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
} from '../../shared/forms/control-styles';
import { Money } from '../../shared/ui/money/money';

/**
 * The finance screen (master plan §11.1; `FIN-3`…`FIN-9`, plan toasty-scott M7).
 *
 * It shows what the ledger says and where the season is heading. The balances are the running total of the
 * entries below; the forecast, the wage budget, the state of the club and the prize at its present place are the
 * server's own figures (`INT-1`), set against what the ledger has taken so far. Every movement is a line, so a
 * manager can follow a balance back to the decision that produced it.
 */
@Component({
  selector: 'app-finances',
  imports: [RouterLink, Money],
  templateUrl: './finances.html',
})
export class Finances {
  private readonly store = inject(FinanceStore);
  private readonly sponsorStore = inject(SponsorStore);

  protected readonly summary = this.store.summary;
  protected readonly ledger = this.store.ledger;
  protected readonly hasMore = this.store.hasMore;
  protected readonly loading = this.store.loading;
  protected readonly loadingMore = this.store.loadingMore;
  protected readonly error = this.store.error;

  private readonly forecastCategories = computed(
    () => new Set((this.summary()?.outlook?.streams ?? []).map((stream) => stream.category)),
  );

  protected readonly sponsor = this.sponsorStore.page;
  protected readonly sponsorLoading = this.sponsorStore.loading;

  /** The banner for a club in the red or in administration, or null while it is healthy. */
  protected readonly banner = computed(() => stateBanner(this.summary()?.outlook ?? null));

  /** The wage-budget gauge, or null before a season exists. */
  protected readonly gauge = computed(() => {
    const outlook = this.summary()?.outlook ?? null;

    return outlook === null ? null : wageGauge(outlook);
  });

  /** The forecast by stream beside what the ledger has taken, income first and then cost. */
  protected readonly rows = computed(() => {
    const summary = this.summary();

    return summary?.outlook == null ? [] : streamRows(summary.outlook, summary.totals);
  });

  protected readonly incomeRows = computed(() =>
    this.rows().filter((row) => row.forecastMinor >= 0),
  );
  protected readonly costRows = computed(() => this.rows().filter((row) => row.forecastMinor < 0));

  /** The season's money in outside the forecast (prizes, fees received, grants), labelled. */
  protected readonly otherCredits = computed(() => {
    const forecast = this.forecastCategories();

    return creditsOf(
      (this.summary()?.totals ?? []).filter((total) => !forecast.has(total.category)),
    );
  });

  /** The season's money out outside the forecast (fees paid, building, releases), labelled. */
  protected readonly otherDebits = computed(() => {
    const forecast = this.forecastCategories();

    return debitsOf(
      (this.summary()?.totals ?? []).filter((total) => !forecast.has(total.category)),
    );
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly linkActionClass = LINK_ACTION;
  protected readonly formErrorClass = FORM_ERROR;

  constructor() {
    this.store.load();
    this.sponsorStore.load();
  }

  /** The tone's classes. */
  protected toneClass(tone: 'good' | 'warn' | 'bad'): string {
    return moneyToneClass(tone);
  }

  /** The prize's place in words: "7th of 16". */
  protected place(place: number, clubs: number): string {
    return placeLabel(place, clubs);
  }

  /** Names a sponsor offer. */
  protected offer(kind: SponsorOfferKind): string {
    return offerLabel(kind);
  }

  /** Names an objective with the place it asks for. */
  protected objective(objective: SeasonObjective, place: number): string {
    return objectiveWithPlace(objective, place);
  }

  /** How a settled deal ended, in words, or null. */
  protected outcome(deal: SponsorDeal): string | null {
    return outcomeLabel(deal);
  }

  /** Formats the sponsor deadline, a moment the manager acts against. */
  protected deadline(value: string): string {
    return formatDeadline(value);
  }

  /** Formats an amount for display. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Formats a signed movement so a debit and a credit differ by more than colour. */
  protected signed(minorUnits: number): string {
    return signedAmount(minorUnits, formatFunds);
  }

  /** Formats an instant in the viewer's local time. */
  protected instant(value: string): string {
    return formatInstant(value);
  }

  /** Reads the next page of the ledger. */
  protected loadMore(): void {
    this.store.loadMore();
  }
}
