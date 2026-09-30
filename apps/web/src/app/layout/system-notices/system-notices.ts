import { Component, computed, inject } from '@angular/core';
import { ConnectivityStore } from '../../core/connectivity/connectivity-store';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { UpdateStore } from '../../core/pwa/update-store';
import { SyncStore } from '../../core/sync/sync-store';
import { formatInstant } from '../../core/world/presentation';
import { SECONDARY_BUTTON } from '../../shared/forms/control-styles';

/**
 * The shell's system notices: maintenance, offline, stale, and update banners in one place
 * (master plan §11.4, ADR-0007).
 *
 * Four different facts share one surface and one ordering, so they live in one small component rather
 * than in the shell beside the whole game state. Read-only mode is a server decision that refuses every
 * command, so it reads first; offline is the strongest claim about the connection, so it reads next; the
 * stale notice appears only while online, when the last read did not reach the server; the update prompt
 * is the calmest and last.
 */
@Component({
  selector: 'app-system-notices',
  templateUrl: './system-notices.html',
})
export class SystemNotices {
  private readonly connectivity = inject(ConnectivityStore);
  private readonly maintenance = inject(MaintenanceStore);
  private readonly sync = inject(SyncStore);
  private readonly update = inject(UpdateStore);

  protected readonly isOnline = this.connectivity.isOnline;
  protected readonly readOnly = this.maintenance.readOnly;
  protected readonly readOnlyMessage = this.maintenance.message;
  protected readonly refreshFailed = this.sync.refreshFailed;
  protected readonly updateReady = this.update.updateReady;
  protected readonly unrecoverable = this.update.unrecoverable;

  /** A read that could not reach the server while the browser still claims to be online. */
  protected readonly showStale = computed(() => this.isOnline() && this.refreshFailed());

  /** A downloaded version, or a version the worker can no longer serve; both want a reload. */
  protected readonly showUpdate = computed(() => this.updateReady() || this.unrecoverable());

  /** When the last successful read landed, in the manager's zone, or null before the first. */
  protected readonly lastRefreshed = computed(() => {
    const at = this.sync.lastRefreshedAt();

    return at === null ? null : formatInstant(new Date(at).toISOString());
  });

  protected readonly secondaryButtonClass = SECONDARY_BUTTON;

  /** Reads again on the manager's request. */
  protected retry(): void {
    this.sync.retry();
  }

  /** Activates the downloaded version and reloads onto it. */
  protected reload(): void {
    this.update.reload();
  }

  /** Defers the update. */
  protected dismiss(): void {
    this.update.dismiss();
  }
}
