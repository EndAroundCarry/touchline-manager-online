/**
 * A navigation destination in the authenticated shell.
 *
 * The list is the target navigation from master plan §11.1. Entries stay here as they are designed
 * and flip to `available: true` when their stage lands, so the shell never links to a route that
 * does not exist and the remaining work stays visible in one place.
 */
export interface NavItem {
  /** Route path, relative to the shell's root. */
  readonly path: string;

  /** Label shown in the sidebar and the mobile bar. */
  readonly label: string;

  /** PrimeIcons class used as the visual marker. Never the only signal (see `label`). */
  readonly icon: string;

  /** Whether the destination is implemented. Unavailable entries are not rendered. */
  readonly available: boolean;
}

/** Planned shell navigation, in product order. */
export const NAV_ITEMS: readonly NavItem[] = [
  { path: '/dashboard', label: 'Dashboard', icon: 'pi pi-home', available: false },
  { path: '/squad', label: 'Squad', icon: 'pi pi-users', available: false },
  { path: '/tactics', label: 'Tactics', icon: 'pi pi-sitemap', available: false },
  { path: '/training', label: 'Training', icon: 'pi pi-chart-line', available: false },
  { path: '/competitions', label: 'Competitions', icon: 'pi pi-list', available: false },
  { path: '/fixtures', label: 'Fixtures', icon: 'pi pi-calendar', available: false },
  { path: '/scouting', label: 'Scouting', icon: 'pi pi-search', available: false },
  { path: '/transfers', label: 'Transfers', icon: 'pi pi-exchange', available: false },
  { path: '/finances', label: 'Finances', icon: 'pi pi-wallet', available: false },
  { path: '/inbox', label: 'Inbox', icon: 'pi pi-inbox', available: false },
  { path: '/settings', label: 'Settings', icon: 'pi pi-cog', available: false },
];

/** Entries the current shell can actually navigate to. */
export const AVAILABLE_NAV_ITEMS: readonly NavItem[] = NAV_ITEMS.filter((item) => item.available);
