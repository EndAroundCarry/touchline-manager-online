import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ApiError } from '../api/api-error';
import { CompetitionApi } from './competition-api';
import { CompetitionStore } from './competition-store';
import {
  DivisionTable,
  FixtureTeamSheet,
  MyFixtures,
  TeamSheetSlot,
  TeamSheetValidation,
} from './competition.models';

/**
 * The competition store's guarantees.
 *
 * The concurrency contract is the point, as it is for the training store: a first save carries no
 * `If-Match` and a replacement carries the sheet's version, a `412` keeps the manager's selection and pulls
 * the server's state, and a refused selection arrives as a validation preview rather than a bare message
 * (CONC-1, ADR-0009, §11.2, SQ-4).
 */

function slot(slotNumber: number, designation: string, playerId: string | null): TeamSheetSlot {
  return {
    slotNumber,
    designation,
    positionFamily: designation === 'starter' ? 'defence' : null,
    role: designation === 'starter' ? 'centre_back' : null,
    player:
      playerId === null
        ? null
        : {
            id: playerId,
            fullName: `Player ${playerId}`,
            shortName: 'PLY',
            primaryPosition: 'cb',
            isUnavailable: false,
          },
  };
}

function teamSheet(overrides: Partial<FixtureTeamSheet> = {}): FixtureTeamSheet {
  return {
    fixtureId: 'f1',
    clubId: 'c1',
    clubName: 'Ashvale United',
    clubShortName: 'ASH',
    opponentClubId: 'c2',
    opponentName: 'Bramford Rovers',
    opponentShortName: 'BRA',
    venue: 'home',
    divisionId: 'd1',
    divisionName: 'England Top Division',
    roundNumber: 1,
    kickoffAt: '2026-10-06T19:00:00Z',
    lockAt: '2026-10-06T18:30:00Z',
    fixtureStatus: 'scheduled',
    isLocked: false,
    planId: 'plan-1',
    planName: 'Shape',
    formationPreset: '4-4-2',
    planVersion: 1,
    sheetVersion: null,
    sheetStatus: 'draft',
    slots: [
      slot(1, 'starter', 'p1'),
      ...Array.from({ length: 10 }, (_, index) => slot(index + 2, 'starter', `s${index + 2}`)),
      slot(12, 'substitute', null),
    ],
    selectablePlayers: [
      {
        id: 'p1',
        fullName: 'Player p1',
        shortName: 'P1',
        primaryPosition: 'cb',
        positionFamily: 'defence',
        isUnavailable: false,
      },
      {
        id: 'p2',
        fullName: 'Player p2',
        shortName: 'P2',
        primaryPosition: 'st',
        positionFamily: 'attack',
        isUnavailable: true,
      },
    ],
    serverTime: '2026-09-25T00:00:00Z',
    ...overrides,
  };
}

function fixtures(): MyFixtures {
  return {
    clubId: 'c1',
    clubName: 'Ashvale United',
    clubShortName: 'ASH',
    divisionId: 'd1',
    divisionName: 'England Top Division',
    tierNumber: 1,
    seasonNumber: 1,
    seasonLabel: '2026/27',
    nextFixtureId: 'f1',
    fixtures: [],
    serverTime: '2026-09-25T00:00:00Z',
  };
}

function table(): DivisionTable {
  return {
    divisionId: 'd1',
    divisionName: 'England Top Division',
    tierNumber: 1,
    countryId: 'co1',
    countryCode: 'england',
    countryName: 'England',
    seasonNumber: 1,
    seasonLabel: '2026/27',
    rows: [
      {
        rank: 1,
        clubId: 'c1',
        clubName: 'Ashvale United',
        clubShortName: 'ASH',
        played: 0,
        won: 0,
        drawn: 0,
        lost: 0,
        goalsFor: 0,
        goalsAgainst: 0,
        goalDifference: 0,
        points: 0,
        yellowCards: 0,
        redCards: 0,
      },
    ],
    serverTime: '2026-09-25T00:00:00Z',
  };
}

function createApiStub() {
  return {
    mine: vi.fn(),
    fixture: vi.fn(),
    divisionFixtures: vi.fn(),
    divisionTable: vi.fn(),
    teamSheet: vi.fn(),
    saveTeamSheet: vi.fn(),
  };
}

describe('CompetitionStore', () => {
  let api: ReturnType<typeof createApiStub>;
  let store: CompetitionStore;

  beforeEach(() => {
    api = createApiStub();

    TestBed.configureTestingModule({
      providers: [CompetitionStore, { provide: CompetitionApi, useValue: api }],
    });

    store = TestBed.inject(CompetitionStore);
  });

  it('reads the manager club fixture list', () => {
    api.mine.mockReturnValue(of(fixtures()));

    store.loadFixtures();

    expect(store.fixtures()).not.toBeNull();
    expect(store.fixtures()!.nextFixtureId).toBe('f1');
    expect(store.fixturesLoading()).toBe(false);
  });

  it('starts editing from the stored side', () => {
    api.teamSheet.mockReturnValue(of(teamSheet()));

    store.loadTeamSheet('f1');

    expect(store.teamSheet()).not.toBeNull();
    expect(store.selection().get(1)).toBe('p1');
    expect(store.starterSlots()).toHaveLength(11);
    expect(store.substituteSlots()).toHaveLength(1);
    expect(store.isDirty()).toBe(false);
  });

  it('moves a player out of the slot they were in rather than duplicating them', () => {
    api.teamSheet.mockReturnValue(of(teamSheet()));

    store.loadTeamSheet('f1');
    store.assign(12, 'p1');

    expect(store.selection().get(12)).toBe('p1');
    expect(store.selection().has(1)).toBe(false);
    expect(store.isDirty()).toBe(true);
  });

  it('sends no version for the first save and the sheet version once one exists', () => {
    api.teamSheet.mockReturnValue(of(teamSheet()));
    api.saveTeamSheet.mockReturnValue(of(teamSheet({ sheetVersion: 2 })));

    store.loadTeamSheet('f1');
    store.assign(1, 'p2');
    store.save();

    expect(api.saveTeamSheet).toHaveBeenCalledWith('f1', expect.anything(), undefined);

    api.saveTeamSheet.mockReturnValue(of(teamSheet({ sheetVersion: 3 })));

    store.assign(1, 'p1');
    store.save();

    expect(api.saveTeamSheet).toHaveBeenLastCalledWith('f1', expect.anything(), '"2"');
  });

  it('keeps the selection and offers a reapply when the sheet changed underneath', () => {
    api.teamSheet.mockReturnValue(of(teamSheet({ sheetVersion: 2 })));
    api.saveTeamSheet.mockReturnValue(
      throwError(() => new ApiError(412, 'PRECONDITION_FAILED', 'stale', null, new Map())),
    );

    store.loadTeamSheet('f1');
    store.assign(12, 'p2');
    store.save();

    expect(store.hasConflict()).toBe(true);
    expect(store.selection().get(12)).toBe('p2');
    expect(store.saveError()).toBeNull();
  });

  it('reapplies against the version that arrived rather than the one that was refused', () => {
    // A 412 reloads asynchronously, so a reapply clicked the moment the conflict appears must not re-send
    // the version that was just refused (§11.2).
    api.teamSheet.mockReturnValue(of(teamSheet({ sheetVersion: 1 })));
    api.saveTeamSheet.mockReturnValue(
      throwError(() => new ApiError(412, 'PRECONDITION_FAILED', 'stale', null, new Map())),
    );

    store.loadTeamSheet('f1');
    store.assign(12, 'p2');
    store.save();

    expect(store.hasConflict()).toBe(true);

    api.teamSheet.mockReturnValue(of(teamSheet({ sheetVersion: 5 })));
    api.saveTeamSheet.mockReturnValue(of(teamSheet({ sheetVersion: 6 })));

    store.reapply();

    expect(api.saveTeamSheet).toHaveBeenLastCalledWith('f1', expect.anything(), '"5"');
    expect(store.hasConflict()).toBe(false);
    expect(store.teamSheet()?.sheetVersion).toBe(6);
  });

  it('surfaces a refused selection as a validation preview', () => {
    const validation: TeamSheetValidation = {
      isValid: false,
      starterCount: 10,
      substituteCount: 0,
      issues: [{ code: 'TEAM_SHEET_INCOMPLETE', slotNumber: null, playerId: null }],
    };

    api.teamSheet.mockReturnValue(of(teamSheet()));
    api.saveTeamSheet.mockReturnValue(
      throwError(
        () =>
          new ApiError(
            400,
            'TEAM_SHEET_VALIDATION_FAILED',
            'bad',
            null,
            new Map(),
            new Map([['validation', validation]]),
          ),
      ),
    );

    store.loadTeamSheet('f1');
    store.save();

    expect(store.validation()).toEqual(validation);
    expect(store.saveError()).toBeNull();
  });

  it('reads the manager club division table and names their own club', () => {
    api.mine.mockReturnValue(of(fixtures()));
    api.divisionTable.mockReturnValue(of(table()));

    store.loadMyDivisionTable();

    expect(api.divisionTable).toHaveBeenCalledWith('d1');
    expect(store.divisionTable()?.divisionName).toBe('England Top Division');
    expect(store.managedClubId()).toBe('c1');
    expect(store.tableLoading()).toBe(false);
  });

  it('reads a named division without marking a club', () => {
    api.divisionTable.mockReturnValue(of(table()));

    store.loadDivisionTable('d9');

    expect(api.divisionTable).toHaveBeenCalledWith('d9');
    expect(store.divisionTable()).not.toBeNull();
    expect(store.managedClubId()).toBeNull();
    expect(api.mine).not.toHaveBeenCalled();
  });

  it('reports a table it could not read', () => {
    api.divisionTable.mockReturnValue(
      throwError(
        () => new ApiError(404, 'DIVISION_NOT_FOUND', 'no such division', null, new Map()),
      ),
    );

    store.loadDivisionTable('d9');

    expect(store.divisionTable()).toBeNull();
    expect(store.tableError()).toBe('no such division');
    expect(store.tableLoading()).toBe(false);
  });

  it('forgets everything when the session ends', () => {
    api.mine.mockReturnValue(of(fixtures()));
    api.divisionTable.mockReturnValue(of(table()));
    api.teamSheet.mockReturnValue(of(teamSheet()));

    store.loadMyDivisionTable();
    store.loadTeamSheet('f1');
    store.clear();

    expect(store.teamSheet()).toBeNull();
    expect(store.selection().size).toBe(0);
    expect(store.divisionTable()).toBeNull();
    expect(store.managedClubId()).toBeNull();
  });
});
