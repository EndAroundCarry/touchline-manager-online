import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { ConversationsStore } from '../../../core/messages/conversations-store';
import { conversation, directMessage, summary } from '../../../core/messages/messages-test-data';
import { Conversation, ConversationSummary } from '../../../core/messages/messages.models';
import { SquadApi } from '../../../core/squad/squad-api';
import { ClubOverview } from '../../../core/squad/squad.models';
import { SyncStore } from '../../../core/sync/sync-store';
import { ComposeMessage } from './compose';
import { ConversationList } from './conversation-list';
import { ConversationThread } from './thread';

/**
 * The three screens of the Managers section: the list, the compose form and a thread
 * (`COM-10`…`COM-16`).
 *
 * What matters on screen: a message body is text and never markup (`COM-13`), unread and blocked are said in
 * words, a reply the server would refuse shows its reason instead of a form (`COM-15`), a block needs a
 * confirmation and an unblock does not, and the compose form is only offered for a club that has a manager
 * who is not the writer (`COM-11`).
 */

function storeStub(overrides: Record<string, unknown> = {}) {
  return {
    conversations: signal<readonly ConversationSummary[]>([]),
    hasMoreConversations: signal(false),
    listLoading: signal(false),
    listLoadingMore: signal(false),
    listError: signal<string | null>(null),
    thread: signal<Conversation | null>(null),
    threadMessages: signal<readonly ReturnType<typeof directMessage>[]>([]),
    hasEarlier: signal(false),
    threadLoading: signal(false),
    threadLoadingMore: signal(false),
    threadError: signal<string | null>(null),
    sending: signal(false),
    sendError: signal<string | null>(null),
    blocking: signal(false),
    blockError: signal<string | null>(null),
    loadList: vi.fn(),
    loadMoreConversations: vi.fn(),
    openThread: vi.fn(),
    loadEarlier: vi.fn(),
    start: vi.fn(),
    reply: vi.fn(),
    block: vi.fn(),
    unblock: vi.fn(),
    ...overrides,
  };
}

const SYNC_STUB = {
  unreadInboxCount: signal(2),
  unreadDirectMessageCount: signal(3),
};

function text(fixture: ComponentFixture<unknown>): string {
  return ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
}

function button(fixture: ComponentFixture<unknown>, label: RegExp): HTMLButtonElement | undefined {
  return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) =>
    label.test(b.textContent ?? ''),
  );
}

describe('ConversationList', () => {
  let store: ReturnType<typeof storeStub>;

  async function open(): Promise<ComponentFixture<ConversationList>> {
    await TestBed.configureTestingModule({
      imports: [ConversationList],
      providers: [
        provideRouter([]),
        { provide: ConversationsStore, useValue: store },
        { provide: SyncStore, useValue: SYNC_STUB },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ConversationList);
    await fixture.whenStable();

    return fixture;
  }

  beforeEach(() => {
    store = storeStub();
  });

  it('reads the conversations when it opens', async () => {
    await open();

    expect(store.loadList).toHaveBeenCalled();
  });

  it('says there is nothing yet and where to start one', async () => {
    const fixture = await open();

    expect(text(fixture)).toContain('No conversations yet');
  });

  it('lists each conversation as a link to its thread, with unread said in words', async () => {
    store.conversations.set([
      summary({ unreadCount: 2 }),
      summary({ id: 'conversation-2', subject: 'Cup draw', unreadCount: 0, blockedByMe: true }),
    ]);

    const fixture = await open();
    const links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
        '[data-testid="conversation-list"] h2 a',
      ),
    );

    expect(links.map((a) => a.getAttribute('href'))).toEqual([
      '/inbox/managers/conversation-1',
      '/inbox/managers/conversation-2',
    ]);
    expect(text(fixture)).toContain('2 unread');
    expect(text(fixture)).toContain('With Sam Rivera · Vale Rovers');
    expect(text(fixture)).toContain('you have blocked this manager');
  });

  it('shows both sections as links, the open one marked, each with its unread count', async () => {
    const fixture = await open();
    const nav = (fixture.nativeElement as HTMLElement).querySelector(
      'nav[aria-label="Inbox sections"]',
    )!;
    const links = Array.from(nav.querySelectorAll('a'));

    expect(links.map((a) => a.getAttribute('href'))).toEqual(['/inbox', '/inbox/managers']);
    expect(links[0].textContent).toContain('Game messages');
    expect(links[0].querySelector('span')?.getAttribute('aria-label')).toBe('2 unread');
    expect(links[1].textContent).toContain('Managers');
    expect(links[1].querySelector('span')?.getAttribute('aria-label')).toBe('3 unread');
  });

  it('offers older conversations only when there are some', async () => {
    store.conversations.set([summary()]);
    store.hasMoreConversations.set(true);

    const fixture = await open();

    button(fixture, /Load older conversations/)?.click();

    expect(store.loadMoreConversations).toHaveBeenCalled();
  });
});

describe('ConversationThread', () => {
  let store: ReturnType<typeof storeStub>;

  async function open(
    thread: Conversation | null,
    messages = thread?.messages.slice().reverse() ?? [],
  ): Promise<ComponentFixture<ConversationThread>> {
    store.thread.set(thread);
    store.threadMessages.set(messages);

    await TestBed.configureTestingModule({
      imports: [ConversationThread],
      providers: [provideRouter([]), { provide: ConversationsStore, useValue: store }],
    }).compileComponents();

    const fixture = TestBed.createComponent(ConversationThread);
    fixture.componentRef.setInput('conversationId', 'conversation-1');
    await fixture.whenStable();

    return fixture;
  }

  beforeEach(() => {
    store = storeStub();
  });

  it('opens the conversation named by the route', async () => {
    await open(conversation());

    expect(store.openThread).toHaveBeenCalledWith('conversation-1');
  });

  it('names the subject, the other manager and their club, with the messages oldest first', async () => {
    const fixture = await open(
      conversation({
        messages: [
          directMessage({ id: 'dm-2', fromMe: true, body: 'Second' }),
          directMessage({ id: 'dm-1', body: 'First' }),
        ],
      }),
    );
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('h1')?.textContent?.trim()).toBe('A friendly');
    expect(root.querySelector('[data-testid="thread-participant"]')?.textContent).toContain(
      'Sam Rivera',
    );
    expect(root.querySelector('[data-testid="thread-participant"] a')?.getAttribute('href')).toBe(
      '/clubs/club-b',
    );

    const items = Array.from(root.querySelectorAll('[data-testid="thread-messages"] li'));

    expect(items.map((li) => li.querySelector('p')?.textContent)).toEqual(['First', 'Second']);
    expect(items[0].textContent).toContain('Sam Rivera');
    expect(items[1].textContent).toContain('You');
  });

  it('shows a body as text, never as markup, and keeps its line breaks (COM-13)', async () => {
    const fixture = await open(
      conversation({
        messages: [directMessage({ body: '<img src=x onerror=alert(1)>\nSecond line' })],
      }),
    );
    const root = fixture.nativeElement as HTMLElement;
    const body = root.querySelector('[data-testid="thread-messages"] li p')!;

    expect(root.querySelector('[data-testid="thread-messages"] img')).toBeNull();
    expect(body.textContent).toBe('<img src=x onerror=alert(1)>\nSecond line');
    expect(body.className).toContain('whitespace-pre-line');
  });

  it('offers a reply box when a reply would be accepted, and sends it trimmed', async () => {
    const fixture = await open(conversation());
    const root = fixture.nativeElement as HTMLElement;
    const box = root.querySelector<HTMLTextAreaElement>('#reply-body')!;

    box.value = '  Sounds good  ';
    box.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    button(fixture, /Send reply/)?.click();

    expect(store.reply).toHaveBeenCalledWith('Sounds good', expect.any(Function));
  });

  it('does not send a reply that is only white space', async () => {
    const fixture = await open(conversation());
    const box = (fixture.nativeElement as HTMLElement).querySelector<HTMLTextAreaElement>(
      '#reply-body',
    )!;

    box.value = '   ';
    box.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(button(fixture, /Send reply/)?.disabled).toBe(true);
  });

  it('shows the reason instead of a reply box when replying is refused (COM-15)', async () => {
    const fixture = await open(
      conversation({
        canReply: false,
        replyBlockedReason: 'Sam Rivera is not accepting your messages.',
      }),
    );
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('#reply-body')).toBeNull();
    expect(root.querySelector('[data-testid="reply-blocked"]')?.textContent).toContain(
      'Sam Rivera is not accepting your messages.',
    );
  });

  it('asks for confirmation before blocking, and can be cancelled', async () => {
    const fixture = await open(conversation());

    button(fixture, /Block this manager/)?.click();
    await fixture.whenStable();

    expect(store.block).not.toHaveBeenCalled();
    expect(text(fixture)).toContain('Block Sam Rivera?');

    button(fixture, /Keep talking/)?.click();
    await fixture.whenStable();

    expect(button(fixture, /Block this manager/)).toBeDefined();
    expect(store.block).not.toHaveBeenCalled();
  });

  it('blocks once confirmed', async () => {
    const fixture = await open(conversation());

    button(fixture, /Block this manager/)?.click();
    await fixture.whenStable();
    button(fixture, /Yes, block Sam Rivera/)?.click();

    expect(store.block).toHaveBeenCalled();
  });

  it('unblocks in one press, saying who is blocked', async () => {
    const fixture = await open(conversation({ blockedByMe: true }));

    expect(text(fixture)).toContain('You have blocked Sam Rivera');

    button(fixture, /Unblock Sam Rivera/)?.click();

    expect(store.unblock).toHaveBeenCalled();
  });

  it('loads earlier messages on request', async () => {
    store.hasEarlier.set(true);

    const fixture = await open(conversation());

    button(fixture, /Load earlier messages/)?.click();

    expect(store.loadEarlier).toHaveBeenCalled();
  });

  it('says so when the conversation is not found', async () => {
    store.threadError.set('No such conversation.');

    const fixture = await open(null);

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent,
    ).toContain('No such conversation.');
  });
});

describe('ComposeMessage', () => {
  let store: ReturnType<typeof storeStub>;
  let squadApi: { club: ReturnType<typeof vi.fn> };

  function club(overrides: Partial<ClubOverview> = {}): ClubOverview {
    return {
      clubId: 'club-b',
      name: 'Vale Rovers',
      managerName: 'Sam Rivera',
      isOwnClub: false,
      ...overrides,
    } as ClubOverview;
  }

  async function open(clubId: string | null = 'club-b'): Promise<ComponentFixture<ComposeMessage>> {
    await TestBed.configureTestingModule({
      imports: [ComposeMessage],
      providers: [
        provideRouter([]),
        { provide: ConversationsStore, useValue: store },
        { provide: SquadApi, useValue: squadApi },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ComposeMessage);
    fixture.componentRef.setInput('club', clubId);
    await fixture.whenStable();

    return fixture;
  }

  function fill(fixture: ComponentFixture<ComposeMessage>, subject: string, body: string): void {
    const root = fixture.nativeElement as HTMLElement;
    const subjectBox = root.querySelector<HTMLInputElement>('#subject')!;
    const bodyBox = root.querySelector<HTMLTextAreaElement>('#body')!;

    subjectBox.value = subject;
    subjectBox.dispatchEvent(new Event('input'));
    bodyBox.value = body;
    bodyBox.dispatchEvent(new Event('input'));
  }

  beforeEach(() => {
    store = storeStub();
    squadApi = { club: vi.fn(() => of(club())) };
  });

  it('names the recipient and their club', async () => {
    const fixture = await open();

    expect(text(fixture)).toContain('To Sam Rivera, manager of Vale Rovers');
    expect(squadApi.club).toHaveBeenCalledWith('club-b');
  });

  it('shows how much of each limit is used (COM-13)', async () => {
    const fixture = await open();

    fill(fixture, 'Hello', 'Twelve chars');
    await fixture.whenStable();

    expect(text(fixture)).toContain('5 of 80 characters');
    expect(text(fixture)).toContain('12 of 1000 characters');
  });

  it('keeps send disabled until both fields hold something', async () => {
    const fixture = await open();

    expect(button(fixture, /Send message/)?.disabled).toBe(true);

    fill(fixture, 'A friendly', '   ');
    await fixture.whenStable();

    expect(button(fixture, /Send message/)?.disabled).toBe(true);

    fill(fixture, 'A friendly', 'Fancy a game?');
    await fixture.whenStable();

    expect(button(fixture, /Send message/)?.disabled).toBe(false);
  });

  it('sends the trimmed subject and message for the club, then opens the new thread', async () => {
    const fixture = await open();
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    store.start.mockImplementation(
      (_club: string, _subject: string, _body: string, done: (id: string) => void) =>
        done('conversation-9'),
    );

    fill(fixture, '  A friendly  ', '  Fancy a game?  ');
    await fixture.whenStable();

    ((fixture.nativeElement as HTMLElement).querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit'),
    );

    expect(store.start).toHaveBeenCalledWith(
      'club-b',
      'A friendly',
      'Fancy a game?',
      expect.any(Function),
    );
    expect(navigate).toHaveBeenCalledWith(['/inbox/managers', 'conversation-9']);
  });

  it('shows the server’s reason when a send is refused', async () => {
    store.sendError.set('That manager is not accepting your messages.');

    const fixture = await open();

    expect(text(fixture)).toContain('That manager is not accepting your messages.');
  });

  it('offers no form for a club the game runs (COM-11)', async () => {
    squadApi.club.mockReturnValue(of(club({ managerName: null })));

    const fixture = await open();

    expect(text(fixture)).toContain('AI manager');
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
  });

  it('offers no form for the writer’s own club (COM-11)', async () => {
    squadApi.club.mockReturnValue(of(club({ isOwnClub: true })));

    const fixture = await open();

    expect(text(fixture)).toContain('your own club');
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
  });

  it('asks for a club when none is named', async () => {
    const fixture = await open(null);

    expect(squadApi.club).not.toHaveBeenCalled();
    expect(text(fixture)).toContain('Choose a club');
  });
});
