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

  /**
   * The rail section the destination sits under. Sections are the runs of consecutive entries that share a
   * group, so the order below decides where each heading appears.
   */
  readonly group: string;
}

/** Planned shell navigation, in product order. */
export const NAV_ITEMS: readonly NavItem[] = [
  { path: '/dashboard', label: 'Dashboard', icon: 'pi pi-home', available: true, group: 'Club' },
  { path: '/squad', label: 'Squad', icon: 'pi pi-users', available: true, group: 'Team' },
  { path: '/tactics', label: 'Tactics', icon: 'pi pi-sitemap', available: true, group: 'Team' },
  {
    path: '/training',
    label: 'Training',
    icon: 'pi pi-chart-line',
    available: true,
    group: 'Team',
  },
  {
    path: '/competitions',
    label: 'Competitions',
    icon: 'pi pi-list',
    available: true,
    group: 'Season',
  },
  { path: '/history', label: 'Seasons', icon: 'pi pi-history', available: true, group: 'Season' },
  {
    path: '/fixtures',
    label: 'Fixtures',
    icon: 'pi pi-calendar',
    available: true,
    group: 'Season',
  },
  { path: '/scouting', label: 'Scouting', icon: 'pi pi-search', available: true, group: 'Market' },
  {
    path: '/transfers',
    label: 'Transfers',
    icon: 'pi pi-arrow-right-arrow-left',
    available: true,
    group: 'Market',
  },
  { path: '/finances', label: 'Finances', icon: 'pi pi-wallet', available: true, group: 'Market' },
  {
    path: '/stadium',
    label: 'Stadium',
    icon: 'pi pi-building',
    available: true,
    group: 'Facilities',
  },
  { path: '/inbox', label: 'Inbox', icon: 'pi pi-inbox', available: true, group: 'Messages' },
  { path: '/news', label: 'News', icon: 'pi pi-megaphone', available: true, group: 'Messages' },
  { path: '/settings', label: 'Settings', icon: 'pi pi-cog', available: true, group: 'Game' },
  { path: '/help', label: 'Help', icon: 'pi pi-question-circle', available: true, group: 'Game' },
];

/** Entries the current shell can actually navigate to. */
export const AVAILABLE_NAV_ITEMS: readonly NavItem[] = NAV_ITEMS.filter((item) => item.available);
