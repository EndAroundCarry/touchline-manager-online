import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { ApiError } from '../../core/api/api-error';
import { ForumApi } from '../../core/forum/forum-api';
import {
  ForumOverview,
  ForumPost,
  ForumThreadDetail,
  ForumThreadList,
} from '../../core/forum/forum.models';
import { Forum } from './forum';
import { NewThread } from './new-thread';
import { Subforum } from './subforum';
import { ForumThread } from './thread';

/**
 * The four forum screens: the front page, a subforum, the new-thread form and a thread.
 *
 * What matters on screen: the international forum says its posts are in English wherever a post is read or
 * written, the Announcements subforum offers no "Start a thread" and its form says why, a manager without a club
 * is told why no country forum is listed, and a post body is text and never markup.
 */

const NOTICE = 'All posts in the International Forum must be in English.';

const OVERVIEW: ForumOverview = {
  serverTime: '2026-10-22T10:00:00Z',
  sections: [
    {
      id: 'international',
      title: 'International Forum',
      description: 'Global community.',
      languageNotice: NOTICE,
      countryId: null,
      countryName: null,
      subforums: [
        {
          id: 'international-general',
          sectionId: 'international',
          name: 'General',
          description: 'Chat.',
          canCreateThreads: true,
          threadCount: 1,
          postCount: 3,
          latestActivityAt: '2026-10-22T09:00:00Z',
        },
        {
          id: 'international-development-bugs',
          sectionId: 'international',
          name: 'Development and Bugs',
          description: 'Bugs.',
          canCreateThreads: true,
          threadCount: 0,
          postCount: 0,
          latestActivityAt: null,
        },
        {
          id: 'international-announcements',
          sectionId: 'international',
          name: 'Announcements',
          description: 'News.',
          canCreateThreads: false,
          threadCount: 2,
          postCount: 2,
          latestActivityAt: null,
        },
      ],
    },
  ],
};

const COUNTRY_SECTION = {
  id: 'country',
  title: 'Wales Forum',
  description: 'Local community.',
  languageNotice: null,
  countryId: 'country-1',
  countryName: 'Wales',
  subforums: [
    {
      id: 'country-general',
      sectionId: 'country',
      name: 'General',
      description: 'Local chat.',
      canCreateThreads: true,
      threadCount: 0,
      postCount: 0,
      latestActivityAt: null,
    },
  ],
};

function threadList(overrides: Partial<ForumThreadList> = {}): ForumThreadList {
  return {
    subforumId: 'international-general',
    subforumName: 'General',
    sectionId: 'international',
    sectionTitle: 'International Forum',
    languageNotice: NOTICE,
    canCreateThreads: true,
    threads: [
      {
        id: 'thread-1',
        subforumId: 'international-general',
        title: 'Best 4-4-2 setup?',
        authorManagerId: 'manager-1',
        authorManagerName: 'Sam Rivera',
        authorClubName: 'Vale Rovers',
        isPinned: true,
        isAnnouncement: false,
        replyCount: 1,
        createdAt: '2026-10-22T08:00:00Z',
        lastActivityAt: '2026-10-22T09:00:00Z',
        latestReplyAuthorName: 'Alex Kim',
      },
    ],
    ...overrides,
  };
}

function post(overrides: Partial<ForumPost> = {}): ForumPost {
  return {
    id: 'post-1',
    threadId: 'thread-1',
    postNumber: 1,
    authorManagerId: 'manager-1',
    authorManagerName: 'Sam Rivera',
    authorClubName: 'Vale Rovers',
    isStaff: false,
    body: 'First line\nsecond line <b>not bold</b>',
    createdAt: '2026-10-22T08:00:00Z',
    ...overrides,
  };
}

function threadDetail(overrides: Partial<ForumThreadDetail> = {}): ForumThreadDetail {
  return {
    id: 'thread-1',
    subforumId: 'international-general',
    subforumName: 'General',
    sectionId: 'international',
    sectionTitle: 'International Forum',
    languageNotice: NOTICE,
    title: 'Best 4-4-2 setup?',
    authorManagerId: 'manager-1',
    authorManagerName: 'Sam Rivera',
    authorClubName: 'Vale Rovers',
    isPinned: false,
    isAnnouncement: false,
    canReply: true,
    createdAt: '2026-10-22T08:00:00Z',
    posts: [post()],
    ...overrides,
  };
}

function apiStub(overrides: Record<string, unknown> = {}) {
  return {
    overview: vi.fn(() => of(OVERVIEW)),
    threads: vi.fn(() => of(threadList())),
    thread: vi.fn(() => of(threadDetail())),
    createThread: vi.fn(() => of({ threadId: 'thread-9' })),
    reply: vi.fn(() => of(post({ id: 'post-2', postNumber: 2, body: 'Thanks' }))),
    ...overrides,
  };
}

function text(fixture: ComponentFixture<unknown>): string {
  return ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
}

function element<T extends Element>(
  fixture: ComponentFixture<unknown>,
  selector: string,
): T | null {
  return (fixture.nativeElement as HTMLElement).querySelector<T>(selector);
}

function apiError(detail: string): ApiError {
  return new ApiError(403, 'FORUM_ERROR', detail, null, new Map());
}

describe('Forum', () => {
  async function open(api: ReturnType<typeof apiStub>): Promise<ComponentFixture<Forum>> {
    await TestBed.configureTestingModule({
      imports: [Forum],
      providers: [provideRouter([]), { provide: ForumApi, useValue: api }],
    }).compileComponents();

    const fixture = TestBed.createComponent(Forum);
    await fixture.whenStable();

    return fixture;
  }

  it('lists the international subforums, each linking to its threads, with the English rule', async () => {
    const fixture = await open(apiStub());
    const links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('h3 a'),
    );

    expect(links.map((a) => a.getAttribute('href'))).toEqual([
      '/forum/international-general',
      '/forum/international-development-bugs',
      '/forum/international-announcements',
    ]);
    expect(links.map((a) => a.textContent?.trim())).toEqual([
      'General',
      'Development and Bugs',
      'Announcements',
    ]);
    expect(element(fixture, '[data-testid="language-notice"]')?.textContent).toContain(NOTICE);
    expect(text(fixture)).toContain('1 thread · 3 posts');
    expect(text(fixture)).toContain('Staff only');
  });

  it('lists the country forum beside it, without a language rule, when the manager has one', async () => {
    const fixture = await open(
      apiStub({
        overview: vi.fn(() =>
          of({ ...OVERVIEW, sections: [...OVERVIEW.sections, COUNTRY_SECTION] }),
        ),
      }),
    );

    expect(text(fixture)).toContain('Wales Forum');
    expect(element(fixture, '[data-testid="no-country-forum"]')).toBeNull();
    expect(
      (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="language-notice"]'),
    ).toHaveLength(1);
  });

  it('says why there is no country forum when the manager runs no club', async () => {
    const fixture = await open(apiStub());

    expect(element(fixture, '[data-testid="no-country-forum"]')?.textContent).toContain(
      'once you run a club',
    );
  });

  it('shows the reason when the forum cannot be read', async () => {
    const fixture = await open(
      apiStub({ overview: vi.fn(() => throwError(() => apiError('Sign in to continue.'))) }),
    );

    expect(element(fixture, '[role="alert"]')?.textContent).toContain('Sign in to continue.');
  });
});

describe('Subforum', () => {
  async function open(
    api: ReturnType<typeof apiStub>,
    subforumId = 'international-general',
  ): Promise<ComponentFixture<Subforum>> {
    await TestBed.configureTestingModule({
      imports: [Subforum],
      providers: [provideRouter([]), { provide: ForumApi, useValue: api }],
    }).compileComponents();

    const fixture = TestBed.createComponent(Subforum);
    fixture.componentRef.setInput('subforumId', subforumId);
    await fixture.whenStable();

    return fixture;
  }

  it('reads the subforum it is opened on and lists its threads as links', async () => {
    const api = apiStub();
    const fixture = await open(api);
    const links = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
        '[data-testid="thread-list"] h2 a',
      ),
    );

    expect(api.threads).toHaveBeenCalledWith('international-general');
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['/forum/threads/thread-1']);
    expect(text(fixture)).toContain('Pinned');
    expect(text(fixture)).toContain(
      'Started by Sam Rivera · Vale Rovers · 1 reply · latest by Alex Kim',
    );
  });

  it('offers to start a thread and repeats the English rule', async () => {
    const fixture = await open(apiStub());

    expect(
      element<HTMLAnchorElement>(fixture, 'a[href="/forum/international-general/new"]'),
    ).not.toBeNull();
    expect(element(fixture, '[data-testid="language-notice"]')?.textContent).toContain(NOTICE);
  });

  it('offers no way to start a thread in Announcements and says only staff do', async () => {
    const fixture = await open(
      apiStub({
        threads: vi.fn(() =>
          of(
            threadList({
              subforumId: 'international-announcements',
              subforumName: 'Announcements',
              canCreateThreads: false,
            }),
          ),
        ),
      }),
      'international-announcements',
    );

    expect(element(fixture, 'a[href$="/new"]')).toBeNull();
    expect(element(fixture, '[data-testid="staff-only"]')?.textContent).toContain('staff');
  });

  it('says there are no threads yet', async () => {
    const fixture = await open(apiStub({ threads: vi.fn(() => of(threadList({ threads: [] }))) }));

    expect(text(fixture)).toContain('No threads yet');
  });

  it('shows the reason when the subforum cannot be read', async () => {
    const fixture = await open(
      apiStub({
        threads: vi.fn(() => throwError(() => apiError('The specified subforum does not exist.'))),
      }),
    );

    expect(element(fixture, '[role="alert"]')?.textContent).toContain('does not exist');
  });
});

describe('NewThread', () => {
  async function open(
    api: ReturnType<typeof apiStub>,
    subforumId = 'international-general',
  ): Promise<ComponentFixture<NewThread>> {
    await TestBed.configureTestingModule({
      imports: [NewThread],
      providers: [provideRouter([]), { provide: ForumApi, useValue: api }],
    }).compileComponents();

    const fixture = TestBed.createComponent(NewThread);
    fixture.componentRef.setInput('subforumId', subforumId);
    await fixture.whenStable();

    return fixture;
  }

  function fill(fixture: ComponentFixture<unknown>, title: string, body: string): void {
    const titleInput = element<HTMLInputElement>(fixture, '#title')!;
    const bodyInput = element<HTMLTextAreaElement>(fixture, '#body')!;

    titleInput.value = title;
    titleInput.dispatchEvent(new Event('input'));
    bodyInput.value = body;
    bodyInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submit(fixture: ComponentFixture<unknown>): HTMLButtonElement {
    return element<HTMLButtonElement>(fixture, 'button[type="submit"]')!;
  }

  it('shows the English rule above the form', async () => {
    const fixture = await open(apiStub());

    expect(element(fixture, '[data-testid="language-notice"]')?.textContent).toContain(NOTICE);
    expect(element(fixture, '[data-testid="new-thread-target"]')?.textContent).toContain(
      'International Forum, General',
    );
  });

  it('keeps Post thread disabled until the title and the post are long enough', async () => {
    const fixture = await open(apiStub());

    expect(submit(fixture).disabled).toBe(true);

    fill(fixture, 'Hi', 'Hello there');
    expect(submit(fixture).disabled).toBe(true);

    fill(fixture, 'Hello', 'Hello there');
    expect(submit(fixture).disabled).toBe(false);
  });

  it('sends the trimmed text and opens the thread it started', async () => {
    const api = apiStub();
    const fixture = await open(api);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    fill(fixture, '  Hello world  ', '  Welcome  ');
    submit(fixture).click();
    await fixture.whenStable();

    expect(api.createThread).toHaveBeenCalledWith(
      'international-general',
      'Hello world',
      'Welcome',
    );
    expect(navigate).toHaveBeenCalledWith(['/forum/threads', 'thread-9']);
  });

  it('shows the server reason and stays on the form when the thread is refused', async () => {
    const api = apiStub({
      createThread: vi.fn(() => throwError(() => apiError('Slow down and try again shortly.'))),
    });
    const fixture = await open(api);

    fill(fixture, 'Hello world', 'Welcome');
    submit(fixture).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element(fixture, '[role="alert"]')?.textContent).toContain('Slow down');
    expect(element(fixture, 'form')).not.toBeNull();
  });

  it('offers no form in Announcements and says why', async () => {
    const fixture = await open(
      apiStub({
        threads: vi.fn(() =>
          of(
            threadList({
              subforumId: 'international-announcements',
              subforumName: 'Announcements',
              canCreateThreads: false,
            }),
          ),
        ),
      }),
      'international-announcements',
    );

    expect(element(fixture, 'form')).toBeNull();
    expect(element(fixture, '[data-testid="staff-only"]')?.textContent).toContain(
      "Only the game's staff",
    );
  });

  it('shows no language rule in a country forum', async () => {
    const fixture = await open(
      apiStub({
        threads: vi.fn(() =>
          of(
            threadList({
              subforumId: 'country-general',
              sectionId: 'country',
              sectionTitle: 'Wales Forum',
              languageNotice: null,
            }),
          ),
        ),
      }),
      'country-general',
    );

    expect(element(fixture, '[data-testid="language-notice"]')).toBeNull();
    expect(element(fixture, 'form')).not.toBeNull();
  });
});

describe('ForumThread', () => {
  async function open(api: ReturnType<typeof apiStub>): Promise<ComponentFixture<ForumThread>> {
    await TestBed.configureTestingModule({
      imports: [ForumThread],
      providers: [provideRouter([]), { provide: ForumApi, useValue: api }],
    }).compileComponents();

    const fixture = TestBed.createComponent(ForumThread);
    fixture.componentRef.setInput('threadId', 'thread-1');
    await fixture.whenStable();

    return fixture;
  }

  it('shows the posts as text, never as markup, with the English rule', async () => {
    const fixture = await open(apiStub());
    const body = element(fixture, '[data-testid="thread-posts"] p')!;

    expect(body.textContent).toContain('<b>not bold</b>');
    expect(body.querySelector('b')).toBeNull();
    expect(text(fixture)).toContain('Best 4-4-2 setup?');
    expect(text(fixture)).toContain('#1');
    expect(element(fixture, '[data-testid="language-notice"]')?.textContent).toContain(NOTICE);
  });

  it('marks a post by the game staff', async () => {
    const fixture = await open(
      apiStub({
        thread: vi.fn(() => of(threadDetail({ posts: [post({ isStaff: true })] }))),
      }),
    );

    expect(text(fixture)).toContain('Staff');
  });

  it('adds a posted reply under the thread and clears the box', async () => {
    const api = apiStub();
    const fixture = await open(api);
    const box = element<HTMLTextAreaElement>(fixture, '#reply-body')!;

    box.value = '  Thanks  ';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    element<HTMLFormElement>(fixture, 'form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.reply).toHaveBeenCalledWith('thread-1', 'Thanks');
    expect(
      (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="thread-posts"] > li'),
    ).toHaveLength(2);
    expect(element<HTMLTextAreaElement>(fixture, '#reply-body')!.value).toBe('');
  });

  it('shows the server reason when a reply is refused', async () => {
    const fixture = await open(
      apiStub({
        reply: vi.fn(() => throwError(() => apiError('Slow down and try again shortly.'))),
      }),
    );
    const box = element<HTMLTextAreaElement>(fixture, '#reply-body')!;

    box.value = 'Thanks';
    box.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    element<HTMLFormElement>(fixture, 'form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element(fixture, '[role="alert"]')?.textContent).toContain('Slow down');
  });

  it('shows the reason and a way back when the thread cannot be read', async () => {
    const fixture = await open(
      apiStub({
        thread: vi.fn(() => throwError(() => apiError('The specified thread does not exist.'))),
      }),
    );

    expect(element(fixture, '[role="alert"]')?.textContent).toContain('does not exist');
    expect(element(fixture, 'a[href="/forum"]')).not.toBeNull();
  });
});
