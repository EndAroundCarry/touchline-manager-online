import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { CommentaryLine, Highlight, Match, MatchPresentation } from '../../core/match/match.models';
import { MatchStore } from '../../core/match/match-store';
import { MatchViewer } from './match-viewer';

/**
 * The viewer's wiring from a loaded presentation to a working replay (`§9.4`).
 *
 * The playback state machine, the renderer, and the store are all tested on their own elsewhere; what was
 * never tested is the seam between them — that the viewer builds its player from the presentation it was
 * given. A viewer that kept an empty player would render a complete, believable match center whose Play
 * button did nothing, which is exactly the bug this guards.
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
    narration: 'Goal — a test scorer, 8.',
    homeColour: '#1f6f43',
    awayColour: '#7a1f2b',
    entities: [],
    tracks: [],
  };
}

function line(): CommentaryLine {
  return {
    sequence: 5,
    minute: 8,
    stoppageMinute: 0,
    side: 'home',
    templateKey: 'match.goal',
    variantKey: 'a',
    parameters: [],
    text: 'It is in.',
  };
}

function presentation(): MatchPresentation {
  return {
    matchId: 'm1',
    presentationVersion: 'highlights-v1',
    engineVersion: 'engine-v1',
    homeGoals: 1,
    awayGoals: 0,
    commentary: [line()],
    highlights: [highlight()],
    estimatedPayloadBytes: 2_048,
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
    engineVersion: 'engine-v1',
    presentationVersion: 'highlights-v1',
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

  it('builds its replay from the presentation, so Play starts it', async () => {
    const native = fixture.nativeElement as HTMLElement;

    // Text-only before the presentation arrives, so the renderer never needs a 2D context.
    (fixture.componentInstance as unknown as { toggleTextOnly(): void }).toggleTextOnly();

    store.match.set(match());
    store.presentation.set(presentation());

    await fixture.whenStable();

    const play = buttonByText(native, 'Play');

    expect(play, 'the replay offers Play once it has a highlight to play').toBeDefined();

    play!.click();

    await fixture.whenStable();

    expect(
      buttonByText(native, 'Pause'),
      'Play starts the replay, so the control becomes Pause',
    ).toBeDefined();
    expect(buttonByText(native, 'Play')).toBeUndefined();
  });
});
