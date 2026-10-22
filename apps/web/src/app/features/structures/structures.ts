import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { signedAmount } from '../../core/finance/finance-presentation';
import {
  STRUCTURE_KINDS,
  bonusLabel,
  bonusPercent,
  buildDaysPhrase,
  customerPhrase,
  kindLabel,
  levelPips,
  readyOn,
  resultLabel,
  upgradeBlockedReason,
  weekLabel,
} from '../../core/structures/structures-presentation';
import { StructuresStore } from '../../core/structures/structures-store';
import { Structure, StructureKind } from '../../core/structures/structures.models';
import { OnboardingStore } from '../../core/world/onboarding-store';
import { formatFunds, preferredLocale } from '../../core/world/presentation';
import {
  FORM_ERROR,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  STATUS_MESSAGE,
} from '../../shared/forms/control-styles';
import { EmbargoNotice } from '../../shared/ui/embargo-notice/embargo-notice';
import { StructurePicture } from './structure-picture';

/**
 * The structures screen (`STRC-1`…`STRC-9`).
 *
 * Two buildings, each on its own tab: the snack bar, which earns from every spectator at a home match, and the
 * merchandise shop, which earns from every fan each week. A tab shows a picture of the building at its level in the
 * club's colours, as the stadium screen does; the level, what a customer spends and what the building costs to keep;
 * the next level with its price, build time and the profit it should add; the bonus table with the line that applies
 * marked; and the building's recent books. Every amount comes from the server; the screen formats them and never
 * works one out.
 */
@Component({
  selector: 'app-structures',
  imports: [RouterLink, StructurePicture, EmbargoNotice],
  templateUrl: './structures.html',
})
export class StructuresScreen {
  private readonly store = inject(StructuresStore);
  private readonly maintenance = inject(MaintenanceStore);
  private readonly onboarding = inject(OnboardingStore);

  /** The club's two colours, for the picture; null until the manager's club has been read, which draws the fallbacks. */
  protected readonly colours = computed(() => {
    const tenure = this.onboarding.state()?.tenure ?? null;

    return {
      primary: tenure?.primaryColour ?? null,
      secondary: tenure?.secondaryColour ?? null,
    };
  });

  protected readonly structures = this.store.structures;
  protected readonly loading = this.store.loading;
  protected readonly error = this.store.error;
  protected readonly upgrading = this.store.upgrading;
  protected readonly upgradeError = this.store.upgradeError;
  protected readonly upgradeEmbargoed = this.store.upgradeEmbargoed;
  protected readonly upgradedMessage = this.store.upgradedMessage;
  protected readonly canMutate = this.maintenance.canMutate;

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly statusClass = STATUS_MESSAGE;

  /** The tabs, in the order they are shown. The snack bar is the default. */
  protected readonly tabs = STRUCTURE_KINDS.map((kind) => ({ key: kind, label: kindLabel(kind) }));

  /** The tab currently open. */
  protected readonly activeTab = signal<StructureKind>('snack_bar');

  /** The building on the open tab. */
  protected readonly active = computed<Structure | null>(() => {
    const both = this.structures();

    if (both === null) {
      return null;
    }

    return this.activeTab() === 'snack_bar' ? both.snackBar : both.merchandise;
  });

  constructor() {
    this.store.load();
  }

  /** Opens a tab. */
  protected selectTab(tab: StructureKind): void {
    this.store.dismissMessages();
    this.activeTab.set(tab);
  }

  /** Moves between tabs with the arrow keys, Home and End, as the tabs pattern expects. */
  protected onTabKeydown(event: KeyboardEvent, index: number): void {
    const last = this.tabs.length - 1;
    const target =
      event.key === 'ArrowRight'
        ? (index + 1) % this.tabs.length
        : event.key === 'ArrowLeft'
          ? (index + last) % this.tabs.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? last
              : -1;

    if (target < 0) {
      return;
    }

    event.preventDefault();
    this.selectTab(this.tabs[target].key);
    (
      document.getElementById(`structures-tab-${this.tabs[target].key}`) as HTMLElement | null
    )?.focus();
  }

  /** Formats an amount for display. */
  protected funds(minorUnits: number): string {
    return formatFunds(minorUnits);
  }

  /** Formats a signed movement so a loss never looks like a gain by colour alone. */
  protected signed(minorUnits: number): string {
    return signedAmount(minorUnits, formatFunds);
  }

  /** Formats a count with the viewer's digit grouping. */
  protected number(value: number): string {
    return value.toLocaleString(preferredLocale());
  }

  protected kindLabel = kindLabel;
  protected customerPhrase = customerPhrase;
  protected bonusLabel = bonusLabel;
  protected bonusPercent = bonusPercent;
  protected buildDaysPhrase = buildDaysPhrase;
  protected levelPips = levelPips;
  protected readyOn = readyOn;
  protected resultLabel = resultLabel;
  protected weekLabel = weekLabel;

  /** Why the next level cannot be ordered, or null when it can. */
  protected blockedReason(structure: Structure): string | null {
    return upgradeBlockedReason(
      structure,
      this.structures()?.availableCashMinor ?? 0,
      this.canMutate(),
    );
  }

  /** Orders the next level of the building on the open tab. */
  protected upgrade(structure: Structure): void {
    if (this.blockedReason(structure) !== null || this.upgrading() !== null) {
      return;
    }

    this.store.upgrade(structure.kind);
  }
}
