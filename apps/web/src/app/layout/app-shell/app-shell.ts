import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ConnectivityStore } from '../../core/connectivity/connectivity-store';
import { CorrelationStore } from '../../core/api/correlation-store';
import { SessionStore } from '../../core/auth/session-store';
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
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.css',
})
export class AppShell {
  private readonly connectivity = inject(ConnectivityStore);
  private readonly correlation = inject(CorrelationStore);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);

  /**
   * Navigable destinations.
   *
   * Every destination is manager-scoped, so the navigation is hidden entirely while signed out rather
   * than showing links that would bounce straight back to the sign-in page.
   */
  protected readonly navItems = computed(() =>
    this.session.isAuthenticated() ? AVAILABLE_NAV_ITEMS : [],
  );

  /** Whether the browser reports a connection. */
  protected readonly isOnline = this.connectivity.isOnline;

  /** The most recent server correlation ID, for support. */
  protected readonly correlationId = this.correlation.correlationId;

  /** Whether the client holds a session. */
  protected readonly isAuthenticated = this.session.isAuthenticated;

  /** The signed-in manager's name, or null. */
  protected readonly displayName = this.session.displayName;

  /** Ends the session on this device. */
  protected signOut(): void {
    this.session.logout().subscribe(() => {
      void this.router.navigateByUrl('/login');
    });
  }
}
