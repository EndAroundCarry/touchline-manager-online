import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { goalDifferenceLabel } from '../../core/competition/competition-presentation';
import { CompetitionStore } from '../../core/competition/competition-store';
import { formatInstant } from '../../core/world/presentation';
import { FORM_ERROR, PAGE_HEADING, STATUS_MESSAGE } from '../../shared/forms/control-styles';

/**
 * The division table screen (master plan §11.1, §10.5).
 *
 * Two ways in, one table. The navigation destination is the manager's *own* division, which no
 * competition read names, so it is resolved from the club's fixture list before the table is read; the
 * named route reads a division outright, which is what a shared or bookmarked link uses.
 *
 * The rows come back in the order the server ranked them (`TBL-1`…`TBL-11`), so the screen renders that
 * order rather than sorting, and the manager's own club is marked with words rather than a tint alone
 * (§11.3). The table is a real `<table>` with a caption, column headers, and a row header, because a
 * league table is tabular data and assistive technology reads it as such.
 */
@Component({
  selector: 'app-competition-table',
  templateUrl: './table.html',
})
export class CompetitionTable {
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(CompetitionStore);

  protected readonly table = this.store.divisionTable;
  protected readonly loading = this.store.tableLoading;
  protected readonly loadError = this.store.tableError;

  /** Whether any matchday has published yet, so the screen can say why every club is level. */
  protected readonly hasPlayedAny = computed(() =>
    (this.table()?.rows ?? []).some((row) => row.played > 0),
  );

  /** The table's caption, so assistive technology is told what the table is before its rows. */
  protected readonly caption = computed(() => {
    const table = this.table();

    return table === null
      ? ''
      : `${table.divisionName} table for ${table.seasonLabel}, best first.`;
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;

  constructor() {
    const divisionId = this.route.snapshot.paramMap.get('divisionId');

    if (divisionId !== null && divisionId.length > 0) {
      this.store.loadDivisionTable(divisionId);
    } else {
      this.store.loadMyDivisionTable();
    }
  }

  /** Whether a row is the manager's own club, marked in words as well as weight (§11.3). */
  protected isManaged(clubId: string): boolean {
    return this.store.managedClubId() === clubId;
  }

  /** Formats a goal difference with its sign (`TBL-3`). */
  protected difference(goalDifference: number): string {
    return goalDifferenceLabel(goalDifference);
  }

  /** Formats the instant the table was read, in the viewer's local time (`TIME-5`). */
  protected instant(value: string): string {
    return formatInstant(value);
  }
}
