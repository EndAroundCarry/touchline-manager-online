import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { StatusStore } from '../../core/status/status-store';
import { PublicStatus } from '../../core/status/status.models';
import { Status } from './status';

/**
 * The service status page (`F-55`, ADR-0050).
 *
 * The page's job is to turn the store's last read into a state a person can act on: operational or in
 * maintenance, and never by colour alone. These tests pin that mapping, including the two cases that are easy
 * to get wrong — a read that failed, and a world that has not been started.
 */
describe('Status', () => {
  const operational: PublicStatus = {
    serverTime: '2026-10-06T18:00:00Z',
    readOnly: false,
    readOnlyMessage: null,
    seasonNumber: 3,
    nextMatchdayAt: '2026-10-06T19:00:00Z',
    documents: { termsVersion: '2026-01-01', privacyVersion: '2026-01-01' },
  };

  function create(
    status: PublicStatus | null,
    failed = false,
  ): { fixture: ComponentFixture<Status>; store: Record<string, unknown> } {
    const store = {
      status: signal(status),
      loading: signal(false),
      failed: signal(failed),
      start: vi.fn(),
      stop: vi.fn(),
      refresh: vi.fn(),
    };

    TestBed.configureTestingModule({
      imports: [Status],
      providers: [provideRouter([]), { provide: StatusStore, useValue: store }],
    });

    const fixture = TestBed.createComponent(Status);
    fixture.detectChanges();

    return { fixture, store };
  }

  it('shows the operational state with the season and the next matchday', () => {
    const { fixture } = create(operational);
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Operational');
    expect(text).toContain('Season');
    expect(text).toContain('3');
    expect(text).toContain('Next matchday');
  });

  it('shows the maintenance state and the operator reason, not colour alone', () => {
    const { fixture } = create({
      ...operational,
      readOnly: true,
      readOnlyMessage: 'Read-only while we repair the ledger.',
    });
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Maintenance');
    expect(text).toContain('Read-only while we repair the ledger.');
    expect(text).not.toContain('Operational');
  });

  it('says the world has not been started when there is no season or matchday', () => {
    const { fixture } = create({ ...operational, seasonNumber: null, nextMatchdayAt: null });
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Not started');
    expect(text).toContain('Not scheduled');
  });

  it('reports an unreachable server rather than pretending the service is down', () => {
    const { fixture } = create(null, true);
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Unavailable');
    expect(text).not.toContain('Maintenance');
  });

  it('starts polling on init and stops when the page goes away', () => {
    const { fixture, store } = create(operational);

    expect(store['start']).toHaveBeenCalledTimes(1);

    fixture.destroy();

    expect(store['stop']).toHaveBeenCalledTimes(1);
  });

  it('reads again when the manager asks', () => {
    const { fixture, store } = create(operational);

    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(store['refresh']).toHaveBeenCalledTimes(1);
  });
});
