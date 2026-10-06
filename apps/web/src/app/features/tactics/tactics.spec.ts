import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { SquadStore } from '../../core/squad/squad-store';
import { TacticsApi } from '../../core/tactics/tactics-api';
import { defaultInstructions } from '../../core/tactics/tactics-draft';
import { TacticsStore } from '../../core/tactics/tactics-store';
import {
  FormationPreset,
  SelectablePlayer,
  TacticalPlan,
  Tactics,
} from '../../core/tactics/tactics.models';
import { Tactics as TacticsPage } from './tactics';

/**
 * The substitutes on the tactics board: seven dots under the pitch, the first for a goalkeeper (`SQ-4`).
 *
 * The draft's rules are pinned in `tactics-draft.spec.ts`; what only a rendered page shows is that the dots
 * are there, that they are reachable by name, and that picking a player for one reaches the plan being
 * saved.
 */

const FAMILIES = ['goalkeeper', 'defence', 'defence', 'defence', 'defence'].concat([
  'midfield',
  'midfield',
  'midfield',
  'midfield',
  'attack',
  'attack',
]);

const formation: FormationPreset = {
  code: '4-4-2',
  slots: FAMILIES.map((family, index) => ({
    slotNumber: index + 1,
    positionFamily: family,
    role: family === 'goalkeeper' ? 'goalkeeper' : 'central_midfielder',
    normalizedX: (index + 1) * 800,
    normalizedY: (index % 4) * 2_000 + 1_000,
  })),
};

const players: SelectablePlayer[] = [
  player('gk1', 'Alaric Alderwick', 'gk', 'goalkeeper'),
  player('gk2', 'Bram Bellweather', 'gk', 'goalkeeper'),
  player('cb1', 'Corin Calloway', 'cb', 'defence'),
  player('st1', 'Dorian Draycott', 'st', 'attack'),
];

function player(id: string, fullName: string, position: string, family: string): SelectablePlayer {
  return {
    id,
    fullName,
    shortName: fullName.slice(0, 3).toUpperCase(),
    primaryPosition: position,
    positionFamily: family,
    isUnavailable: false,
  };
}

function plan(): TacticalPlan {
  return {
    id: 'plan-1',
    name: 'Home shape',
    formationPreset: '4-4-2',
    isDefault: true,
    instructions: defaultInstructions(),
    version: 1,
    assignedCount: 0,
    isComplete: false,
    slots: formation.slots.map((slot) => ({
      ...slot,
      assignedPlayer: null,
      isOutOfPosition: false,
    })),
    bench: [],
  };
}

function tactics(): Tactics {
  return {
    clubId: 'club-1',
    clubName: 'Ashvale United',
    clubShortName: 'ASH',
    countryCode: 'ENG',
    seasonNumber: 1,
    plans: [plan()],
    selectablePlayers: players,
    formations: [formation],
    serverTime: '2026-10-06T00:00:00Z',
  };
}

describe('Tactics bench', () => {
  let fixture: ComponentFixture<TacticsPage>;
  let element: HTMLElement;
  let store: TacticsStore;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: TacticsApi, useValue: { get: () => of(tactics()) } },
        { provide: SquadStore, useValue: { loadSquad: () => of({ players: [] }) } },
        { provide: MaintenanceStore, useValue: { canMutate: signal(true) } },
      ],
    });

    store = TestBed.inject(TacticsStore);
    fixture = TestBed.createComponent(TacticsPage);
    element = fixture.nativeElement as HTMLElement;

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  function dots(): HTMLButtonElement[] {
    return [
      ...element.querySelectorAll<HTMLButtonElement>(
        '[data-testid="bench"] button[data-testid^="bench-"]',
      ),
    ];
  }

  function pick(name: string): void {
    const button = [
      ...element.querySelectorAll<HTMLButtonElement>('[data-testid="players-table"] button'),
    ].find((candidate) => candidate.textContent?.trim() === name);

    expect(button, `a table row named ${name}`).toBeTruthy();
    button?.click();
    fixture.detectChanges();
  }

  it('draws seven substitutes under the pitch, the first of them the goalkeeper', () => {
    expect(dots()).toHaveLength(7);
    expect(dots()[0].textContent).toContain('GK');
    expect(dots()[0].getAttribute('aria-label')).toBe('Substitute 1, reserve goalkeeper: empty');
    expect(dots()[1].getAttribute('aria-label')).toBe('Substitute 2: empty');
    expect(element.querySelector('[data-testid="bench"]')?.textContent).toContain('0 of 7');
  });

  it('puts a goalkeeper on the first dot and shows them there', () => {
    dots()[0].click();
    fixture.detectChanges();
    pick('Bram Bellweather');

    expect(dots()[0].getAttribute('aria-label')).toBe(
      'Substitute 1, reserve goalkeeper: Bram Bellweather',
    );
    expect(store.benchCount()).toBe(1);
    expect(element.querySelector('[data-testid="bench-notice"]')).toBeNull();
    expect(store.isDirty()).toBe(true);
  });

  it('refuses an outfield player for the goalkeeper dot and says why', () => {
    dots()[0].click();
    fixture.detectChanges();
    pick('Corin Calloway');

    expect(store.benchCount()).toBe(0);
    expect(element.querySelector('[data-testid="bench-notice"]')?.textContent).toContain(
      'reserve goalkeeper',
    );
  });

  it('lets any player, a second goalkeeper included, take one of the other six dots', () => {
    dots()[3].click();
    fixture.detectChanges();
    pick('Dorian Draycott');

    dots()[4].click();
    fixture.detectChanges();
    pick('Alaric Alderwick');

    expect(store.benchCount()).toBe(2);
    expect(dots()[3].getAttribute('aria-label')).toBe('Substitute 4: Dorian Draycott');
    expect(dots()[4].getAttribute('aria-label')).toBe('Substitute 5: Alaric Alderwick');
  });

  it('empties a dot from the line under the bench', () => {
    dots()[2].click();
    fixture.detectChanges();
    pick('Corin Calloway');

    expect(store.benchCount()).toBe(1);

    const empty = [
      ...element.querySelectorAll<HTMLButtonElement>('[data-testid="bench"] button'),
    ].find((button) => button.textContent?.includes('empty it'));

    empty?.click();
    fixture.detectChanges();

    expect(store.benchCount()).toBe(0);
  });
});
