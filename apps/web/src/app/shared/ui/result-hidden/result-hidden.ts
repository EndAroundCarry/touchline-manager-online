import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ResultGate } from '../../../core/match/result-gate';
import { LINK_ACTION, SECONDARY_BUTTON } from '../../forms/control-styles';

/**
 * Stands in for a screen's aggregate of results — a table, a record, a list of leaders — while one of the
 * manager's own results is still unseen, because the aggregate would give it away.
 *
 * It says what is being held back and why, offers the two ways out the manager has everywhere (watch the match,
 * or show the result), and tells a screen reader the same. While the manager's fixture list is still being read
 * it says only that it is checking, so the aggregate is never on screen for a moment first.
 */
@Component({
  selector: 'app-result-hidden',
  imports: [RouterLink],
  templateUrl: './result-hidden.html',
  host: { class: 'block' },
})
export class ResultHidden {
  private readonly gate = inject(ResultGate);

  /** What is being held back, as a noun phrase that starts a sentence: "The league table". */
  readonly subject = input('This');

  protected readonly settled = this.gate.settled;
  protected readonly unseenCount = computed(() => this.gate.unseen().length);

  /** Where to watch the match that is being waited for: the oldest unseen one. */
  protected readonly watchLink = computed(() => {
    const matchId = this.gate.unseen()[0]?.matchId ?? null;

    return matchId === null ? null : `/matches/${matchId}`;
  });

  protected readonly buttonClass = SECONDARY_BUTTON;
  protected readonly linkClass = LINK_ACTION;

  /** Shows the unseen results, and with them whatever was held back. */
  protected showResults(): void {
    this.gate.revealAll();
  }
}
