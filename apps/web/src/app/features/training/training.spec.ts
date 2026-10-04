import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { TrainingApi } from '../../core/training/training-api';
import { training } from '../../core/training/training-test-data';
import { Training } from './training';

/**
 * The training screen.
 *
 * What the screen must get right: the squad appears as a table and as cards, each with a labelled programme
 * select whose first option is the position default; the attributes a player's programme trains are tinted by
 * weight and say so in words as well as colour (ADR-0039); and choosing a programme goes to the store with
 * the empty value meaning "back to the position default".
 */
describe('Training screen', () => {
  let fixture: ComponentFixture<Training>;
  let root: HTMLElement;
  let api: {
    get: ReturnType<typeof vi.fn>;
    save: ReturnType<typeof vi.fn>;
    setProgramme: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    api = { get: vi.fn().mockReturnValue(of(training())), save: vi.fn(), setProgramme: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Training],
      providers: [
        provideRouter([]),
        { provide: TrainingApi, useValue: api },
        { provide: MaintenanceStore, useValue: { canMutate: signal(true) } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Training);
    await fixture.whenStable();

    root = fixture.nativeElement as HTMLElement;
  });

  const table = () => root.querySelector('[data-testid="training-table"]')!;
  const cards = () => root.querySelector('[data-testid="training-cards"]')!;

  it('renders the squad as a table and as cards, each player with a labelled programme select', () => {
    const tableSelect = table().querySelector<HTMLSelectElement>(
      'select[aria-label="Training programme for Alaric Alderwick"]',
    );
    const cardSelect = cards().querySelector<HTMLSelectElement>(
      'select[aria-label="Training programme for Alaric Alderwick"]',
    );

    expect(table().querySelectorAll('tbody tr')).toHaveLength(2);
    expect(cards().querySelectorAll(':scope > li')).toHaveLength(2);
    expect(tableSelect).not.toBeNull();
    expect(cardSelect).not.toBeNull();
  });

  it('offers the position default first, naming the programme it means, then the catalogue', () => {
    const select = table().querySelector<HTMLSelectElement>(
      'select[aria-label="Training programme for Alaric Alderwick"]',
    )!;
    const options = Array.from(select.options);

    expect(options.map((option) => option.value)).toEqual([
      '',
      'goalkeeper',
      'forward',
      'recovery',
    ]);
    expect(options[0].textContent?.trim()).toBe('Position default (Forward)');
  });

  it('selects the position default for a player with no override, and the override otherwise', () => {
    const selectFor = (name: string) =>
      table().querySelector<HTMLSelectElement>(
        `select[aria-label="Training programme for ${name}"]`,
      )!;

    expect(selectFor('Alaric Alderwick').value).toBe('');
    expect(selectFor('Bramwell Brambleby').value).toBe('recovery');
  });

  it('has a column for each of the twenty-eight attributes under four family headings', () => {
    const groups = Array.from(table().querySelectorAll('th[scope="colgroup"]')).map((th) =>
      th.textContent?.trim(),
    );

    expect(groups).toEqual(['Technical', 'Mental', 'Physical', 'Goalkeeping']);
    expect(table().querySelectorAll('tbody tr:first-child td[data-weight]')).toHaveLength(28);
    expect(table().querySelector('abbr[title="Finishing"]')?.textContent).toBe('Fin');
  });

  it('tints the trained attributes by weight, and leaves the rest untinted', () => {
    const firstRow = table().querySelector('tbody tr')!;
    const weights = (weight: string) =>
      Array.from(firstRow.querySelectorAll<HTMLElement>(`td[data-weight="${weight}"]`));

    expect(weights('3')).toHaveLength(1);
    expect(weights('3')[0].classList.contains('bg-sky-300')).toBe(true);
    expect(weights('2')[0].classList.contains('bg-sky-200')).toBe(true);
    expect(weights('1')[0].classList.contains('bg-sky-100')).toBe(true);
    expect(weights('0')).toHaveLength(25);
    expect(weights('0').some((cell) => /bg-sky/.test(cell.className))).toBe(false);

    // The goalkeeper is on recovery, which trains nothing.
    const secondRow = table().querySelectorAll('tbody tr')[1];

    expect(secondRow.querySelectorAll('td[data-weight="0"]')).toHaveLength(28);
  });

  it('says in words what the tint says: bold value, marker, and an in-training read-out', () => {
    const core = table().querySelector<HTMLElement>('tbody tr td[data-weight="3"]')!;

    expect(core.querySelector('.font-bold')?.textContent?.trim()).toBe('17');
    expect(core.textContent).toContain('•••');
    expect(core.querySelector('.sr-only')?.textContent).toBe('17, Strong, in training, core focus');
  });

  it('marks the same attributes on the cards', () => {
    const firstCard = cards().querySelector(':scope > li')!;

    expect(firstCard.querySelectorAll('li[data-weight="3"]')).toHaveLength(1);
    expect(firstCard.querySelectorAll('li[data-weight="0"]')).toHaveLength(25);
    expect(firstCard.querySelectorAll('section')).toHaveLength(4);
  });

  it('sends a chosen programme to the store, and the empty choice as a clear', () => {
    api.setProgramme.mockReturnValue(
      of({
        playerId: 'p1',
        programme: 'recovery',
        isDefaultProgramme: false,
        defaultProgramme: 'forward',
        version: 1,
        serverTime: '2026-09-25T00:00:00Z',
      }),
    );

    const select = table().querySelector<HTMLSelectElement>(
      'select[aria-label="Training programme for Alaric Alderwick"]',
    )!;

    select.value = 'recovery';
    select.dispatchEvent(new Event('change'));

    expect(api.setProgramme).toHaveBeenCalledWith('p1', 'recovery', undefined);

    const clearing = table().querySelector<HTMLSelectElement>(
      'select[aria-label="Training programme for Bramwell Brambleby"]',
    )!;

    api.setProgramme.mockReturnValue(
      of({
        playerId: 'p2',
        programme: 'goalkeeper',
        isDefaultProgramme: true,
        defaultProgramme: 'goalkeeper',
        version: 0,
        serverTime: '2026-09-25T00:00:00Z',
      }),
    );
    clearing.value = '';
    clearing.dispatchEvent(new Event('change'));

    expect(api.setProgramme).toHaveBeenLastCalledWith('p2', null, '"7"');
  });

  it('lists what each programme trains by weight, with the recovery note', () => {
    const list = root.querySelector('[data-testid="programme-list"]')!;

    expect(list.querySelectorAll(':scope > li')).toHaveLength(3);
    const forward = list.querySelectorAll(':scope > li')[1];

    expect(forward.querySelector('dt')?.textContent).toBe('Core:');
    expect(forward.querySelector('dd')?.textContent).toBe('Finishing');
    expect(list.textContent).toContain('Trains no attributes');
  });

  it('is the intensity alone that the plan form edits', () => {
    const labels = Array.from(root.querySelectorAll('label span')).map((span) =>
      span.textContent?.trim(),
    );

    expect(labels).toContain('Intensity');
    expect(labels).not.toContain('Team focus');
  });
});
