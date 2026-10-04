import { Location } from '@angular/common';
import {
  Component,
  HostListener,
  OnDestroy,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Subscription, filter } from 'rxjs';
import { CorrelationStore } from '../../core/api/correlation-store';
import { SessionStore } from '../../core/auth/session-store';
import { lockCountdown, venueLabel } from '../../core/competition/competition-presentation';
import { CompetitionStore } from '../../core/competition/competition-store';
import { GameClockStore } from '../../core/devtools/game-clock-store';
import { InboxStore } from '../../core/inbox/inbox-store';
import { MatchStore } from '../../core/match/match-store';
import { NewsStore } from '../../core/news/news-store';
import { NotificationPreferencesStore } from '../../core/notifications/notification-preferences-store';
import { SessionsStore } from '../../core/sessions/sessions-store';
import { SquadStore } from '../../core/squad/squad-store';
import { SyncStore } from '../../core/sync/sync-store';
import { ThemeStore } from '../../core/theme/theme-store';
import { TacticsStore } from '../../core/tactics/tactics-store';
import { PlayerTrainingStore } from '../../core/training/player-training-store';
import { TrainingStore } from '../../core/training/training-store';
import { OnboardingStore } from '../../core/world/onboarding-store';
import { GameClockBar } from '../game-clock-bar/game-clock-bar';
import { SystemNotices } from '../system-notices/system-notices';
import { AVAILABLE_NAV_ITEMS, NavItem } from '../navigation/nav-items';

/**
 * The responsive application shell.
 *
 * Desktop shows a persistent sidebar with dense content; mobile collapses to a compact top bar
 * (master plan §11.3). Both share one navigation model, so a destination cannot exist on one
 * layout and be missing from the other.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SystemNotices, GameClockBar],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.css',
})
export class AppShell implements OnDestroy {
  private readonly correlation = inject(CorrelationStore);
  private readonly session = inject(SessionStore);
  private readonly onboarding = inject(OnboardingStore);
  private readonly squad = inject(SquadStore);
  private readonly tactics = inject(TacticsStore);
  private readonly training = inject(TrainingStore);
  private readonly playerTraining = inject(PlayerTrainingStore);
  private readonly competition = inject(CompetitionStore);
  private readonly inbox = inject(InboxStore);
  private readonly news = inject(NewsStore);
  private readonly notifications = inject(NotificationPreferencesStore);
  private readonly sessions = inject(SessionsStore);
  private readonly match = inject(MatchStore);
  private readonly sync = inject(SyncStore);
  private readonly gameClock = inject(GameClockStore);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly theme = inject(ThemeStore);

  /** The route-change subscription that moves focus to the main content (`§11.3`). */
  private readonly navigationSubscription: Subscription;

  /** Whether the initial navigation has been seen, so focus is not stolen on first load. */
  private isInitialNavigation = true;

  constructor() {
    // The poll starts with the shell and stops with it, so the badge is fresh on every screen and no timer
    // outlives the shell that owns it (§11.2).
    this.sync.start();

    // The top bar's "Prepare" button needs the next fixture on every screen, not only on the dashboard. A signed-in
    // manager's fixture list is read once, here, if no screen has read it; a failed read is not retried in a loop
    // (a manager with no club has no list, and the dashboard says so).
    effect(() => {
      if (
        this.session.isAuthenticated() &&
        this.competition.fixtures() === null &&
        !this.competition.fixturesLoading() &&
        this.competition.fixturesError() === null
      ) {
        this.competition.loadFixtures();
      }
    });

    // A single-page app does not move focus when the view swaps, so a keyboard manager is left on the link
    // they pressed while the page under them changed (WCAG 2.4.3). Focus the main landmark on every
    // navigation after the first, so the new page's heading is read next; the router already sets the
    // document title, so only focus was missing.
    this.navigationSubscription = this.router.events
      .pipe(filter((event) => event instanceof NavigationEnd))
      .subscribe(() => {
        if (this.isInitialNavigation) {
          this.isInitialNavigation = false;

          return;
        }

        document.getElementById('main-content')?.focus();
      });
  }

  /**
   * Navigable destinations.
   *
   * Every destination is manager-scoped, so the navigation is hidden entirely while signed out rather
   * than showing links that would bounce straight back to the sign-in page.
   */
  protected readonly navItems = computed(() =>
    this.session.isAuthenticated() ? AVAILABLE_NAV_ITEMS : [],
  );

  /**
   * The navigation grouped into the rail's sections: each is a labelled list under a quiet heading, so fourteen
   * entries read as six short lists rather than one long one. Sections are the runs of consecutive entries that
   * share a group (`nav-items.ts`).
   */
  protected readonly railGroups = computed(() => {
    const groups: { name: string; items: NavItem[] }[] = [];

    for (const item of this.navItems()) {
      const last = groups[groups.length - 1];

      if (last !== undefined && last.name === item.group) {
        last.items.push(item);
      } else {
        groups.push({ name: item.group, items: [item] });
      }
    }

    return groups;
  });

  /**
   * The manager's next fixture, for the top bar's call to action, or null while there is none or the fixture
   * list has not been read yet.
   */
  protected readonly nextFixture = computed(() => {
    const list = this.competition.fixtures();

    if (list === null || list.nextFixtureId === null) {
      return null;
    }

    const fixture = list.fixtures.find((candidate) => candidate.id === list.nextFixtureId);

    if (fixture === undefined) {
      return null;
    }

    return {
      id: fixture.id,
      label: `${venueLabel(fixture.venue)} v ${fixture.opponentShortName}`,
      countdown: lockCountdown(fixture.lockAt, new Date()),
    };
  });

  /** The most recent server correlation ID, for support. */
  protected readonly correlationId = this.correlation.correlationId;

  /** Whether the client holds a session. */
  protected readonly isAuthenticated = this.session.isAuthenticated;

  /** The signed-in manager's name, or null. */
  protected readonly displayName = this.session.displayName;

  /** How many inbox messages are unread, for the navigation badge (`F-41`). */
  protected readonly unreadInboxCount = this.sync.unreadInboxCount;

  /**
   * Whether the mobile navigation panel is open.
   *
   * Below `md` the sidebar is replaced by a disclosure in the header; the panel is a plain vertical
   * list of the same destinations (`F-44`, master plan §11.3). It is a disclosure rather than a
   * drag drawer so it needs no focus trap and closes on Escape and on the link that is chosen.
   */
  protected readonly mobileNavOpen = signal(false);

  /** Opens or closes the mobile navigation panel. */
  protected toggleMobileNav(): void {
    this.mobileNavOpen.update((open) => !open);
  }

  /** The theme in force, for the toggle's label and icon. */
  protected readonly themeMode = this.theme.mode;

  /** Switches between the dark and the light theme. */
  protected toggleTheme(): void {
    this.theme.toggle();
  }

  /** Steps back through the browser history, like the arrows at the top of a management sim. */
  protected back(): void {
    this.location.back();
  }

  /** Steps forward through the browser history. */
  protected forward(): void {
    this.location.forward();
  }

  /** Closes the mobile navigation panel, e.g. after a destination is chosen. */
  protected closeMobileNav(): void {
    this.mobileNavOpen.set(false);
  }

  /** Closes the mobile navigation panel on Escape, the expected dismissal for a disclosure. */
  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.closeMobileNav();
  }

  /** Stops the synchronization poll and the focus subscription when the shell goes away. */
  ngOnDestroy(): void {
    this.sync.stop();
    this.navigationSubscription.unsubscribe();
  }

  /** Ends the session on this device. */
  protected signOut(): void {
    this.session.logout().subscribe(() => {
      // The onboarding and squad stores are dropped too, so a shared device does not keep the previous
      // manager's club, players, attributes, plan, fixtures, or last match on screen for whoever signs in next. The
      // sync count is dropped for the same reason: the badge must not outlive the session that produced it.
      this.onboarding.clear();
      this.squad.clear();
      this.tactics.clear();
      this.training.clear();
      this.playerTraining.clear();
      this.competition.clear();
      this.inbox.clear();
      this.news.clear();
      this.notifications.clear();
      this.sessions.clear();
      this.match.clear();
      this.sync.clear();
      this.gameClock.clear();

      void this.router.navigateByUrl('/login');
    });
  }
}
