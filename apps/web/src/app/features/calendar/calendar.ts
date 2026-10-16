import {
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { CalendarEvent } from '../../core/calendar/calendar.models';
import {
  CalendarDay,
  MINUTES_PER_DAY,
  MonthRef,
  countLabel,
  dayKey,
  firstDayOfWeek,
  formatDay,
  formatHour,
  formatMonth,
  formatTime,
  gridRange,
  groupByDay,
  kindStyle,
  kindsOf,
  minuteOfDay,
  monthGrid,
  monthOf,
  monthOfKey,
  placeEvents,
  shiftDay,
  shiftMonth,
  weekdayNames,
} from '../../core/calendar/calendar-presentation';
import { CalendarStore } from '../../core/calendar/calendar-store';
import { signedAmount } from '../../core/finance/finance-presentation';
import { ResultGate } from '../../core/match/result-gate';
import { formatFunds } from '../../core/world/presentation';
import {
  FORM_ERROR,
  LINK_ACTION,
  PAGE_HEADING,
  SECONDARY_BUTTON,
} from '../../shared/forms/control-styles';

/**
 * How many rows of a day cell are events. When a day has more events than this, the last row becomes "n more"
 * and the one before it is given up to it, so the cell never grows and nothing is silently dropped.
 */
const CELL_ROWS = 3;

/** How tall one hour is in the hourly view, in pixels. */
const HOUR_PX = 64;

/** The shortest a block is drawn, so an event at the very end of the day stays readable. */
const MIN_BLOCK_PX = 28;

/** One cell of the month grid, with what it needs to draw. */
interface DayCell {
  readonly day: CalendarDay;
  readonly isToday: boolean;
  readonly shown: readonly CalendarEvent[];
  readonly more: number;
  readonly count: number;
  readonly label: string;
}

/** One event block of the hourly view, with where it is drawn. */
interface Block {
  readonly event: CalendarEvent;
  readonly top: number;
  readonly height: number;
  readonly left: number;
  readonly width: number;
}

/**
 * The calendar screen: what is happening at the club on each day.
 *
 * Two views, like any calendar. The month stacks each day's events in its cell as one-line chips, with an
 * "n more" row when there are too many to fit. Opening a day switches to the hourly view: the day laid out hour by
 * hour, each event at the time it happens, and events that overlap sharing the width between them.
 *
 * The screen knows no kind of event: every event arrives with its own title, detail, link and kind, and the
 * kind only decides which marker it wears (`calendar-presentation.ts`). A new source of events on the server
 * therefore shows up here as soon as it exists.
 *
 * "Today" is the game clock's day, which the server reports, not the device's: on a stepped development clock
 * the two differ by months. Days and hours are the manager's own zone's (`CAL-4`). A played match's result
 * stays held back until the manager has seen it, exactly as on the fixtures screen.
 */
@Component({
  selector: 'app-calendar',
  imports: [RouterLink],
  templateUrl: './calendar.html',
})
export class Calendar {
  private readonly store = inject(CalendarStore);
  private readonly gate = inject(ResultGate);

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  protected readonly loading = this.store.loading;
  protected readonly loadError = this.store.error;

  private readonly shownMonth = signal<MonthRef | null>(null);
  private readonly openedDay = signal<string | null>(null);
  private readonly mutedKinds = signal<ReadonlySet<string>>(new Set());

  /** The month on screen, or null until the server has said what day it is. */
  protected readonly month = this.shownMonth.asReadonly();

  /** The day open in the hourly view, or null while the month is showing. */
  protected readonly openDay = this.openedDay.asReadonly();

  /** The key of the game clock's day, or null before the first read. */
  protected readonly today = computed(() => {
    const window = this.store.window();

    return window === null ? null : dayKey(window.serverTime);
  });

  protected readonly monthTitle = computed(() => {
    const month = this.shownMonth();

    return month === null ? '' : formatMonth(month);
  });

  private readonly weekStartsOn = computed(() => firstDayOfWeek());

  protected readonly weekdays = computed(() => weekdayNames(this.weekStartsOn()));
  protected readonly weekdaysNarrow = computed(() =>
    weekdayNames(this.weekStartsOn(), undefined, 'narrow'),
  );

  private readonly days = computed(() => {
    const month = this.shownMonth();

    return month === null ? [] : monthGrid(month, this.weekStartsOn());
  });

  /** Every event of the window, whatever the filter says: the legend is built from these. */
  private readonly allEvents = computed(() => this.store.window()?.events ?? []);

  /** The kinds present in the window, for the legend. */
  protected readonly legend = computed(() =>
    kindsOf(this.allEvents()).map((kind) => ({
      kind,
      style: kindStyle(kind),
      shown: !this.mutedKinds().has(kind),
    })),
  );

  private readonly byDay = computed(() =>
    groupByDay(this.allEvents().filter((event) => !this.mutedKinds().has(event.kind))),
  );

  /** The cells of the month grid, in reading order. */
  protected readonly cells = computed<readonly DayCell[]>(() => {
    const today = this.today();
    const byDay = this.byDay();

    return this.days().map((day) => {
      const events = byDay.get(day.key) ?? [];
      const overflow = events.length > CELL_ROWS;

      return {
        day,
        isToday: day.key === today,
        shown: overflow ? events.slice(0, CELL_ROWS - 1) : events,
        more: overflow ? events.length - (CELL_ROWS - 1) : 0,
        count: events.length,
        label: this.cellLabel(day.key, events.length, day.key === today),
      };
    });
  });

  /** The open day's events, in time order. */
  protected readonly dayEvents = computed(() => {
    const key = this.openedDay();

    return key === null ? [] : (this.byDay().get(key) ?? []);
  });

  /** The open day's events as blocks on the hourly view, overlapping ones sharing the width. */
  protected readonly blocks = computed<readonly Block[]>(() =>
    placeEvents(this.dayEvents()).map((placed) => {
      const top = (placed.startMinute / 60) * HOUR_PX;

      return {
        event: placed.event,
        top,
        height: Math.max(((placed.endMinute - placed.startMinute) / 60) * HOUR_PX, MIN_BLOCK_PX),
        left: (placed.column / placed.columns) * 100,
        width: 100 / placed.columns,
      };
    }),
  );

  protected readonly hours = Array.from({ length: 24 }, (_, hour) => ({
    hour,
    top: hour * HOUR_PX,
    label: formatHour(hour),
  }));

  protected readonly timelineHeight = (MINUTES_PER_DAY / 60) * HOUR_PX;

  /** Where the game clock's "now" line sits, in pixels, or null when the open day is not today. */
  protected readonly nowTop = computed(() => {
    const window = this.store.window();

    return window !== null && this.openedDay() === this.today()
      ? (minuteOfDay(window.serverTime) / 60) * HOUR_PX
      : null;
  });

  protected readonly dayHeading = computed(() => {
    const key = this.openedDay();

    return key === null ? '' : formatDay(key);
  });

  /** Whether the open day is the game clock's day. */
  protected readonly openIsToday = computed(() => this.openedDay() === this.today());

  /** Whether the open day is behind the game clock's day. */
  protected readonly openIsPast = computed(() => {
    const key = this.openedDay();
    const today = this.today();

    return key !== null && today !== null && key < today;
  });

  /** Whether the screen is already showing today: the month's, or the day's. */
  protected readonly atToday = computed(() => {
    const today = this.today();

    if (today === null) {
      return false;
    }

    const open = this.openedDay();
    const month = this.shownMonth();

    return open !== null
      ? open === today
      : month !== null &&
          month.year === monthOfKey(today).year &&
          month.month === monthOfKey(today).month;
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly linkClass = LINK_ACTION;

  constructor() {
    // A fresh read each time the screen opens, so it never opens on a day the game has moved past.
    this.store.clear();
    this.store.load();
    this.gate.refresh();

    // The first answer says what day it is, which decides the month to draw.
    effect(() => {
      const window = this.store.window();

      if (window !== null && untracked(this.shownMonth) === null) {
        this.shownMonth.set(monthOf(window.serverTime));
      }
    });

    // Moving to a month the held window does not cover reads it.
    effect(() => {
      const days = this.days();

      if (days.length > 0) {
        const range = gridRange(days);

        untracked(() => this.store.ensure(range.from, range.to));
      }
    });

    // Opening a day scrolls the hourly view to where the day's action is, rather than to midnight.
    effect(() => {
      const element = this.scroller();
      const key = this.openedDay();

      if (element !== undefined && key !== null) {
        untracked(() => {
          element.nativeElement.scrollTop = this.firstVisibleHour() * HOUR_PX;
        });
      }
    });
  }

  protected previousMonth(): void {
    this.moveMonth(-1);
  }

  protected nextMonth(): void {
    this.moveMonth(1);
  }

  /** Goes back to the game clock's day, in whichever view is open. */
  protected goToToday(): void {
    const today = this.today();

    if (today === null) {
      return;
    }

    this.shownMonth.set(monthOfKey(today));

    if (this.openedDay() !== null) {
      this.openedDay.set(today);
    }
  }

  /** Opens a day's hourly view. */
  protected open(cell: DayCell): void {
    this.showDay(cell.day.key);
  }

  protected previousDay(): void {
    this.moveDay(-1);
  }

  protected nextDay(): void {
    this.moveDay(1);
  }

  /** Goes back from the hourly view to the month the day is in. */
  protected closeDay(): void {
    this.openedDay.set(null);
  }

  protected toggleKind(kind: string): void {
    this.mutedKinds.update((muted) => {
      const next = new Set(muted);

      if (!next.delete(kind)) {
        next.add(kind);
      }

      return next;
    });
  }

  protected style(kind: string) {
    return kindStyle(kind);
  }

  protected time(event: CalendarEvent): string {
    return formatTime(event.at);
  }

  protected amount(event: CalendarEvent): string {
    return signedAmount(event.amountMinor ?? 0, formatFunds);
  }

  protected isCredit(event: CalendarEvent): boolean {
    return (event.amountMinor ?? 0) > 0;
  }

  /**
   * Whether an event's detail is held back because it would give a result away. Held back until the manager's
   * fixture list has been read as well, so a result is never shown first and hidden after.
   */
  protected concealed(event: CalendarEvent): boolean {
    return (
      event.resultMatchId !== null &&
      (!this.gate.settled() || this.gate.isHidden(event.resultMatchId))
    );
  }

  protected revealResult(event: CalendarEvent): void {
    this.gate.reveal(event.resultMatchId);
  }

  protected retry(): void {
    this.store.load();
  }

  private showDay(key: string): void {
    this.openedDay.set(key);

    // A day in the grid's first or last row can belong to a neighbouring month; the month follows the day so
    // the window held covers it and "back to the month" lands on the day's own month.
    const month = monthOfKey(key);
    const shown = this.shownMonth();

    if (shown === null || shown.year !== month.year || shown.month !== month.month) {
      this.shownMonth.set(month);
    }
  }

  private moveDay(delta: number): void {
    const key = this.openedDay();

    if (key !== null) {
      this.showDay(shiftDay(key, delta));
    }
  }

  private moveMonth(delta: number): void {
    const month = this.shownMonth();

    if (month !== null) {
      this.shownMonth.set(shiftMonth(month, delta));
    }
  }

  /**
   * The hour the hourly view opens scrolled to: an hour before the day's first event, else a little before
   * the game clock's "now" on today, else the start of a working day.
   */
  private firstVisibleHour(): number {
    const first = this.blocks()[0];

    if (first !== undefined) {
      return Math.max(0, first.top / HOUR_PX - 1);
    }

    const now = this.nowTop();

    return now !== null ? Math.max(0, now / HOUR_PX - 2) : 8;
  }

  private cellLabel(key: string, count: number, isToday: boolean): string {
    const parts = [formatDay(key)];

    if (isToday) {
      parts.push('today');
    }

    parts.push(count === 0 ? 'nothing happening' : countLabel(count));

    return parts.join(', ');
  }
}
