import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { SyncStore } from '../../core/sync/sync-store';

/**
 * The two sections of the inbox: what the game told the manager, and what other managers wrote.
 *
 * Two links rather than a tab widget, because each is its own route: the address says which section is open
 * and the back button works. The open one carries `aria-current="page"`. Each shows its unread count in words
 * for a screen reader, from the shell's poll so it needs no read of its own.
 */
@Component({
  selector: 'app-inbox-tabs',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav aria-label="Inbox sections" class="mt-4 flex gap-1 border-b border-line">
      @for (tab of tabs; track tab.path) {
        <a
          [routerLink]="tab.path"
          routerLinkActive="border-accent text-ink"
          ariaCurrentWhenActive="page"
          [routerLinkActiveOptions]="{ exact: tab.exact }"
          class="inline-flex min-h-11 items-center gap-2 border-b-2 border-transparent px-3 py-2 text-sm font-semibold text-ink-2 hover:text-ink focus:outline-none focus-visible:ring-2 focus-visible:ring-accent"
        >
          {{ tab.label }}
          @if (count(tab.key) > 0) {
            <span
              class="rounded-full bg-accent px-2 py-0.5 text-xs font-semibold text-on-accent"
              [attr.aria-label]="count(tab.key) + ' unread'"
            >
              {{ count(tab.key) }}
            </span>
          }
        </a>
      }
    </nav>
  `,
})
export class InboxTabs {
  private readonly sync = inject(SyncStore);

  protected readonly tabs = [
    { key: 'game', label: 'Game messages', path: '/inbox', exact: true },
    { key: 'managers', label: 'Managers', path: '/inbox/managers', exact: false },
  ] as const;

  protected count(key: 'game' | 'managers'): number {
    return key === 'game' ? this.sync.unreadInboxCount() : this.sync.unreadDirectMessageCount();
  }
}
