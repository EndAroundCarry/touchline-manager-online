import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InboxMessage } from '../../core/inbox/inbox.models';
import { InboxStore } from '../../core/inbox/inbox-store';
import {
  messageRevealKey,
  RESULT_REVEAL_STORAGE_KEY,
  ResultRevealStore,
} from '../../core/match/result-reveal-store';
import { Inbox } from './inbox';

/**
 * The inbox keeps a match's result back until the manager asks for it or has watched the match.
 *
 * A message about a match says that the result is in and which match it was, and nothing about how it went.
 * The score, the outcome and the table move are the message's spoiler, shown on "Show result" and remembered
 * under the match, so the inbox and the viewer agree on what the manager has seen.
 */
describe('Inbox', () => {
  const MATCH_ID = '11111111-1111-1111-1111-111111111111';
  const SPOILER = 'Won 2–1. You are 3rd in the table after round 5.';

  let fixture: ComponentFixture<Inbox>;
  let messages: ReturnType<typeof signal<readonly InboxMessage[]>>;

  function message(overrides: Partial<InboxMessage>): InboxMessage {
    return {
      id: 'message-1',
      category: 'result',
      templateKey: 'inbox.result.recorded',
      title: 'Round 5: At home to Vale Athletic',
      body: 'The result is in. Watch the match, or show the result.',
      relatedEntityId: MATCH_ID,
      isRead: false,
      createdAt: '2026-10-04T10:00:00Z',
      readAt: null,
      spoiler: SPOILER,
      ...overrides,
    };
  }

  beforeEach(async () => {
    localStorage.clear();
    messages = signal<readonly InboxMessage[]>([message({})]);

    await TestBed.configureTestingModule({
      imports: [Inbox],
      providers: [
        provideRouter([]),
        {
          provide: InboxStore,
          useValue: {
            messages,
            unreadCount: signal(1),
            hasMore: signal(false),
            loading: signal(false),
            loadingMore: signal(false),
            error: signal<string | null>(null),
            actionError: signal<string | null>(null),
            markingId: signal<string | null>(null),
            markingAll: signal(false),
            unreadOnly: signal(false),
            load: vi.fn(),
            setUnreadOnly: vi.fn(),
            markRead: vi.fn(),
            markAllRead: vi.fn(),
            loadMore: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Inbox);
    await fixture.whenStable();
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function showResult(): HTMLButtonElement | undefined {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'Show result',
    );
  }

  it('says that a result is in without saying how it went', () => {
    expect(text()).toContain('Round 5: At home to Vale Athletic');
    expect(text()).toContain('The result is in');
    expect(text()).not.toContain('Won');
    expect(text()).not.toContain('2–1');
    expect(text()).not.toContain('3rd');
    expect(showResult(), 'the result is one click away').toBeDefined();
  });

  it('shows the result when the manager asks for it, and no longer offers to', async () => {
    showResult()!.click();
    await fixture.whenStable();

    expect(text()).toContain(SPOILER);
    expect(showResult()).toBeUndefined();
  });

  it('remembers the result under the match, so the viewer opens with it already shown', async () => {
    showResult()!.click();
    await fixture.whenStable();

    expect(TestBed.inject(ResultRevealStore).isRevealed(MATCH_ID)).toBe(true);
    expect(JSON.parse(localStorage.getItem(RESULT_REVEAL_STORAGE_KEY) ?? '[]')).toContain(MATCH_ID);
  });

  it('shows the result straight away when the match has already been watched', async () => {
    TestBed.inject(ResultRevealStore).reveal(MATCH_ID);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(text()).toContain(SPOILER);
    expect(showResult()).toBeUndefined();
  });

  it('keeps one match revealed without revealing another', async () => {
    messages.set([
      message({}),
      message({
        id: 'message-2',
        relatedEntityId: '22222222-2222-2222-2222-222222222222',
        title: 'Round 4: Away to Northfield',
        spoiler: 'Lost 0–2. You are 12th in the table after round 4.',
      }),
    ]);
    await fixture.whenStable();

    [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((button) => button.textContent?.trim() === 'Show result')!
      .click();
    await fixture.whenStable();

    expect(text()).toContain(SPOILER);
    expect(text()).not.toContain('Lost 0–2');
    expect(showResult(), 'the other match still waits').toBeDefined();
  });

  it('holds a table move back on its own, since it names no match', async () => {
    messages.set([
      message({
        id: 'table-1',
        category: 'table',
        templateKey: 'inbox.table.moved',
        title: 'Round 5: your league position has changed',
        body: 'Watch your match, or show the result.',
        relatedEntityId: null,
        spoiler: 'You are 3rd. After round 5 you moved up to 3rd from 5th.',
      }),
    ]);
    await fixture.whenStable();

    expect(text()).not.toContain('moved up');

    showResult()!.click();
    await fixture.whenStable();

    expect(text()).toContain('moved up to 3rd from 5th');
    expect(TestBed.inject(ResultRevealStore).isRevealed(messageRevealKey('table-1'))).toBe(true);
    expect(TestBed.inject(ResultRevealStore).isRevealed(MATCH_ID)).toBe(false);
  });

  it('offers no result button on a message with nothing to hide', async () => {
    messages.set([
      message({
        id: 'injury-1',
        category: 'injury',
        title: 'A Player is injured',
        body: 'A Player is out for 2 fixtures.',
        spoiler: null,
      }),
    ]);
    await fixture.whenStable();

    expect(showResult()).toBeUndefined();
  });

  it('invites a manager to watch a match, and to view anything else', async () => {
    expect(text()).toContain('Watch the match');

    messages.set([
      message({
        id: 'injury-1',
        category: 'injury',
        title: 'A Player is injured',
        body: 'A Player is out for 2 fixtures.',
        relatedEntityId: '33333333-3333-3333-3333-333333333333',
        spoiler: null,
      }),
    ]);
    await fixture.whenStable();

    expect(text()).toContain('View');
    expect(text()).not.toContain('Watch the match');
  });
});
