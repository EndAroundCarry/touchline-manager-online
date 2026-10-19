import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { SquadStore } from '../../core/squad/squad-store';
import { MAX_MATCH_INSTRUCTIONS } from '../../core/tactics/match-instructions';
import { TacticsApi } from '../../core/tactics/tactics-api';
import { defaultInstructions } from '../../core/tactics/tactics-draft';
import {
  FormationPreset,
  SaveTacticalPlanRequest,
  SelectablePlayer,
  TacticalPlan,
  Tactics,
} from '../../core/tactics/tactics.models';
import { Tactics as TacticsPage } from './tactics';

/**
 * The tactics screen's two tabs and the Match instructions editor.
 *
 * The rules the instructions follow are pinned in `match-instructions.spec.ts` and `match-instructions-draft.spec.ts`; what only a
 * rendered page shows is that the tabs work by mouse and keyboard, that an instruction can be added, edited, ordered
 * and removed, that a new shape is drawn and a player can be placed in it, and that all of it reaches the request a save sends.
 */

const SLOT_LAYOUT: readonly { family: string; role: string; position: string }[] = [
  { family: 'goalkeeper', role: 'goalkeeper', position: 'gk' },
  { family: 'defence', role: 'full_back', position: 'rb' },
  { family: 'defence', role: 'centre_back', position: 'cb' },
  { family: 'defence', role: 'centre_back', position: 'cb' },
  { family: 'defence', role: 'full_back', position: 'lb' },
  { family: 'midfield', role: 'wide_midfielder', position: 'rm' },
  { family: 'midfield', role: 'central_midfielder', position: 'cm' },
  { family: 'midfield', role: 'central_midfielder', position: 'cm' },
  { family: 'midfield', role: 'wide_midfielder', position: 'lm' },
  { family: 'attack', role: 'striker', position: 'st' },
  { family: 'attack', role: 'striker', position: 'st' },
];

function preset(code: string, wingers: boolean): FormationPreset {
  return {
    code,
    slots: SLOT_LAYOUT.map((layout, index) => ({
      slotNumber: index + 1,
      positionFamily: wingers && index === 8 ? 'attack' : layout.family,
      role: wingers && index === 8 ? 'winger' : layout.role,
      normalizedX: 500 + index * 700,
      normalizedY: 1_000 + (index % 5) * 2_000,
    })),
  };
}

const formations = [preset('4-4-2', false), preset('4-3-3', true)];

const starters: SelectablePlayer[] = SLOT_LAYOUT.map((layout, index) => ({
  id: `s${index + 1}`,
  fullName: `Starter ${index + 1}`,
  shortName: `STA${index + 1}`,
  primaryPosition: layout.position,
  positionFamily: layout.family,
  isUnavailable: false,
}));

const substitutes: SelectablePlayer[] = [
  {
    id: 'b1',
    fullName: 'Reserve Keeper',
    shortName: 'RKP',
    primaryPosition: 'gk',
    positionFamily: 'goalkeeper',
    isUnavailable: false,
  },
  {
    id: 'b2',
    fullName: 'Fresh Forward',
    shortName: 'FFW',
    primaryPosition: 'st',
    positionFamily: 'attack',
    isUnavailable: false,
  },
];

function assigned(player: SelectablePlayer) {
  return {
    id: player.id,
    fullName: player.fullName,
    shortName: player.shortName,
    primaryPosition: player.primaryPosition,
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
    assignedCount: 11,
    isComplete: true,
    slots: formations[0].slots.map((slot, index) => ({
      ...slot,
      assignedPlayer: assigned(starters[index]),
      isOutOfPosition: false,
    })),
    bench: substitutes.map((player, index) => ({
      slotNumber: 12 + index,
      assignedPlayer: assigned(player),
    })),
    matchInstructions: [],
    assistantFatigueSubstitutions: true,
    matchInstructionWarnings: [],
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
    selectablePlayers: [...starters, ...substitutes],
    formations,
    serverTime: '2026-10-17T00:00:00Z',
  };
}

describe('Tactics tabs and match instructions', () => {
  let fixture: ComponentFixture<TacticsPage>;
  let element: HTMLElement;
  let api: { get: ReturnType<typeof vi.fn>; update: ReturnType<typeof vi.fn> };
  let navigate: ReturnType<typeof vi.spyOn>;

  async function create(queryTab: string | null = null): Promise<void> {
    api = { get: vi.fn(() => of(tactics())), update: vi.fn() };

    api.update.mockImplementation((_id: string, request: SaveTacticalPlanRequest) =>
      of({ ...plan(), version: 2, matchInstructions: request.matchInstructions ?? [] }),
    );

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap(queryTab === null ? {} : { tab: queryTab }),
            },
          },
        },
        { provide: TacticsApi, useValue: api },
        { provide: SquadStore, useValue: { loadSquad: () => of({ players: [] }) } },
        { provide: MaintenanceStore, useValue: { canMutate: signal(true) } },
      ],
    });

    // The route above is a stub with only a snapshot, so the address is never really changed: the tab the page shows is its own state.
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    fixture = TestBed.createComponent(TacticsPage);
    element = fixture.nativeElement as HTMLElement;

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function byId<T extends HTMLElement = HTMLElement>(testId: string): T {
    const found = element.querySelector<T>(`[data-testid="${testId}"]`);

    expect(found, `an element with data-testid ${testId}`).toBeTruthy();

    return found as T;
  }

  function maybe(testId: string): HTMLElement | null {
    return element.querySelector<HTMLElement>(`[data-testid="${testId}"]`);
  }

  function click(testId: string): void {
    byId(testId).click();
    fixture.detectChanges();
  }

  function choose(testId: string, value: string): void {
    const select = byId<HTMLSelectElement>(testId);

    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function type(testId: string, value: string): void {
    const input = byId<HTMLInputElement>(testId);

    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function summary(ordinal: number): string {
    return byId(`summary-${ordinal}`).textContent?.replace(/\s+/g, ' ').trim() ?? '';
  }

  function saveButton(): HTMLButtonElement {
    return [...element.querySelectorAll<HTMLButtonElement>('button')].find((button) =>
      /Save plan|Create plan/.test(button.textContent ?? ''),
    ) as HTMLButtonElement;
  }

  async function openInstructionsAndAdd(): Promise<void> {
    click('tab-instructions');
    click('add-instruction');
  }

  /** Sets the open, blank rule to: from 60', losing, one step more attacking, a forward on for the most tired midfielder. */
  function fillChase(): void {
    type('rule-from', '60');
    choose('rule-score', 'trailing');
    choose('rule-mentality', 'steps:1');
    click('add-substitution');
    choose('sub-off', 'most_tired_in_family');
    choose('sub-off-family', 'midfield');
    choose('sub-on', 'best_fit_in_family');
    choose('sub-on-family', 'attack');
  }

  /** Sets the open, blank rule to: when one of the side is sent off, one step more defensive. */
  function fillTenMen(): void {
    type('rule-name', 'Down to ten men');
    choose('rule-event', 'own_sent_off');
    choose('rule-mentality', 'steps:-1');
  }

  /** Sets the open, blank rule to: from 60', whatever the score, move to a 4-3-3. */
  function fillPlannedChange(): void {
    type('rule-from', '60');
    choose('rule-formation', '4-3-3');
  }

  describe('the tabs', () => {
    beforeEach(async () => {
      await create();
    });

    it('opens on Match tactics, with the board showing and Match instructions one tab away', () => {
      expect(byId('tab-tactics').getAttribute('aria-selected')).toBe('true');
      expect(byId('tab-instructions').getAttribute('aria-selected')).toBe('false');
      expect(byId('tab-instructions').getAttribute('tabindex')).toBe('-1');
      expect(element.querySelector('[data-testid="players-table"]')).toBeTruthy();
      expect(maybe('match-instructions')).toBeNull();
      expect(byId('tab-tactics').getAttribute('aria-controls')).toBe('tactics-panel-tactics');
      expect(element.querySelector('#tactics-panel-tactics')?.getAttribute('aria-labelledby')).toBe(
        'tactics-tab-tactics',
      );
    });

    it("shows the saved plan's own choices in its selects, not the first option of each", () => {
      const valueOf = (option: string): string | undefined =>
        [...element.querySelectorAll('select')].find((select) =>
          select.querySelector(`option[value="${option}"]`),
        )?.value;

      expect(valueOf('cautious')).toBe('balanced');
      expect(valueOf('mid_block')).toBe('mid_block');
      expect(valueOf('4-3-3')).toBe('4-4-2');
      expect(valueOf('plan-1')).toBe('plan-1');
    });

    it('switches to Match instructions and back, leaving the plan header and the save row in place', () => {
      click('tab-instructions');

      expect(byId('tab-instructions').getAttribute('aria-selected')).toBe('true');
      expect(byId('match-instructions')).toBeTruthy();
      expect(element.querySelector('[data-testid="players-table"]')).toBeNull();
      expect(element.textContent).toContain('Plan name');
      expect(saveButton()).toBeTruthy();

      click('tab-tactics');

      expect(maybe('match-instructions')).toBeNull();
      expect(element.querySelector('[data-testid="players-table"]')).toBeTruthy();
    });

    it('moves between the tabs with the arrow keys, Home and End', () => {
      const press = (testId: string, key: string): void => {
        byId(testId).dispatchEvent(
          new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
        );
        fixture.detectChanges();
      };

      press('tab-tactics', 'ArrowRight');
      expect(byId('tab-instructions').getAttribute('aria-selected')).toBe('true');
      expect(document.activeElement).toBe(byId('tab-instructions'));

      press('tab-instructions', 'ArrowRight');
      expect(byId('tab-tactics').getAttribute('aria-selected')).toBe('true');

      press('tab-tactics', 'End');
      expect(byId('tab-instructions').getAttribute('aria-selected')).toBe('true');

      press('tab-instructions', 'Home');
      expect(byId('tab-tactics').getAttribute('aria-selected')).toBe('true');

      press('tab-tactics', 'ArrowLeft');
      expect(byId('tab-instructions').getAttribute('aria-selected')).toBe('true');
    });

    it('records the open tab in the address and clears it on the first tab', () => {
      click('tab-instructions');
      expect(navigate.mock.calls[0][1]?.queryParams).toEqual({ tab: 'instructions' });

      click('tab-tactics');
      expect(navigate.mock.calls[1][1]?.queryParams).toEqual({ tab: null });
    });
  });

  it('opens on Match instructions when the address asks for it', async () => {
    await create('instructions');

    expect(byId('tab-instructions').getAttribute('aria-selected')).toBe('true');
    expect(byId('match-instructions')).toBeTruthy();
  });

  it('opens on Match tactics for a tab it does not know', async () => {
    await create('nonsense');

    expect(byId('tab-tactics').getAttribute('aria-selected')).toBe('true');
  });

  describe('the instruction list', () => {
    beforeEach(async () => {
      await create();
    });

    it('starts empty, with the assistant on and nothing planned', () => {
      click('tab-instructions');

      expect(element.textContent).toContain('No instructions yet');
      expect(byId<HTMLInputElement>('assistant-fatigue').checked).toBe(true);
      expect(byId('substitution-count').textContent).toContain('0 of 5');
      expect(saveButton().disabled).toBe(true);
    });

    it('adds a blank rule straight away, with no list of templates to pick from', async () => {
      await openInstructionsAndAdd();

      expect(maybe('template-menu')).toBeNull();
      expect(byId('instruction-count').textContent).toContain('1 of 10');
      expect(byId('toggle-1').textContent).toContain('New instruction');
      expect(byId('editor-1')).toBeTruthy();
    });

    it('summarises a rule in words and offers to save it', async () => {
      await openInstructionsAndAdd();
      fillChase();

      expect(summary(1)).toBe(
        "From 60', if losing by 1 or more → one step more attacking, a fresh forward on for the most tired midfielder",
      );
      expect(byId('instruction-count').textContent).toContain('1 of 10');
      expect(byId('substitution-count').textContent).toContain('1 of 5');
      expect(byId('tab-instructions').textContent).toContain('(1)');
      expect(saveButton().disabled).toBe(false);
      expect(byId('editor-1')).toBeTruthy();
    });

    it('edits a rule in place and the summary follows', async () => {
      await openInstructionsAndAdd();
      fillChase();

      type('rule-from', '70');
      choose('rule-score', 'level');
      choose('rule-formation', '4-3-3');
      choose('rule-pressing', 'high_press');
      choose('rule-mentality', 'set:attacking');

      expect(summary(1)).toBe(
        "From 70', if level → 4-3-3, All-out attack, high press, a fresh forward on for the most tired midfielder",
      );
    });

    it('clears the family and threshold when the kind of event changes', async () => {
      await openInstructionsAndAdd();

      choose('rule-event', 'playing_badly');
      choose('rule-event-family', 'attack');
      type('rule-threshold', '5.5');

      expect(summary(1)).toBe('If a forward is rated below 5.5 → changes nothing');

      choose('rule-event', 'own_sent_off');

      expect(summary(1)).toBe('If one of your players is sent off → changes nothing');
      expect(maybe('rule-threshold')).toBeNull();
    });

    it('names a substitute in the summary and flags a substitution still missing a player', async () => {
      await openInstructionsAndAdd();
      click('add-substitution');

      expect(element.textContent).toContain('A substitution is missing a player');

      choose('sub-off-player', 's10');
      choose('sub-on-player', 'b2');

      expect(summary(1)).toBe('From kick-off → Fresh Forward on for Starter 10');
      expect(element.textContent).not.toContain('A substitution is missing a player');
    });

    it('orders, duplicates and deletes rules', async () => {
      await openInstructionsAndAdd();
      fillChase();
      click('add-instruction');
      fillTenMen();
      click('up-2');

      expect(summary(1)).toContain('sent off');
      expect(summary(2)).toContain('losing');

      click('duplicate-1');

      expect(
        byId('instruction-list').querySelectorAll('li[data-testid^="instruction-"]'),
      ).toHaveLength(3);
      expect(byId('instruction-2').textContent).toContain('Down to ten men (copy)');

      click('delete-2');

      expect(
        byId('instruction-list').querySelectorAll('li[data-testid^="instruction-"]'),
      ).toHaveLength(2);
      expect(byId('up-1').hasAttribute('disabled')).toBe(true);
      expect(byId('down-2').hasAttribute('disabled')).toBe(true);
    });

    it('turns a rule off without losing it', async () => {
      await openInstructionsAndAdd();
      fillChase();

      byId<HTMLInputElement>('enabled-1').click();
      fixture.detectChanges();

      expect(byId('toggle-1').textContent).toContain('(off)');
      expect(byId('substitution-count').textContent).toContain('0 of 5');
    });

    it('stops adding at ten rules', async () => {
      click('tab-instructions');

      for (let count = 0; count < MAX_MATCH_INSTRUCTIONS; count++) {
        click('add-instruction');
      }

      expect(byId('instruction-count').textContent).toContain('10 of 10');
      expect(byId<HTMLButtonElement>('add-instruction').disabled).toBe(true);
    });

    it('warns when the rules between them plan more than five substitutions', async () => {
      click('tab-instructions');

      for (let count = 0; count < 3; count++) {
        click('add-instruction');
        fillChase();
      }

      expect(byId('substitution-count').textContent).toContain('3 of 5');

      for (let count = 0; count < 2; count++) {
        click('add-substitution');
      }

      expect(byId('substitution-count').textContent).toContain('5 of 5');
      expect(byId('substitution-count').classList.contains('text-amber-200')).toBe(false);

      click('toggle-2');
      click('add-substitution');

      expect(byId('substitution-count').textContent).toContain('6 of 5');

      expect(byId('substitution-count').classList.contains('text-amber-200')).toBe(true);
      expect(element.textContent).toContain('plan more than five substitutions');
    });

    it('turns the assistant off and sends the choice with the plan', async () => {
      click('tab-instructions');

      byId<HTMLInputElement>('assistant-fatigue').click();
      fixture.detectChanges();

      expect(saveButton().disabled).toBe(false);

      saveButton().click();

      expect(api.update).toHaveBeenCalledTimes(1);
      expect(api.update.mock.calls[0][1].assistantFatigueSubstitutions).toBe(false);
      expect(api.update.mock.calls[0][1].matchInstructions).toBeNull();
    });

    it('saves the rules with the plan', async () => {
      await openInstructionsAndAdd();
      fillTenMen();

      saveButton().click();
      fixture.detectChanges();

      const request = api.update.mock.calls[0][1] as SaveTacticalPlanRequest;

      expect(request.matchInstructions).toHaveLength(1);
      expect(request.matchInstructions?.[0]).toMatchObject({
        name: 'Down to ten men',
        event: 'own_sent_off',
        changes: { mentalitySteps: -1 },
      });
      expect(summary(1)).toContain('sent off');
    });
  });

  describe('a new shape', () => {
    beforeEach(async () => {
      await create();
      await openInstructionsAndAdd();
      fillPlannedChange();
    });

    it('draws the eleven in the new formation, each in the slot the fit gave them', () => {
      expect(byId<HTMLSelectElement>('rule-formation').value).toBe('4-3-3');

      const pitch = byId('shape-pitch');
      const labels = [...pitch.querySelectorAll('button')].map((button) =>
        button.getAttribute('aria-label'),
      );

      expect(labels).toHaveLength(11);
      expect(labels[0]).toContain('Starter 1');
      expect(labels[9]).toContain('Starter 10');
      expect(byId<HTMLButtonElement>('shape-slot-1').disabled).toBe(true);
    });

    it('places a player in the slot the manager picks, marks it, and resets to the best fit', () => {
      click('shape-slot-10');

      expect(byId('shape-slot-10').getAttribute('aria-pressed')).toBe('true');
      expect(element.textContent).toContain('Slot 10 is selected');

      click('place-s3');

      expect(byId('shape-slot-10').getAttribute('aria-label')).toContain('Starter 3');
      expect(byId('shape-slot-10').getAttribute('aria-label')).toContain('placed by you');
      expect(byId('toggle-1').closest('li')?.textContent).toContain('4-3-3');

      click('reset-placements');

      expect(maybe('reset-placements')).toBeNull();
      expect(byId('shape-slot-10').getAttribute('aria-label')).not.toContain('placed by you');
    });

    it('never offers the goalkeeper for a slot, nor lets the goalkeeper slot be picked', () => {
      click('shape-slot-10');

      expect(maybe('place-s1')).toBeNull();
      expect(maybe('place-s2')).toBeTruthy();

      click('shape-slot-1');

      expect(byId('shape-slot-10').getAttribute('aria-pressed')).toBe('true');
    });

    it('sends the placement with the rule', () => {
      click('shape-slot-10');
      click('place-s3');

      saveButton().click();

      const request = api.update.mock.calls[0][1] as SaveTacticalPlanRequest;

      expect(request.matchInstructions?.[0].formation).toBe('4-3-3');
      expect(request.matchInstructions?.[0].overrides).toEqual([
        { playerId: 's3', slotNumber: 10 },
      ]);
    });

    it('drops the shape and its placements when the rule goes back to keeping the shape', () => {
      click('shape-slot-10');
      click('place-s3');
      choose('rule-formation', '');

      expect(maybe('shape-pitch')).toBeNull();
      expect(byId('summary-1').textContent).not.toContain('4-3-3');
    });

    it('keeps a placement when the rule moves to another formation that still has the slot', () => {
      click('shape-slot-10');
      click('place-s3');
      choose('rule-formation', '4-4-2');

      expect(byId('shape-slot-10').getAttribute('aria-label')).toContain('Starter 3');
    });

    it('marks a player placed in a slot he is not at home in', () => {
      click('shape-slot-10');
      click('place-s2');

      expect(byId('shape-slot-10').getAttribute('aria-label')).toContain('out of position');
      expect(maybe('misfit-legend')).toBeTruthy();

      click('place-s11');

      expect(byId('shape-slot-10').getAttribute('aria-label')).not.toContain('out of position');
      expect(maybe('misfit-legend')).toBeNull();
    });

    it('fits every outfield player naturally on request, keeping a placement the manager made', () => {
      click('shape-slot-10');
      click('place-s3');
      click('natural-fit');

      expect(byId('shape-slot-10').getAttribute('aria-label')).toContain('Starter 3');

      saveButton().click();

      const overrides = (api.update.mock.calls[0][1] as SaveTacticalPlanRequest)
        .matchInstructions?.[0].overrides;

      expect(overrides).toHaveLength(10);
      expect(overrides).toContainEqual({ playerId: 's3', slotNumber: 10 });
    });

    describe('with a substitute the rule brings on', () => {
      beforeEach(() => {
        click('add-substitution');
        choose('sub-off-player', 's10');
        choose('sub-on-player', 'b2');
      });

      it('puts the substitute in the shape in the seat of the man he replaces, and offers him for a slot', () => {
        const pitchText = byId('shape-pitch').textContent;

        expect(pitchText).toContain('FFW');
        expect(pitchText).not.toContain('STA10');

        click('shape-slot-9');

        expect(maybe('place-b2')).toBeTruthy();
        expect(maybe('place-s10')).toBeNull();
      });

      it('places him where the manager says and sends it, with no problem raised', () => {
        click('shape-slot-9');
        click('place-b2');

        expect(byId('shape-slot-9').getAttribute('aria-label')).toContain('Fresh Forward');
        expect(byId('shape-slot-9').getAttribute('aria-label')).toContain('placed by you');
        expect(element.querySelector('[role="alert"]')).toBeNull();

        saveButton().click();

        expect(
          (api.update.mock.calls[0][1] as SaveTacticalPlanRequest).matchInstructions?.[0].overrides,
        ).toEqual([{ playerId: 'b2', slotNumber: 9 }]);
      });

      it('drops his placement when the rule stops bringing him on', () => {
        click('shape-slot-9');
        click('place-b2');
        choose('sub-on-player', '');

        expect(element.querySelector('[role="alert"]')?.textContent ?? '').not.toContain(
          'cannot be placed',
        );
        expect(maybe('reset-placements')).toBeNull();
      });

      it('says where he cannot be placed when the man to take off is chosen by tiredness', () => {
        expect(maybe('incoming-note')).toBeNull();

        choose('sub-off', 'most_tired_in_family');

        expect(maybe('incoming-note')).toBeTruthy();
      });

      it('warns when a player is placed whom the rule takes off', () => {
        click('shape-slot-9');
        click('place-s5');
        choose('sub-off-player', 's5');

        expect(byId('rule-warning').textContent).toContain('takes him off');
      });
    });
  });
});
