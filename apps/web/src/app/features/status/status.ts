import { Component, OnDestroy, OnInit, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StatusStore } from '../../core/status/status-store';
import { PublicStatus } from '../../core/status/status.models';
import { formatDeadline, formatInstant } from '../../core/world/presentation';
import { LINK_ACTION, PAGE_HEADING, SECONDARY_BUTTON } from '../../shared/forms/control-styles';

/** What the status page renders, derived from the store's last read. */
interface StatusView {
  readonly maintenance: boolean;
  readonly label: string;
  readonly message: string | null;
  readonly seasonNumber: number | null;
  readonly nextMatchdayAt: string | null;
  readonly asOf: string;
}

/**
 * Service status (master plan §16 Stage 15, `F-55`, ADR-0050).
 *
 * A public page: it reads the one anonymous endpoint and shows whether the game is operational or in
 * read-only maintenance (and why), the running season, and the next matchday. It polls while it is open so
 * the answer stays current, and it states a failed read rather than pretending the service is down.
 *
 * The state is never carried by colour alone: the words differ ("Operational" against "Maintenance") and the
 * operator's reason is shown verbatim.
 */
@Component({
  selector: 'app-status',
  imports: [RouterLink],
  templateUrl: './status.html',
})
export class Status implements OnInit, OnDestroy {
  /** The status store, exposed to the template for its `loading` and `failed` signals. */
  protected readonly store = inject(StatusStore);

  /** The last read, as the view model, or null before the first successful read. */
  protected readonly view = computed<StatusView | null>(() => {
    const status: PublicStatus | null = this.store.status();

    if (status === null) {
      return null;
    }

    return {
      maintenance: status.readOnly,
      label: status.readOnly ? 'Maintenance' : 'Operational',
      message: status.readOnlyMessage,
      seasonNumber: status.seasonNumber,
      nextMatchdayAt: status.nextMatchdayAt,
      asOf: status.serverTime,
    };
  });

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly linkActionClass = LINK_ACTION;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;

  /** Starts the poll while the page is open. */
  ngOnInit(): void {
    this.store.start();
  }

  /** Stops the poll when the page goes away. */
  ngOnDestroy(): void {
    this.store.stop();
  }

  /** Reads again on the manager's request. */
  protected refresh(): void {
    this.store.refresh();
  }

  /** Renders an instant in the viewer's zone (`CAL-4`). */
  protected asOf(instant: string): string {
    return formatInstant(instant);
  }

  /** Renders a kickoff as a deadline, naming the zone it falls in (`VOI-4`). */
  protected kickoff(instant: string): string {
    return formatDeadline(instant);
  }
}
