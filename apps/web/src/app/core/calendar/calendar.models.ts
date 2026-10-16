/**
 * The calendar's transport mirror.
 *
 * Hand-written to match `Tactical11Manager.Contracts.Calendar`. The text of an event is finished prose from the
 * server and the kind is an open string, so a new kind of event reaches the screen without a model change.
 */

/** Whether an event has already happened against the game clock. */
export type CalendarEventState = 'upcoming' | 'done';

/** One thing that happens, or happened, on a day. */
export interface CalendarEvent {
  /** A stable identity, unique within a response. */
  readonly id: string;

  /** What the event is. Open: a kind this build does not know is drawn with a generic marker. */
  readonly kind: string;

  /** When it happens, as a UTC instant. The screen files it under the manager's own day. */
  readonly at: string;

  readonly state: CalendarEventState;
  readonly title: string;
  readonly detail: string | null;

  /** An app route that opens the thing the event is about. */
  readonly link: string | null;

  /** A signed cash amount in minor units, or null when the event moves no money. */
  readonly amountMinor: number | null;

  /** The match whose unseen result `detail` would give away, or null. */
  readonly resultMatchId: string | null;
}

/** A window of the calendar. */
export interface CalendarWindow {
  readonly from: string;
  readonly to: string;
  readonly events: readonly CalendarEvent[];

  /** The game clock's current instant, which is what "today" means. */
  readonly serverTime: string;
}
