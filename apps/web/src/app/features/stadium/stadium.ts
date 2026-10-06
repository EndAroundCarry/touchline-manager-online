import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import {
  fillPercent,
  levelProgress,
  levelRange,
  orderCost,
  placesPhrase,
  standLabel,
} from '../../core/stadium/stadium-presentation';
import { StadiumStore } from '../../core/stadium/stadium-store';
import { Stadium, StadiumStand, StadiumStandCode } from '../../core/stadium/stadium.models';
import { formatFunds, preferredLocale } from '../../core/world/presentation';
import {
  FORM_ERROR,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';
import { StadiumPicture } from './stadium-picture';

/** Order sizes a manager can reach with one tap. */
const QUICK_ORDERS: readonly number[] = [100, 500, 1_000];

/**
 * The stadium screen (`STAD-1`…`STAD-6`).
 *
 * A manager sees the ground as a picture in the club's colours, how big it is and how far it is from the next
 * level, and the four kinds of place with what each sells for, costs to add, and how full it gets. They can add
 * as many places of any kind as the club can pay for. Every price, cost and crowd comes from the server; the
 * screen only multiplies a quoted cost by the number being considered, so the figure shown beside the button is
 * the figure the server charges.
 */
@Component({
  selector: 'app-stadium',
  imports: [RouterLink, StadiumPicture],
  templateUrl: './stadium.html',
})
export class StadiumScreen {
  private readonly store = inject(StadiumStore);
  private readonly maintenance = inject(MaintenanceStore);

  protected readonly stadium = this.store.stadium;
  protected readonly loading = this.store.loading;
  protected readonly error = this.store.error;
  protected readonly building = this.store.building;
  protected readonly buildError = this.store.buildError;
  protected readonly builtMessage = this.store.builtMessage;
  protected readonly canMutate = this.maintenance.canMutate;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusClass = STATUS_MESSAGE;
  protected readonly textInputClass = TEXT_INPUT;
  protected readonly quickOrders = QUICK_ORDERS;

  /** The places the manager is considering, by stand. Absent means nothing typed yet. */
  private readonly counts = signal<Readonly<Partial<Record<StadiumStandCode, number>>>>({});

  /** Every level the stadium can reach, for the gallery. */
  protected readonly levels = computed(() => {
    const ground = this.stadium();

    return ground === null
      ? []
      : Array.from({ length: ground.maxLevel }, (_, index) => {
          const level = index + 1;
          const range = levelRange(level, ground.seatsPerLevel);

          return {
            level,
            from: range.from,
            to: range.to,
            state: level < ground.level ? 'Built' : level === ground.level ? 'Current' : 'Ahead',
          };
        });
  });

  /** The share of the current level that is built, for the progress bar. */
  protected readonly progress = computed(() => {
    const ground = this.stadium();

    return ground === null ? 0 : levelProgress(ground.capacity, ground.level, ground.seatsPerLevel);
  });

  constructor() {
    this.store.load();
  }

  /** Formats an amount for display. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Formats a count of places with the viewer's digit grouping. */
  protected number(value: number): string {
    return value.toLocaleString(preferredLocale());
  }

  /** The names and blurb of a stand. */
  protected label(stand: StadiumStand) {
    return standLabel(stand.stand);
  }

  /** How full a stand gets at a mid-table home match. */
  protected fill(stand: StadiumStand): number {
    return fillPercent(stand);
  }

  /** The places the manager has typed for a stand, or zero. */
  protected count(stand: StadiumStand): number {
    return this.counts()[stand.stand] ?? 0;
  }

  /** What the typed order would cost. */
  protected cost(stand: StadiumStand): number {
    return orderCost(stand, this.count(stand));
  }

  /** The most places the club could add of a stand, by money and by room, so the form can say so. */
  protected maxOrder(ground: Stadium, stand: StadiumStand): number {
    const room = ground.maxCapacity - ground.capacity;
    const affordable =
      stand.buildCostMinor === 0 ? room : Math.floor(ground.availableMinor / stand.buildCostMinor);

    return Math.max(0, Math.min(room, affordable));
  }

  /** Why the typed order cannot be placed, or null when it can. */
  protected problem(ground: Stadium, stand: StadiumStand): string | null {
    const count = this.count(stand);

    if (count === 0) {
      return null;
    }

    if (count > ground.maxCapacity - ground.capacity) {
      return `The ground has room for ${this.number(ground.maxCapacity - ground.capacity)} more places.`;
    }

    if (this.cost(stand) > ground.availableMinor) {
      return `The club can pay for ${this.number(this.maxOrder(ground, stand))} of these, not ${this.number(count)}.`;
    }

    return null;
  }

  /** Whether the order for a stand can be placed now. */
  protected canBuild(ground: Stadium, stand: StadiumStand): boolean {
    return (
      this.count(stand) > 0 &&
      this.problem(ground, stand) === null &&
      this.building() === null &&
      this.canMutate()
    );
  }

  /** What the ground would hold after the typed order. */
  protected capacityAfter(ground: Stadium, stand: StadiumStand): number {
    return ground.capacity + this.count(stand);
  }

  /** Records what was typed into a stand's order box. A blank or invalid entry is no order. */
  protected onCount(stand: StadiumStand, event: Event): void {
    const raw = (event.target as HTMLInputElement).value;
    const parsed = Number.parseInt(raw, 10);

    this.setCount(stand, Number.isFinite(parsed) && parsed > 0 ? parsed : 0);
  }

  /** Fills a stand's order box with a quick amount. */
  protected quick(stand: StadiumStand, amount: number): void {
    this.setCount(stand, amount);
  }

  /** Places the order for a stand. */
  protected build(stand: StadiumStand): void {
    const ground = this.stadium();

    if (ground === null || !this.canBuild(ground, stand)) {
      return;
    }

    const count = this.count(stand);

    // The box is emptied before the order goes, because emptying it also clears the last confirmation and
    // the new order's own confirmation must not be the one that is cleared.
    this.setCount(stand, 0);
    this.store.build(stand.stand, count, placesPhrase(stand.stand, count));
  }

  /** Describes how many places remain before the next level, for the level card. */
  protected untilNextLevel(ground: Stadium): string {
    return ground.level >= ground.maxLevel
      ? 'This is the largest stadium there is.'
      : `${this.number(ground.seatsToNextLevel)} more ${ground.seatsToNextLevel === 1 ? 'place takes' : 'places take'} the stadium to level ${ground.level + 1}, and its picture changes.`;
  }

  /** Names an order, e.g. "100 seats". */
  protected phrase(stand: StadiumStand, count: number): string {
    return placesPhrase(stand.stand, count);
  }

  private setCount(stand: StadiumStand, count: number): void {
    this.store.dismissMessages();
    this.counts.update((current) => ({ ...current, [stand.stand]: count }));
  }
}
