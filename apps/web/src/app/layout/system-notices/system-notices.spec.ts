import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConnectivityStore } from '../../core/connectivity/connectivity-store';
import { UpdateStore } from '../../core/pwa/update-store';
import { SyncStore } from '../../core/sync/sync-store';
import { SystemNotices } from './system-notices';

/**
 * The system notices (`§11.4`, ADR-0007, `F-45`).
 *
 * The states are separate claims, so the tests check they do not smear into one another: offline reads
 * on its own, the stale notice appears only while online, and the update prompt offers a reload — but
 * not a "later" for an unrecoverable version, which a reload is the only answer to.
 */
describe('SystemNotices', () => {
  let fixture: ComponentFixture<SystemNotices>;
  let online: WritableSignal<boolean>;
  let refreshFailed: WritableSignal<boolean>;
  let lastRefreshedAt: WritableSignal<number | null>;
  let updateReady: WritableSignal<boolean>;
  let unrecoverable: WritableSignal<boolean>;
  let retry: ReturnType<typeof vi.fn>;
  let reload: ReturnType<typeof vi.fn>;
  let dismiss: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    online = signal(true);
    refreshFailed = signal(false);
    lastRefreshedAt = signal<number | null>(null);
    updateReady = signal(false);
    unrecoverable = signal(false);
    retry = vi.fn();
    reload = vi.fn();
    dismiss = vi.fn();

    await TestBed.configureTestingModule({
      imports: [SystemNotices],
      providers: [
        { provide: ConnectivityStore, useValue: { isOnline: online } },
        { provide: SyncStore, useValue: { refreshFailed, lastRefreshedAt, retry } },
        { provide: UpdateStore, useValue: { updateReady, unrecoverable, reload, dismiss } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SystemNotices);
  });

  async function render(): Promise<HTMLElement> {
    await fixture.whenStable();

    return fixture.nativeElement as HTMLElement;
  }

  function button(element: HTMLElement, label: string): HTMLButtonElement {
    const match = [...element.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === label,
    );

    if (match === undefined) {
      throw new Error(`No button labelled "${label}"`);
    }

    return match;
  }

  it('tells the manager they are offline and says why changes are blocked', async () => {
    online.set(false);

    const element = await render();

    expect(element.textContent).toContain('You are offline');
    expect(element.textContent).toContain('changes are disabled');
  });

  it('says nothing when online, fresh, and up to date', async () => {
    const element = await render();

    expect(element.textContent?.trim()).toBe('');
  });

  it('labels older data when a refresh could not reach the server while online', async () => {
    refreshFailed.set(true);
    lastRefreshedAt.set(Date.UTC(2026, 9, 6, 19, 0));

    const element = await render();

    expect(element.textContent).toContain('could not refresh');
    expect(element.textContent).toContain('Last updated');

    button(element, 'Retry').click();

    expect(retry).toHaveBeenCalledOnce();
  });

  it('shows only the offline notice while offline, even after a failed refresh', async () => {
    online.set(false);
    refreshFailed.set(true);

    const element = await render();

    expect(element.textContent).toContain('You are offline');
    expect(element.textContent).not.toContain('could not refresh');
  });

  it('offers a reload and a later for a ready update', async () => {
    updateReady.set(true);

    const element = await render();

    expect(element.textContent).toContain('A new version');

    button(element, 'Reload').click();
    expect(reload).toHaveBeenCalledOnce();

    button(element, 'Later').click();
    expect(dismiss).toHaveBeenCalledOnce();
  });

  it('offers only a reload when the running version is unrecoverable', async () => {
    unrecoverable.set(true);

    const element = await render();

    expect(element.textContent).toContain('can no longer load its files');
    expect(element.textContent).not.toContain('Later');

    button(element, 'Reload').click();
    expect(reload).toHaveBeenCalledOnce();
  });
});
