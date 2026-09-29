import { Component, HostListener, OnDestroy, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { CorrelationStore } from '../../core/api/correlation-store';
import { SessionStore } from '../../core/auth/session-store';
import { CompetitionStore } from '../../core/competition/competition-store';
import { InboxStore } from '../../core/inbox/inbox-store';
import { MatchStore } from '../../core/match/match-store';
import { NewsStore } from '../../core/news/news-store';
import { NotificationPreferencesStore } from '../../core/notifications/notification-preferences-store';
import { SessionsStore } from '../../core/sessions/sessions-store';
import { SquadStore } from '../../core/squad/squad-store';
import { SyncStore } from '../../core/sync/sync-store';
import { TacticsStore } from '../../core/tactics/tactics-store';
import { OnboardingStore } from '../../core/world/onboarding-store';
import { SystemNotices } from '../system-notices/system-notices';
import { AVAILABLE_NAV_ITEMS } from '../navigation/nav-items';

/**
 * The responsive application shell.
 *
 * Desktop shows a persistent sidebar with dense content; mobile collapses to a compact top bar
 * (master plan §11.3). Both share one navigation model, so a destination cannot exist on one
 * layout and be missing from the other.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SystemNotices],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.css',
})
export class AppShell implements OnDestroy {
  private readonly correlation = inject(CorrelationStore);
  private readonly session = inject(SessionStore);
  private readonly onboarding = inject(OnboardingStore);
  private readonly squad = inject(SquadStore);
  private readonly tactics = inject(TacticsStore);
  private readonly competition = inject(CompetitionStore);
  private readonly inbox = inject(InboxStore);
  private readonly news = inject(NewsStore);
  private readonly notifications = inject(NotificationPreferencesStore);
  private readonly sessions = inject(SessionsStore);
  private readonly match = inject(MatchStore);
  private readonly sync = inject(SyncStore);
  private readonly router = inject(Router);

  constructor() {
    // The poll starts with the shell and stops with it, so the badge is fresh on every screen and no timer
    // outlives the shell that owns it (§11.2).
    this.sync.start();
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

  /** Closes the mobile navigation panel, e.g. after a destination is chosen. */
  protected closeMobileNav(): void {
    this.mobileNavOpen.set(false);
  }

  /** Closes the mobile navigation panel on Escape, the expected dismissal for a disclosure. */
  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.closeMobileNav();
  }

  /** Stops the synchronization poll when the shell goes away. */
  ngOnDestroy(): void {
    this.sync.stop();
  }

  /** Ends the session on this device. */
  protected signOut(): void {
    this.session.logout().subscribe(() => {
      // The onboarding and squad stores are dropped too, so a shared device does not keep the previous
      // manager's club, players, plan, fixtures, or last match on screen for whoever signs in next. The
      // sync count is dropped for the same reason: the badge must not outlive the session that produced it.
      this.onboarding.clear();
      this.squad.clear();
      this.tactics.clear();
      this.competition.clear();
      this.inbox.clear();
      this.news.clear();
      this.notifications.clear();
      this.sessions.clear();
      this.match.clear();
      this.sync.clear();

      void this.router.navigateByUrl('/login');
    });
  }
}
