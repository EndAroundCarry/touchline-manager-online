import { Component, computed, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { suspensionRemainingLabel } from '../../core/competition/competition-presentation';
import { CompetitionStore } from '../../core/competition/competition-store';
import { formatInstant } from '../../core/world/presentation';
import { FORM_ERROR, PAGE_HEADING, STATUS_MESSAGE } from '../../shared/forms/control-styles';

/**
 * The division discipline screen (master plan §10.5, `DIS-2`…`DIS-5`).
 *
 * The cards a season has shown and who still owes a suspension — the detail behind the table's card
 * columns, which break ties but name nobody. The rows come back in the order the server ranked them —
 * most sendings-off, then most bookings, then name — so the screen sorts nothing, and the suspension
 * column reads the fixtures a player still misses, from the same open absences the matchday serves, so the
 * page cannot disagree with the side a manager may actually name (`DIS-5`).
 *
 * It is a real `<table>` with a caption, column headers, and a player row header, because this is tabular
 * data and assistive technology reads it as such (§11.3).
 */
@Component({
  selector: 'app-competition-discipline',
  imports: [RouterLink],
  templateUrl: './discipline.html',
})
export class CompetitionDiscipline {
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(CompetitionStore);

  protected readonly discipline = this.store.divisionDiscipline;
  protected readonly loading = this.store.disciplineLoading;
  protected readonly loadError = this.store.disciplineError;

  /** The table's caption, so assistive technology is told what the table is before its rows. */
  protected readonly caption = computed(() => {
    const discipline = this.discipline();

    return discipline === null
      ? ''
      : `${discipline.divisionName} discipline for ${discipline.seasonLabel}, most sendings-off first.`;
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;

  constructor() {
    const divisionId = this.route.snapshot.paramMap.get('divisionId');

    if (divisionId !== null && divisionId.length > 0) {
      this.store.loadDivisionDiscipline(divisionId);
    }
  }

  /** Describes the fixtures a player still misses through suspension (`DIS-5`). */
  protected suspension(remainingFixtures: number): string {
    return suspensionRemainingLabel(remainingFixtures);
  }

  /** Formats the instant the discipline was read, in the viewer's local time (`TIME-5`). */
  protected instant(value: string): string {
    return formatInstant(value);
  }
}
