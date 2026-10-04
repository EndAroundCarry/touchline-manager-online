import { Injectable, computed, inject } from '@angular/core';
import { ClubFixture } from '../competition/competition.models';
import { CompetitionStore } from '../competition/competition-store';
import { ResultRevealStore } from './result-reveal-store';

/**
 * Which of the manager's own results are still to be seen, and so which screens must keep quiet.
 *
 * A result is a surprise until the manager has watched the match, skipped to its end, or asked for it
 * ({@link ResultRevealStore}). Beyond the match's own scoreline, a result is also in the league table, the club's
 * record, the division's leaders, and a player's match rows, so each of those asks the gate before it shows
 * anything the match would give away.
 *
 * Only the manager's own club's matches are protected. Another club's match is not something the manager is
 * waiting to watch, so its result is public the moment it is published. The gate cannot take a result back out
 * of an aggregate, so a screen that shows one holds the whole aggregate back while any of the manager's own
 * results is unseen, rather than show a table that says what a hidden line does not.
 *
 * The gate decides from the manager's own fixture list. Until that list has been read it answers "hide": a
 * result is never shown first and hidden after.
 */
@Injectable({ providedIn: 'root' })
export class ResultGate {
  private readonly competition = inject(CompetitionStore);
  private readonly reveals = inject(ResultRevealStore);

  /** The manager's own published matches whose result has not been seen, in round order. */
  readonly unseen = computed<readonly ClubFixture[]>(() =>
    (this.competition.fixtures()?.fixtures ?? []).filter(
      (fixture) =>
        fixture.status === 'published' &&
        fixture.matchId !== null &&
        !this.reveals.isRevealed(fixture.matchId),
    ),
  );

  /** Whether the manager's fixture list has been read, or has failed to be, so the gate can decide. */
  readonly settled = computed(
    () =>
      !this.competition.fixturesLoading() &&
      (this.competition.fixtures() !== null || this.competition.fixturesError() !== null),
  );

  /**
   * Whether a screen showing an aggregate of results must hold it back. True while the fixture list is being
   * read, and while any of the manager's own results is unseen.
   */
  readonly hiding = computed(() => !this.settled() || this.unseen().length > 0);

  /**
   * Whether a screen showing a division's aggregates must hold them back.
   *
   * Another division's table does not contain the manager's matches, so it is never held back. The manager's own
   * division is, while any result of theirs is unseen — and until the fixture list has been read, so nothing is
   * shown first and hidden after.
   *
   * @param divisionId The division the aggregates are of, or null where it is the manager's own.
   */
  holdsBack(divisionId: string | null): boolean {
    if (!this.settled()) {
      return true;
    }

    const own = this.competition.fixtures()?.divisionId;

    return (
      this.unseen().length > 0 && (divisionId === null || own === undefined || divisionId === own)
    );
  }

  /** Reads the manager's fixture list again, so a result published since it was last read is accounted for. */
  refresh(): void {
    if (!this.competition.fixturesLoading()) {
      this.competition.loadFixtures();
    }
  }

  /**
   * Whether a match's result is held back: it is one of the manager's own, and unseen.
   *
   * A match that is not the manager's own is never held back.
   */
  isHidden(matchId: string | null): boolean {
    return matchId !== null && this.unseen().some((fixture) => fixture.matchId === matchId);
  }

  /** Shows one match's result. */
  reveal(matchId: string | null): void {
    this.reveals.reveal(matchId);
  }

  /** Shows every unseen result of the manager's own. */
  revealAll(): void {
    for (const fixture of this.unseen()) {
      this.reveals.reveal(fixture.matchId);
    }
  }
}
