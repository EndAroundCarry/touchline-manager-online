import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { vi } from 'vitest';
import { PlayerSearchResult } from '../../../core/scouting/scouting.models';
import { ScoutingApi } from '../../../core/scouting/scouting-api';
import { PlayerAttributes } from '../../../core/squad/squad.models';
import { CARD_MARGIN, CARD_WIDTH, placeCard } from './player-card-placement';
import { PlayerCardService } from './player-card.service';

function attributes(value: number): PlayerAttributes {
  const fill = (keys: readonly string[]) => Object.fromEntries(keys.map((key) => [key, value]));

  return {
    technical: fill([
      'finishing',
      'passing',
      'crossing',
      'dribbling',
      'firstTouch',
      'tackling',
      'marking',
      'heading',
      'technique',
      'setPieces',
    ]),
    mental: fill([
      'decisions',
      'vision',
      'positioning',
      'composure',
      'anticipation',
      'workRate',
      'aggression',
      'leadership',
    ]),
    physical: fill(['pace', 'acceleration', 'stamina', 'strength', 'agility', 'jumpingReach']),
    goalkeeping: fill(['handling', 'reflexes', 'oneOnOnes', 'aerialAbility']),
  } as unknown as PlayerAttributes;
}

function player(id: string, name: string): PlayerSearchResult {
  return {
    playerId: id,
    fullName: name,
    shortName: name.slice(0, 3),
    nationalityCode: 'GB',
    age: 24,
    preferredFoot: 'right',
    primaryPosition: 'cb',
    secondaryPositions: [],
    clubId: 'club',
    clubName: 'Ashford',
    tierNumber: 5,
    divisionId: 'division',
    divisionName: 'Division',
    isListed: false,
    attributes: attributes(12),
  };
}

const ANCHOR = { x: 100, top: 100, bottom: 120 };

describe('PlayerCardService', () => {
  const reads = new Map<string, Subject<PlayerSearchResult>>();
  let calls: string[];

  beforeEach(() => {
    vi.useFakeTimers();
    calls = [];
    reads.clear();

    TestBed.configureTestingModule({
      providers: [
        {
          provide: ScoutingApi,
          useValue: {
            player: (id: string) => {
              calls.push(id);
              const subject = new Subject<PlayerSearchResult>();

              reads.set(id, subject);

              return subject;
            },
          },
        },
      ],
    });
  });

  afterEach(() => vi.useRealTimers());

  it('asks for a player only after the pointer has rested on the name', () => {
    const service = TestBed.inject(PlayerCardService);

    service.show('a', ANCHOR);
    service.hide();
    vi.advanceTimersByTime(500);

    expect(calls).toEqual([]);
    expect(service.card()).toBeNull();
  });

  it('shows the skills for the position the player plays, once they are read', () => {
    const service = TestBed.inject(PlayerCardService);

    service.show('a', ANCHOR);
    vi.advanceTimersByTime(200);
    reads.get('a')!.next(player('a', 'Alan Alder'));

    const card = service.card();

    expect(card?.name).toBe('Alan Alder');
    expect(card?.subtitle).toContain('age 24');
    expect(card?.subtitle).toContain('Ashford');
    expect(card?.groups.map((group) => group.key)).toEqual([
      'technical',
      'mental',
      'physical',
      'goalkeeping',
    ]);
  });

  it('drops a slow answer for a name the pointer has already left', () => {
    const service = TestBed.inject(PlayerCardService);

    service.show('a', ANCHOR);
    vi.advanceTimersByTime(200);
    service.show('b', ANCHOR);
    reads.get('a')!.next(player('a', 'Alan Alder'));

    expect(service.card()).toBeNull();

    vi.advanceTimersByTime(200);
    reads.get('b')!.next(player('b', 'Ben Birch'));

    expect(service.card()?.name).toBe('Ben Birch');
  });

  it('shows a player it has just read straight away, without asking again', () => {
    const service = TestBed.inject(PlayerCardService);

    service.show('a', ANCHOR);
    vi.advanceTimersByTime(200);
    reads.get('a')!.next(player('a', 'Alan Alder'));
    service.hide();
    service.show('a', ANCHOR);

    expect(service.card()?.name).toBe('Alan Alder');
    expect(calls).toEqual(['a']);
  });

  it('puts the card away', () => {
    const service = TestBed.inject(PlayerCardService);

    service.show('a', ANCHOR);
    vi.advanceTimersByTime(200);
    reads.get('a')!.next(player('a', 'Alan Alder'));
    service.hide();

    expect(service.card()).toBeNull();
  });
});

describe('placeCard', () => {
  it('opens below the anchor when there is room', () => {
    expect(placeCard({ x: 100, top: 100, bottom: 120 }, 1440, 900)).toEqual({
      left: 100,
      top: 120 + CARD_MARGIN,
    });
  });

  it('opens above the anchor near the bottom of the window', () => {
    const placement = placeCard({ x: 100, top: 800, bottom: 820 }, 1440, 900);

    expect(placement.top).toBeLessThan(800);
  });

  it('never runs past the right edge or the left edge', () => {
    expect(placeCard({ x: 1400, top: 100, bottom: 120 }, 1440, 900).left).toBe(
      1440 - CARD_WIDTH - CARD_MARGIN,
    );
    expect(placeCard({ x: -50, top: 100, bottom: 120 }, 1440, 900).left).toBe(CARD_MARGIN);
  });
});
