import { Injectable, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { ApiError } from '../api/api-error';
import { MatchApi } from './match-api';
import { Match, MatchInstructionNames, MatchPresentation } from './match.models';

/**
 * The match center's view state (master plan §9.5, §11.1).
 *
 * A feature-scoped store following `CompetitionStore`. It holds one match's summary and its replay, and
 * nothing else: the viewer plays a response, it never edits one, so there is no draft to reconcile and no
 * concurrency contract beyond the cache the server already applies to an immutable presentation.
 *
 * Both reads are made together because the screen shows them together — the scoreline above the replay —
 * and one failure is one message rather than a half-drawn screen.
 */
@Injectable({ providedIn: 'root' })
export class MatchStore {
  private readonly api = inject(MatchApi);

  private readonly matchSignal = signal<Match | null>(null);
  private readonly presentationSignal = signal<MatchPresentation | null>(null);
  private readonly instructionNamesSignal = signal<MatchInstructionNames | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  /** The match last asked for, so a slow answer about the one before it is dropped. */
  private requestedMatchId: string | null = null;

  /** The match summary last read. */
  readonly match = this.matchSignal.asReadonly();

  /** The replay last read: commentary and highlights. */
  readonly presentation = this.presentationSignal.asReadonly();

  /**
   * The names of the caller's own match instructions in this match, or null until read or when they could not be.
   * They only decorate the commentary, so a failure to read them never stops the screen.
   */
  readonly instructionNames = this.instructionNamesSignal.asReadonly();

  /** Whether the match center is reading the server. */
  readonly loading = this.loadingSignal.asReadonly();

  /** Why the last read failed. */
  readonly error = this.errorSignal.asReadonly();

  /** Reads one played match's summary and replay. */
  load(matchId: string): void {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    this.matchSignal.set(null);
    this.presentationSignal.set(null);
    this.instructionNamesSignal.set(null);
    this.requestedMatchId = matchId;

    this.api.instructionNames(matchId).subscribe({
      next: (names) => {
        if (this.requestedMatchId === matchId) {
          this.instructionNamesSignal.set(names);
        }
      },
      error: () => undefined,
    });

    forkJoin({
      match: this.api.get(matchId),
      presentation: this.api.presentation(matchId),
    }).subscribe({
      next: ({ match, presentation }) => {
        this.matchSignal.set(match);
        this.presentationSignal.set(presentation);
        this.loadingSignal.set(false);
      },
      error: (error: unknown) => {
        this.loadingSignal.set(false);
        this.errorSignal.set(
          error instanceof ApiError ? error.detail : 'That match could not be loaded.',
        );
      },
    });
  }

  /** Forgets everything read. Called when the session ends. */
  clear(): void {
    this.requestedMatchId = null;
    this.matchSignal.set(null);
    this.presentationSignal.set(null);
    this.instructionNamesSignal.set(null);
    this.loadingSignal.set(false);
    this.errorSignal.set(null);
  }
}
