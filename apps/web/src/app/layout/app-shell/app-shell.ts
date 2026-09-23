import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ConnectivityStore } from '../../core/connectivity/connectivity-store';
import { CorrelationStore } from '../../core/api/correlation-store';
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

  /** Navigable destinations. Entries whose stage has not landed are omitted. */
  protected readonly navItems = AVAILABLE_NAV_ITEMS;

  /** Whether the browser reports a connection. */
  protected readonly isOnline = this.connectivity.isOnline;

  /** The most recent server correlation ID, for support. */
  protected readonly correlationId = this.correlation.correlationId;
}
