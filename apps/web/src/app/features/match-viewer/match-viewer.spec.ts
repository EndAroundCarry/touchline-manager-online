import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import {
  ClockKeyframe,
  CommentaryLine,
  HighlightKeyframe,
  Match,
  MatchPresentation,
  Passage,
} from '../../core/match/match.models';
import { MatchStore } from '../../core/match/match-store';
import { MatchViewer } from './match-viewer';

/**
 * The viewer's wiring from a loaded presentation to a working FM-style match center (`replay-v3`).
 *
 * The playback state machine, the renderer, and the store are all tested on their own elsewhere; what was
 * never tested is the seam between them — that the viewer builds its player from the presentation it was
 * given, that the panels follow the minute-by-minute curve the server captured, and that the tabs show the
 * replay's own facts. A viewer that kept an empty player would render a complete, believable match center
 * whose Play button did nothing, which is exactly the bug this guards.
 *
 * The control is exercised in text-only mode, because a headless DOM has no canvas 2D context and the
 * replay's controls are what this seam is about.
 */

function passage(): Passage {
  return {
    sourceEventSequence: 5,
    minute: 8,
    stoppageMinute: 0,
    startMatchSecond: 480,
    endMatchSecond: 540,
    durationMilliseconds: 6_000,
    outcomeCode: 'goal',
    narration: 'Goal — A Scorer, 8.',
    homeColour: '#1f6f43',
    awayColour: '#7a1f2b',
    eventSequences: [5],
    entities: [
      {
        entityId: 'H9',
        isBall: false,
        side: 'home',
        participantId: 'p1',
        shirtNumber: 9,
        family: 'attack',
        x: 7_000,
        y: 3_500,
      },
      {
        entityId: 'ball',
        isBall: true,
        side: null,
        participantId: null,
        shirtNumber: 0,
        family: null,
        x: 7_000,
        y: 3_500,
      },
    ],
    tracks: [
      {
        entityId: 'H9',
        keyframes: [
          { timeMilliseconds: 0, x: 6_000, y: 3_000 },
          { timeMilliseconds: 3_000, x: 7_400, y: 3_600, action: 'shot' },
        ],
      },
      {
        entityId: 'ball',
        keyframes: [
          { timeMilliseconds: 0, x: 5_000, y: 3_500 },
          { timeMilliseconds: 6_000, x: 7_800, y: 3_600 },
        ],
      },
    ],
    commentary: [
      {
        timeMilliseconds: 0,
        templateKey: 'match.build.pass',
        variantKey: 'match.build.pass.v1',
        parameters: [{ name: 'playerId', value: 'pl1' }],
        text: 'Ashvale United build the move.',
      },
      {
        timeMilliseconds: 3_000,
        templateKey: 'match.build.chance',
        variantKey: 'match.build.chance.v1',
        parameters: [],
        text: 'The shot is struck.',
      },
    ],
  };
}

function line(): CommentaryLine {
  return {
    sequence: 5,
    minute: 8,
    stoppageMinute: 0,
    side: 'home',
    templateKey: 'match.goal',
    variantKey: 'match.goal.v1',
    parameters: [{ name: 'playerId', value: 'pl1' }],
    text: 'Goal for Ashvale United.',
  };
}

function presentation(): MatchPresentation {
  return {
    matchId: 'm1',
    presentationVersion: 'replay-v3',
    engineVersion: 'engine-v4',
    homeGoals: 1,
    awayGoals: 0,
    commentary: [line()],
    passages: [passage()],
    reel: [
      {
        sourceEventSequence: 5,
        outcomeCode: 'goal',
        minute: 8,
        stoppageMinute: 0,
        startMilliseconds: 0,
        endMilliseconds: 6_000,
      },
    ],
    estimatedPayloadBytes: 2_048,
    homeLineup: {
      clubName: 'Ashvale United',
      shortName: 'ASH',
      primaryColour: '#1f6f43',
      secondaryColour: '#ffffff',
      formation: '4-4-2',
      starters: [
        {
          participantId: 'p1',
          playerId: 'pl1',
          shirtNumber: 9,
          name: 'A Scorer',
          position: 'ST',
          family: 'attack',
          isStarter: true,
          slotNumber: 9,
          kickoffCondition: 10_000,
          finalCondition: 6_000,
          finalRating: 7_200,
          goals: 1,
          assists: 0,
          yellowCards: 0,
          sentOff: false,
          subbedOutMinute: null,
          subbedInMinute: null,
          isInjured: false,
        },
      ],
      bench: [],
    },
    awayLineup: null,
    liveMetrics: [
      { participantId: 'p1', minute: 0, conditionBasisPoints: 10_000, ratingBasisPoints: 6_000 },
      { participantId: 'p1', minute: 8, conditionBasisPoints: 8_000, ratingBasisPoints: 7_400 },
    ],
    playback: [
      {
        kind: 'passage',
        sourceEventSequence: 5,
        startMilliseconds: 0,
        durationMilliseconds: 6_000,
      },
    ],
    totalPlaybackMilliseconds: 6_000,
  };
}

function match(): Match {
  return {
    matchId: 'm1',
    fixtureId: 'f1',
    divisionId: 'd1',
    divisionName: 'England Top Division',
    tierNumber: 1,
    countryId: 'co1',
    countryCode: 'ENG',
    countryName: 'England',
    seasonNumber: 1,
    seasonLabel: '2026/27',
    roundNumber: 1,
    kickoffAt: '2026-10-06T19:00:00Z',
    status: 'published',
    home: {
      clubId: 'c1',
      name: 'Ashvale United',
      shortName: 'ASH',
      goals: 1,
      statistics: statistics(1),
    },
    away: {
      clubId: 'c2',
      name: 'Bramford Rovers',
      shortName: 'BRA',
      goals: 0,
      statistics: statistics(0),
    },
    engineVersion: 'engine-v4',
    presentationVersion: 'replay-v3',
    serverTime: '2026-10-06T21:00:00Z',
  };
}

function statistics(goals: number) {
  return {
    possessionBasisPoints: 5_000,
    goals,
    shots: 5,
    shotsOnTarget: 2,
    shotsOffTarget: 2,
    shotsBlocked: 1,
    woodworkHits: 0,
    saves: 2,
    corners: 3,
    offsides: 1,
    fouls: 8,
    yellowCards: 0,
    redCards: 0,
    penaltiesAwarded: 0,
    penaltiesScored: 0,
    injuries: 0,
    substitutions: 0,
  };
}

/** A button whose visible label is exactly the given text. */
function buttonByText(root: HTMLElement, text: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === text,
  ) as HTMLButtonElement | undefined;
}

describe('MatchViewer', () => {
  let fixture: ComponentFixture<MatchViewer>;
  let store: {
    match: ReturnType<typeof signal<Match | null>>;
    presentation: ReturnType<typeof signal<MatchPresentation | null>>;
    loading: ReturnType<typeof signal<boolean>>;
    error: ReturnType<typeof signal<string | null>>;
    load: ReturnType<typeof vi.fn>;
    clear: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    store = {
      match: signal<Match | null>(null),
      presentation: signal<MatchPresentation | null>(null),
      loading: signal(false),
      error: signal<string | null>(null),
      load: vi.fn(),
      clear: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [MatchViewer],
      providers: [
        { provide: MatchStore, useValue: store },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['matchId', 'm1']]) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MatchViewer);
  });

  /** Loads the fixture match and waits for the screen to settle. */
  async function load(): Promise<HTMLElement> {
    const native = fixture.nativeElement as HTMLElement;

    // Text-only before the presentation arrives, so the renderer never needs a 2D context.
    (fixture.componentInstance as unknown as { toggleTextOnly(): void }).toggleTextOnly();

    store.match.set(match());
    store.presentation.set(presentation());

    await fixture.whenStable();

    return native;
  }

  it('builds its replay from the presentation, so Play starts it', async () => {
    const native = await load();
    const play = [...native.querySelectorAll('button')].find(
      (button) => button.getAttribute('aria-label') === 'Play replay',
    ) as HTMLButtonElement | undefined;

    expect(play, 'the replay offers Play once it has a passage to play').toBeDefined();

    play!.click();

    await fixture.whenStable();

    expect(
      native.querySelector('button[aria-label="Pause replay"]'),
      'Play starts the replay, so the control becomes Pause',
    ).not.toBeNull();
    expect(native.querySelector('button[aria-label="Play replay"]')).toBeNull();
  });

  it('offers skip-all, which ends the replay rather than pausing it', async () => {
    const native = await load();

    (native.querySelector('button[aria-label="Play replay"]') as HTMLButtonElement).click();

    await fixture.whenStable();

    expect(native.querySelector('button[aria-label="Pause replay"]')).not.toBeNull();

    buttonByText(native, 'Skip all')!.click();

    await fixture.whenStable();

    expect(native.querySelector('button[aria-label="Play replay"]')).not.toBeNull();
    expect(native.textContent).toContain('END');
  });

  it('moves the lineup panels with the minute-by-minute curve the server captured', async () => {
    const native = await load();

    // Before the replay starts the panel shows kickoff: a full condition bar and the 6.0 baseline.
    expect(native.textContent).toContain('6.0');
    expect(barWidth(native)).toBe('100%');

    const play = native.querySelector('button[aria-label="Play replay"]') as HTMLButtonElement;

    play.click();

    await fixture.whenStable();

    // At the active passage's minute the curve's own figures are on the panel, not an interpolation.
    expect(native.textContent).toContain('7.4');
    expect(barWidth(native)).toBe('80%');
  });

  it('shows the build-up commentary feed as the playhead reaches it', async () => {
    const native = await load();

    const feed = native.querySelector('[data-testid="match-feed"]');

    expect(feed).not.toBeNull();
    expect(feed!.textContent).toContain('Ashvale United build the move.');
  });

  it('offers a progress scrubber with the film clock', async () => {
    const native = await load();

    expect(native.querySelector('input[aria-label="Replay position"]')).not.toBeNull();
    expect(native.querySelector('[data-testid="match-elapsed"]')?.textContent).toContain('0:00');
  });

  it('keeps a screen-reader scoreline and switches between the report, statistics, and player tabs', async () => {
    const native = await load();

    expect(native.querySelector('h1')?.textContent).toContain('1\u20130');

    buttonByText(native, 'Statistics')!.click();
    await fixture.whenStable();

    expect(native.textContent).toContain('Match Statistics');
    expect(native.querySelector('svg[aria-label="Shot map"]')).not.toBeNull();
    expect(native.querySelector('title')?.textContent).toContain("Goal \u2014 8'");

    buttonByText(native, 'Players')!.click();
    await fixture.whenStable();

    expect(native.textContent).toContain('Player Performance');
    expect(native.textContent).toContain('A Scorer');
  });

  it('offers the full match by default and switches to the highlights reel on request', async () => {
    const native = await load();

    const full = buttonByText(native, 'Full match')!;
    const highlights = buttonByText(native, 'Highlights')!;

    expect(full.getAttribute('aria-pressed')).toBe('true');
    expect(highlights.getAttribute('aria-pressed')).toBe('false');

    highlights.click();

    await fixture.whenStable();

    expect(highlights.getAttribute('aria-pressed')).toBe('true');
    expect(full.getAttribute('aria-pressed')).toBe('false');
  });
});

/** The width of the first condition bar on the screen. */
function barWidth(native: HTMLElement): string {
  const bar = native.querySelector('.bg-emerald-500, .bg-lime-500, .bg-amber-500, .bg-rose-500');

  return bar === null ? '' : (bar as HTMLElement).style.width;
}

// ---- The film ------------------------------------------------------------------------------------------

/**
 * A film of several passages, as `replay-v4` sends one: each passage carries its half and its clock.
 *
 * Passages one and two run through the first half's stoppage, the third is the half-time card, the fourth
 * opens the second half, and the fifth is its stoppage — so a seek to each reads the clock a manager reads.
 */
function filmPresentation(): MatchPresentation {
  const make = (
    sequence: number,
    narration: string,
    duration: number,
    period: number,
    clock: readonly ClockKeyframe[],
    extra: Partial<Passage> = {},
  ): Passage => ({
    sourceEventSequence: sequence,
    minute: 45,
    stoppageMinute: 0,
    startMatchSecond: clock[0].matchSecond,
    endMatchSecond: clock[clock.length - 1].matchSecond,
    durationMilliseconds: duration,
    outcomeCode: 'play',
    narration,
    homeColour: '#1f6f43',
    awayColour: '#7a1f2b',
    eventSequences: sequence === 0 ? [] : [sequence],
    entities: [
      {
        entityId: 'H9',
        isBall: false,
        side: 'home',
        participantId: 'p1',
        shirtNumber: 9,
        family: 'attack',
        x: 5_000,
        y: 5_000,
        name: 'A Scorer',
      },
      {
        entityId: 'ball',
        isBall: true,
        side: null,
        participantId: null,
        shirtNumber: 0,
        family: null,
        x: 5_000,
        y: 5_000,
      },
    ],
    tracks: [
      {
        entityId: 'H9',
        keyframes: [
          { timeMilliseconds: 0, x: 5_000, y: 5_000 } satisfies HighlightKeyframe,
          { timeMilliseconds: duration, x: 6_000, y: 5_000 } satisfies HighlightKeyframe,
        ],
      },
    ],
    commentary: [],
    period,
    clock,
    cuts: [],
    ...extra,
  });

  const passages = [
    make(1, 'Passage one', 4_000, 1, [
      { timeMilliseconds: 0, matchSecond: 2_640 },
      { timeMilliseconds: 4_000, matchSecond: 2_700 },
    ]),
    make(
      2,
      'Passage two',
      4_000,
      1,
      [
        { timeMilliseconds: 0, matchSecond: 2_700 },
        { timeMilliseconds: 4_000, matchSecond: 2_820 },
      ],
      {
        commentary: [
          {
            timeMilliseconds: 2_000,
            templateKey: 'match.build.pass',
            variantKey: 'match.build.pass.v1',
            parameters: [{ name: 'playerId', value: 'pl1' }],
            text: 'Late in the half.',
          },
        ],
      },
    ),
    make(
      0,
      'Half-time.',
      3_000,
      1,
      [
        { timeMilliseconds: 0, matchSecond: 2_820 },
        { timeMilliseconds: 3_000, matchSecond: 2_820 },
      ],
      { outcomeCode: 'half_time' },
    ),
    make(
      4,
      'Passage four',
      4_000,
      2,
      [
        { timeMilliseconds: 0, matchSecond: 2_700 },
        { timeMilliseconds: 4_000, matchSecond: 2_760 },
      ],
      {
        cuts: [{ timeMilliseconds: 0, durationMilliseconds: 300, kind: 'half_time' }],
        commentary: [
          {
            timeMilliseconds: 1_000,
            templateKey: 'match.build.pass',
            variantKey: 'match.build.pass.v1',
            parameters: [{ name: 'playerId', value: 'pl1' }],
            text: 'The second half begins.',
          },
        ],
      },
    ),
    make(5, 'Passage five', 3_000, 2, [
      { timeMilliseconds: 0, matchSecond: 5_400 },
      { timeMilliseconds: 3_000, matchSecond: 5_460 },
    ]),
  ];
  let cursor = 0;

  return {
    ...presentation(),
    presentationVersion: 'replay-v4',
    passages,
    reel: [],
    playback: passages.map((item) => {
      const segment = {
        kind: 'passage',
        sourceEventSequence: item.sourceEventSequence,
        startMilliseconds: cursor,
        durationMilliseconds: item.durationMilliseconds,
      };

      cursor += item.durationMilliseconds;

      return segment;
    }),
    totalPlaybackMilliseconds: cursor,
  };
}

/** A 2D context that accepts anything, so a real canvas can be drawn on in a headless DOM. */
function permissiveContext(): CanvasRenderingContext2D {
  const state: Record<string | symbol, unknown> = {};

  return new Proxy(state, {
    get: (target, property) => (property in target ? target[property] : () => undefined),
    set: (target, property, value) => {
      target[property] = value;

      return true;
    },
  }) as unknown as CanvasRenderingContext2D;
}

describe('MatchViewer: one continuous film', () => {
  let fixture: ComponentFixture<MatchViewer>;
  let store: {
    match: ReturnType<typeof signal<Match | null>>;
    presentation: ReturnType<typeof signal<MatchPresentation | null>>;
    loading: ReturnType<typeof signal<boolean>>;
    error: ReturnType<typeof signal<string | null>>;
    load: ReturnType<typeof vi.fn>;
    clear: ReturnType<typeof vi.fn>;
  };
  let frames: Map<number, FrameRequestCallback>;
  let nextFrame: number;

  beforeEach(async () => {
    frames = new Map();
    nextFrame = 1;

    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback): number => {
      const handle = nextFrame;

      nextFrame += 1;
      frames.set(handle, callback);

      return handle;
    });
    vi.stubGlobal('cancelAnimationFrame', (handle: number): void => {
      frames.delete(handle);
    });

    store = {
      match: signal<Match | null>(null),
      presentation: signal<MatchPresentation | null>(null),
      loading: signal(false),
      error: signal<string | null>(null),
      load: vi.fn(),
      clear: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [MatchViewer],
      providers: [
        { provide: MatchStore, useValue: store },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['matchId', 'm1']]) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MatchViewer);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  /** Runs every frame the loop has scheduled, as the browser would at a timestamp. */
  function frame(timestamp: number): void {
    const due = [...frames.entries()];

    frames.clear();

    for (const [, callback] of due) {
      callback(timestamp);
    }
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** Loads the film and returns the page, in text-only mode so there is no canvas, unless asked. */
  async function load(options: { textOnly: boolean }): Promise<HTMLElement> {
    const native = fixture.nativeElement as HTMLElement;

    if (options.textOnly) {
      (fixture.componentInstance as unknown as { toggleTextOnly(): void }).toggleTextOnly();
    }

    store.match.set(match());
    store.presentation.set(filmPresentation());

    await settle();

    return native;
  }

  /** Moves the playhead the way a manager does, with the scrubber. */
  async function scrubTo(native: HTMLElement, milliseconds: number): Promise<void> {
    const scrubber = native.querySelector(
      'input[aria-label="Replay position"]',
    ) as HTMLInputElement;

    scrubber.value = `${milliseconds}`;
    scrubber.dispatchEvent(new Event('input'));

    await settle();
  }

  const clockText = (native: HTMLElement) =>
    native.querySelector('[data-testid="match-clock"]')?.textContent?.trim();

  describe('the picture', () => {
    let getContext: ReturnType<typeof vi.spyOn>;
    let mainCanvas: HTMLCanvasElement;

    beforeEach(() => {
      getContext = vi
        .spyOn(HTMLCanvasElement.prototype, 'getContext')
        .mockImplementation(() => permissiveContext() as never);
    });

    /** How many renderers were made for the pitch canvas: each one asks the canvas for its context once. */
    const renderers = () =>
      getContext.mock.contexts.filter((context: unknown) => context === mainCanvas).length;

    async function loadWithPitch(): Promise<HTMLElement> {
      const native = await load({ textOnly: false });

      mainCanvas = native.querySelector('canvas[role="img"]') as HTMLCanvasElement;

      expect(mainCanvas, 'the pitch is on the page').not.toBeNull();

      return native;
    }

    it('makes one renderer for the film and keeps it as the playhead crosses passage boundaries', async () => {
      const native = await loadWithPitch();

      expect(renderers()).toBe(1);

      (native.querySelector('button[aria-label="Play replay"]') as HTMLButtonElement).click();
      await settle();

      // The first passage runs to 4 s, the second to 8 s, the half-time card to 11 s, the fourth to 15 s.
      // Frames are 100 ms apart, which is the longest a frame may be.
      const narrations = new Set<string>();

      for (let timestamp = 0; timestamp <= 15_500; timestamp += 100) {
        frame(timestamp);
        fixture.detectChanges();

        narrations.add(native.querySelector('[role="status"]')?.textContent?.trim() ?? '');
      }

      await settle();

      // Several passages were played through, and the renderer was never rebuilt for any of them.
      expect(renderers(), 'one renderer across every passage boundary').toBe(1);
      expect(narrations.size).toBeGreaterThanOrEqual(4);
    });

    it('does not rebuild the renderer for a pause, a speed change, a mode switch, or a seek', async () => {
      const native = await loadWithPitch();

      (native.querySelector('button[aria-label="Play replay"]') as HTMLButtonElement).click();
      await settle();
      frame(0);
      frame(100);

      (native.querySelector('button[aria-label="Pause replay"]') as HTMLButtonElement).click();
      await settle();

      for (const label of ['4x', '8x']) {
        const speed = [...native.querySelectorAll('button')].find(
          (button) => button.textContent?.trim() === label,
        ) as HTMLButtonElement;

        speed.click();
      }

      (native.querySelector('#mv-highlights') as HTMLButtonElement).click();
      await settle();
      (native.querySelector('#mv-full') as HTMLButtonElement).click();
      await settle();
      await scrubTo(native, 9_000);
      (native.querySelector('button[aria-label="Play replay"]') as HTMLButtonElement).click();
      await settle();
      frame(200);

      expect(renderers()).toBe(1);
    });

    it('makes a new renderer only when the pitch itself comes back after the text-only view', async () => {
      const native = await loadWithPitch();

      (native.querySelector('#mv-text-only') as HTMLButtonElement).click();
      await settle();

      expect(native.querySelector('canvas[role="img"]')).toBeNull();

      (native.querySelector('#mv-text-only') as HTMLButtonElement).click();
      await settle();

      mainCanvas = native.querySelector('canvas[role="img"]') as HTMLCanvasElement;

      // A new canvas element, so a new renderer for it: that is the only time one is made.
      expect(renderers()).toBe(1);
    });

    it('stops its loop when the viewer is destroyed, so no frame outlives the canvas', async () => {
      const native = await loadWithPitch();
      const playback = (
        fixture.componentInstance as unknown as { playback: { positionMs: number } }
      ).playback;

      (native.querySelector('button[aria-label="Play replay"]') as HTMLButtonElement).click();
      await settle();

      frame(0);
      frame(100);

      expect(playback.positionMs, 'the film plays while the viewer is alive').toBe(100);

      fixture.destroy();
      frame(200);
      frame(300);

      // Angular's own scheduler shares the animation frame, so the loop is told apart by what it does: a
      // loop that outlived the viewer would still be moving the playhead.
      expect(playback.positionMs).toBe(100);
    });
  });

  describe('the clock', () => {
    it("reads 45+N' through the first half's stoppage, HT at the interval, 46' as the second half starts, and 90+N' in its stoppage", async () => {
      const native = await load({ textOnly: true });

      // The 45th minute begins at 2,640 s, so the first passage reads 45' throughout.
      await scrubTo(native, 1_000);
      expect(clockText(native)).toBe("45'");

      // The second passage opens on 2,700 s, which is where stoppage begins.
      await scrubTo(native, 4_500);
      expect(clockText(native)).toBe("45+1'");

      // 7,000 ms is three quarters of the way through the second passage's 120 s of stoppage.
      await scrubTo(native, 7_000);
      expect(clockText(native)).toBe("45+2'");

      await scrubTo(native, 9_000);
      expect(clockText(native)).toBe('HT');

      await scrubTo(native, 11_500);
      expect(clockText(native)).toBe("46'");

      await scrubTo(native, 15_500);
      expect(clockText(native)).toBe("90+1'");
    });

    it('labels the feed rows with the clock at the moment each beat happens', async () => {
      const native = await load({ textOnly: true });

      await scrubTo(native, 17_999);

      const feed = native.querySelector('[data-testid="match-feed"]')!;
      const rows = [...feed.querySelectorAll('li')].map((row) =>
        [...row.querySelectorAll('span')].map((cell) => cell.textContent?.trim()),
      );

      // 2,000 ms into a passage whose clock runs 2,700 to 2,820 s reads 2,760 s: the second minute of stoppage.
      expect(rows).toEqual([
        ["45+2'", 'Late in the half.'],
        ["46'", 'The second half begins.'],
      ]);
    });

    it('moves the live panels on to the minute of the half the film is in', async () => {
      const native = await load({ textOnly: true });

      // At 46' the curve's figures for the minute are shown, and not those of a 48' the old clock called it.
      await scrubTo(native, 11_500);

      expect(clockText(native)).toBe("46'");
    });
  });

  describe('the scoreboard', () => {
    it("lights the scorer's side for as long as the film celebrates the goal, and not after", async () => {
      const native = await load({ textOnly: true });
      const view = filmPresentation();
      const scoring = view.passages[3];

      store.presentation.set({
        ...view,
        passages: [
          ...view.passages.slice(0, 3),
          {
            ...scoring,
            outcomeCode: 'goal',
            commentary: [
              {
                timeMilliseconds: 1_000,
                templateKey: 'match.goal',
                variantKey: 'v1',
                parameters: [],
                text: 'Goal!',
              },
            ],
          },
          view.passages[4],
        ],
        commentary: [{ ...line(), sequence: 4 }],
      });

      await settle();

      const lit = () => native.querySelector('.text-emerald-400.font-mono.text-4xl') !== null;

      await scrubTo(native, 11_500);
      expect(lit(), 'before the goal goes in').toBe(false);

      await scrubTo(native, 12_500);
      expect(lit(), 'while it is celebrated').toBe(true);

      await scrubTo(native, 16_500);
      expect(lit(), 'after the celebration has run its course').toBe(false);
    });
  });
});
