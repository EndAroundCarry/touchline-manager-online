import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { tieBreakerLabel } from '../../core/competition/competition-presentation';
import { CompetitionStore } from '../../core/competition/competition-store';
import { formatInstant } from '../../core/world/presentation';
import { FORM_ERROR, PAGE_HEADING, STATUS_MESSAGE } from '../../shared/forms/control-styles';

/**
 * The competition rules screen (master plan §10.5, `TBL-1`…`TBL-11`).
 *
 * The points, the exact tie-break order, and the draw the season committed to before a ball was kicked —
 * which is the reader `TBL-11` asks for. The order arrives as the server's sequence of stable codes, so the
 * screen lists the criteria in the order the table applies them rather than reproducing the rule, and the
 * stored draw seed, its published hash, and every club's draw key are shown beside it, so the final
 * tie-breaker is something a manager can inspect rather than take on trust.
 *
 * The ordering is an ordered list and the clubs are a real `<table>` with a caption and a row header, so
 * assistive technology reads both as what they are (§11.3).
 */
@Component({
  selector: 'app-competition-rules',
  imports: [RouterLink],
  templateUrl: './rules.html',
})
export class CompetitionRules {
  private readonly route = inject(ActivatedRoute);
  private readonly store = inject(CompetitionStore);

  protected readonly rules = this.store.divisionRules;
  protected readonly loading = this.store.rulesLoading;
  protected readonly loadError = this.store.rulesError;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;

  constructor() {
    const divisionId = this.route.snapshot.paramMap.get('divisionId');

    if (divisionId !== null && divisionId.length > 0) {
      this.store.loadDivisionRules(divisionId);
    }
  }

  /** Names one tie-break criterion, falling back to the code (`TBL-2`…`TBL-10`). */
  protected tieBreaker(code: string): string {
    return tieBreakerLabel(code);
  }

  /** Formats the instant the rules were read, in the viewer's local time (`TIME-5`). */
  protected instant(value: string): string {
    return formatInstant(value);
  }
}
