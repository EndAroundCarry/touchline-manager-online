import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { CommentaryLine, Highlight, Match, MatchPresentation } from '../../core/match/match.models';
import { MatchStore } from '../../core/match/match-store';
import { MatchViewer } from './match-viewer';

/**
 * The viewer's wiring from a loaded presentation to a working FM-style match center (`§9.4`, `§9.5`).
 *
 * The playback state machine, the renderer, and the store are all tested on their own elsewhere; what was
 * never tested is the seam between them — that the viewer builds its player from the presentation it was
 * given, that the panels follow the minute-by-minute curve the server captured, and that the tabs show the
 * replay's own facts. A viewer that kept an empty player would render a complete, believable match center
 * whose Play button did nothing, which is exactly the bug this guards.
 *
 * The control is exercised in text-only mode (`§9.4`), because a headless DOM has no canvas 2D context and
 * the replay's controls are what this seam is about.
 */

function highlight(): Highlight {
  return {
    sourceEventSequence: 5,
    minute: 8,
    stoppageMinute: 0,
    durationMilliseconds: 6_000,
    outcomeCode: 'goal',
    narration: 'Goal — A Scorer, 8.',
    homeColour: '#1f6f43',
    awayColour: '#7a1f2b',
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
        templateKey: 'match.passage.build_up',
        variantKey: 'match.passage.build_up.v1',
        parameters: [],
        text: 'Ashvale United build the move.',
      },
      {
        timeMilliseconds: 3_000,
        templateKey: 'match.passage.shot',
        variantKey: 'match.passage.shot.v1',
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
    presentationVersion: 'replay-v2',
    engineVersion: 'engine-v3',
    homeGoals: 1,
    awayGoals: 0,
    commentary: [line()],
    highlights: [highlight()],
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
    bridges: [],
    playback: [],
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
    engineVersion: 'engine-v3',
    presentationVersion: 'replay-v2',
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

    expect(play, 'the replay offers Play once it has a highlight to play').toBeDefined();

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

    // At minute eight the curve's own figures are on the panel, not an interpolation to full time.
    expect(native.textContent).toContain('7.4');
    expect(barWidth(native)).toBe('80%');
  });

  it("overwrites the ticker with the passage's own commentary", async () => {
    const native = await load();

    expect(native.textContent).toContain('Ashvale United build the move.');
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

  it('offers the condensed replay by default and cuts to highlights on request', async () => {
    const native = await load();

    const condensed = buttonByText(native, 'Condensed')!;
    const highlights = buttonByText(native, 'Highlights')!;

    expect(condensed.getAttribute('aria-pressed')).toBe('true');
    expect(highlights.getAttribute('aria-pressed')).toBe('false');

    // Switching modes resumes on the same highlight rather than restarting the replay.
    highlights.click();

    await fixture.whenStable();

    expect(highlights.getAttribute('aria-pressed')).toBe('true');
    expect(condensed.getAttribute('aria-pressed')).toBe('false');
  });
});

/** The width of the first condition bar on the screen. */
function barWidth(native: HTMLElement): string {
  const bar = native.querySelector('.bg-emerald-500, .bg-lime-500, .bg-amber-500, .bg-rose-500');

  return bar === null ? '' : (bar as HTMLElement).style.width;
}
