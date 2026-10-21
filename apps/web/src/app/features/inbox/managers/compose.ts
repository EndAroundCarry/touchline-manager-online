import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '../../../core/api/api-error';
import { ConversationsStore } from '../../../core/messages/conversations-store';
import { BODY_MAX_CHARS, SUBJECT_MAX_CHARS } from '../../../core/messages/messages.models';
import { SquadApi } from '../../../core/squad/squad-api';
import { ClubOverview } from '../../../core/squad/squad.models';
import {
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  TEXT_INPUT,
} from '../../../shared/forms/control-styles';

/**
 * Starts a conversation with the manager who runs a club (`COM-11`).
 *
 * It is reached from the club page's "Send a message" link, which carries the club in the `club` query
 * parameter. The page reads the club again rather than trusting the link, so a club that has since lost its
 * manager, or turned out to be the writer's own, says so before a form is offered. The server decides in the
 * end; the counters only show the limits (`COM-13`).
 */
@Component({
  selector: 'app-compose-message',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './compose.html',
})
export class ComposeMessage {
  /** The club to write to, bound from the `club` query parameter. */
  readonly clubId = input<string | null>(null, { alias: 'club' });

  private readonly squadApi = inject(SquadApi);
  private readonly store = inject(ConversationsStore);
  private readonly router = inject(Router);

  protected readonly club = signal<ClubOverview | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  protected readonly subjectMax = SUBJECT_MAX_CHARS;
  protected readonly bodyMax = BODY_MAX_CHARS;

  protected readonly form = new FormGroup({
    subject: new FormControl('', { nonNullable: true }),
    body: new FormControl('', { nonNullable: true }),
  });

  private readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.value });

  protected readonly subjectLength = computed(() => (this.values().subject ?? '').length);
  protected readonly bodyLength = computed(() => (this.values().body ?? '').length);

  /** Whether both fields hold something once trimmed, and neither is over its limit. */
  protected readonly canSend = computed(() => {
    const subject = (this.values().subject ?? '').trim();
    const body = (this.values().body ?? '').trim();

    return (
      subject.length > 0 &&
      subject.length <= SUBJECT_MAX_CHARS &&
      body.length > 0 &&
      body.length <= BODY_MAX_CHARS
    );
  });

  protected readonly sending = this.store.sending;
  protected readonly sendError = this.store.sendError;

  /** Why no form is offered: no club named, none loaded, nobody to write to, or the writer's own club. */
  protected readonly problem = computed<string | null>(() => {
    const club = this.club();

    if (this.loadError() !== null) {
      return this.loadError();
    }

    if (club === null) {
      return this.clubId() === null ? 'Choose a club to write to from its page.' : null;
    }

    if (club.isOwnClub) {
      return 'That is your own club. You can write to the manager of another club.';
    }

    if (club.managerName === null) {
      return `${club.name} is run by the game's AI manager, so there is nobody to write to.`;
    }

    return null;
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK;
  protected readonly textInputClass = TEXT_INPUT;

  constructor() {
    effect(() => {
      const clubId = this.clubId();

      untracked(() => this.load(clubId));
    });
  }

  /** Sends the message and opens the thread it started. */
  protected send(): void {
    const club = this.club();

    if (club === null || !this.canSend() || this.sending()) {
      return;
    }

    const { subject, body } = this.form.getRawValue();

    this.store.start(club.clubId, subject.trim(), body.trim(), (conversationId) => {
      void this.router.navigate(['/inbox/managers', conversationId]);
    });
  }

  private load(clubId: string | null): void {
    this.club.set(null);
    this.loadError.set(null);

    if (clubId === null) {
      this.loading.set(false);

      return;
    }

    this.loading.set(true);

    this.squadApi.club(clubId).subscribe({
      next: (club) => {
        if (clubId === this.clubId()) {
          this.club.set(club);
          this.loading.set(false);
        }
      },
      error: (error: unknown) => {
        if (clubId === this.clubId()) {
          this.loading.set(false);
          this.loadError.set(
            error instanceof ApiError ? error.detail : 'That club could not be loaded.',
          );
        }
      },
    });
  }
}
