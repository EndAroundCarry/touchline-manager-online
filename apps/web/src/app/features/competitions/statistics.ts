import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { averageRatingLabel } from '../../core/competition/competition-presentation';
import { CompetitionStore } from '../../core/competition/competition-store';
import { ResultGate } from '../../core/match/result-gate';
import { formatInstant } from '../../core/world/presentation';
import { FORM_ERROR, PAGE_HEADING, STATUS_MESSAGE } from '../../shared/forms/control-styles';
import { ResultHidden } from '../../shared/ui/result-hidden/result-hidden';

/**
 * The division statistics screen (master plan §11.1, §10.5).
 *
 * A division's players, most goals first. The rows come back in the order the server ranked them — goals,
 * then assists, then name — so the screen sorts nothing and reimplements no ordering rule. It is a real
 * `<table>` with a caption, column headers, and a player row header, because a statistics table is tabular
 * data and assistive technology reads it as such, and the rating arrives already on its display scale
 * (`TRN-8`).
 */
@Component({
  selector: 'app-competition-statistics',
  imports: [RouterLink, ResultHidden],
  templateUrl: './statistics.html',
})
export class CompetitionStatistics {
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(CompetitionStore);
  private readonly gate = inject(ResultGate);

  protected readonly statistics = this.store.divisionStatistics;
  protected readonly loading = this.store.statisticsLoading;
  protected readonly loadError = this.store.statisticsError;

  /** The table's caption, so assistive technology is told what the table is before its rows. */
  protected readonly caption = computed(() => {
    const statistics = this.statistics();

    return statistics === null
      ? ''
      : `${statistics.divisionName} player statistics for ${statistics.seasonLabel}, most goals first.`;
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;

  constructor() {
    this.gate.refresh();

    const divisionId = this.route.snapshot.paramMap.get('divisionId');

    if (divisionId !== null && divisionId.length > 0) {
      this.store.loadDivisionStatistics(divisionId);
    }
  }

  /**
   * Whether the statistics are held back because they would give the manager's latest result away: they count
   * the goals of every match in the division, the manager's own included.
   */
  protected holdsBack(divisionId: string): boolean {
    return this.gate.holdsBack(divisionId);
  }

  /** Formats a player's average rating, or a dash before they have one (`TRN-8`). */
  protected rating(value: number | null): string {
    return averageRatingLabel(value);
  }

  /** Formats the instant the statistics were read, in the viewer's local time (`TIME-5`). */
  protected instant(value: string): string {
    return formatInstant(value);
  }
}
