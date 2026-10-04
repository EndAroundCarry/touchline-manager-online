import { attributes, PROGRAMMES, player } from './training-test-data';
import {
  ATTRIBUTE_COLUMNS,
  ATTRIBUTE_FAMILY_COLUMNS,
  POSITION_DEFAULT,
  WEIGHT_STYLES,
  attributeLabel,
  defaultOptionLabel,
  effectiveDateLabel,
  intensityLabel,
  intensityOptions,
  programmeChoices,
  programmeLabel,
  programmeWeightGroups,
  trainedAttributes,
  trainingRows,
  weightStyle,
} from './training-presentation';

/**
 * The training presentation helpers.
 *
 * These are what turn the server's catalogue and a squad's attributes into the words, rows, and cells a
 * manager reads. The behaviours that matter: the server's codes all have labels and an unknown one falls back
 * to itself; the position default is the empty option that sends a null programme (`TRN-2`); and a player's
 * attributes are marked by what *their* programme trains, with the weight carried by a mark and a read-out as
 * well as by colour (ADR-0039).
 */
describe('training presentation', () => {
  describe('labels and options', () => {
    it('names every intensity the server can send, and falls back to the code', () => {
      expect(intensityLabel('light')).toBe('Light');
      expect(intensityLabel('intense')).toBe('Intense');
      expect(intensityLabel('brutal')).toBe('brutal');

      expect(
        intensityOptions(['light', 'normal', 'intense']).map((option) => option.label),
      ).toEqual(['Light', 'Normal', 'Intense']);
    });

    it('names programmes from the catalogue, in the order the server sent them', () => {
      expect(programmeLabel(PROGRAMMES, 'forward')).toBe('Forward');
      expect(programmeLabel(PROGRAMMES, 'sweeper')).toBe('sweeper');
      expect(programmeChoices(PROGRAMMES)).toEqual([
        { value: 'goalkeeper', label: 'Goalkeeper' },
        { value: 'forward', label: 'Forward' },
        { value: 'recovery', label: 'Recovery' },
      ]);
    });

    it('words the position default by the programme it falls back to', () => {
      expect(POSITION_DEFAULT).toBe('');
      expect(defaultOptionLabel(PROGRAMMES, 'forward')).toBe('Position default (Forward)');
    });

    it('renders the plan date in the viewer locale, or the raw value when it is not a date', () => {
      expect(effectiveDateLabel('2026-09-25', 'en-GB')).toBe('25 September 2026');
      expect(effectiveDateLabel('not-a-date', 'en-GB')).toBe('not-a-date');
    });
  });

  describe('attribute columns', () => {
    it('has the twenty-eight attributes, each with a unique code and a unique short heading', () => {
      expect(ATTRIBUTE_COLUMNS).toHaveLength(28);
      expect(new Set(ATTRIBUTE_COLUMNS.map((column) => column.key)).size).toBe(28);
      expect(new Set(ATTRIBUTE_COLUMNS.map((column) => column.abbr)).size).toBe(28);
    });

    it('groups them into the four families in table order, 10, 8, 6 and 4 wide', () => {
      expect(ATTRIBUTE_FAMILY_COLUMNS.map((family) => family.label)).toEqual([
        'Technical',
        'Mental',
        'Physical',
        'Goalkeeping',
      ]);
      expect(ATTRIBUTE_FAMILY_COLUMNS.map((family) => family.columns.length)).toEqual([
        10, 8, 6, 4,
      ]);
    });

    it('reads each column from its own place in the attribute set', () => {
      const set = attributes(1, {
        technical: { setPieces: 20 },
        mental: { workRate: 19 },
        physical: { jumpingReach: 18 },
        goalkeeping: { aerialAbility: 17 },
      });
      const read = (key: string) => ATTRIBUTE_COLUMNS.find((c) => c.key === key)!.value(set);

      expect(read('setPieces')).toBe(20);
      expect(read('workRate')).toBe(19);
      expect(read('jumpingReach')).toBe(18);
      expect(read('aerialAbility')).toBe(17);
      expect(read('finishing')).toBe(1);
    });

    it('names an attribute from its code, falling back to the code', () => {
      expect(attributeLabel('firstTouch')).toBe('First touch');
      expect(attributeLabel('oneOnOnes')).toBe('One-on-ones');
      expect(attributeLabel('flair')).toBe('flair');
    });
  });

  describe('programme weights', () => {
    it('has three weights, heaviest first, each darker than the next', () => {
      expect(WEIGHT_STYLES.map((style) => style.weight)).toEqual([3, 2, 1]);
      expect(WEIGHT_STYLES.map((style) => style.label)).toEqual([
        'Core',
        'Important',
        'Supporting',
      ]);
      expect(new Set(WEIGHT_STYLES.map((style) => style.tintClass)).size).toBe(3);
      expect(WEIGHT_STYLES.map((style) => style.marker.length)).toEqual([3, 2, 1]);
      expect(weightStyle(0)).toBeNull();
      expect(weightStyle(9)).toBeNull();
    });

    it('lists what a programme trains by weight, leaving out a weight that has none', () => {
      const forward = PROGRAMMES.find((programme) => programme.code === 'forward')!;

      expect(programmeWeightGroups(forward)).toEqual([
        { label: 'Core', attributes: ['Finishing'] },
        { label: 'Important', attributes: ['Composure'] },
        { label: 'Supporting', attributes: ['Pace'] },
      ]);
      expect(programmeWeightGroups(PROGRAMMES.find((p) => p.code === 'recovery')!)).toEqual([]);
    });

    it('maps a programme to its attribute weights, and recovery or an unknown code to nothing', () => {
      expect([...trainedAttributes(PROGRAMMES, 'forward')]).toEqual([
        ['finishing', 3],
        ['composure', 2],
        ['pace', 1],
      ]);
      expect(trainedAttributes(PROGRAMMES, 'recovery').size).toBe(0);
      expect(trainedAttributes(PROGRAMMES, 'sweeper').size).toBe(0);
    });
  });

  describe('trainingRows', () => {
    const [row] = trainingRows([player()], PROGRAMMES);
    const cell = (key: string) => row.cells.find((candidate) => candidate.key === key)!;

    it('has one cell per attribute, in column order, with the values read from the player', () => {
      expect(row.cells.map((c) => c.key)).toEqual(ATTRIBUTE_COLUMNS.map((c) => c.key));
      expect(cell('finishing').value).toBe(17);
      expect(row.groups.map((group) => group.cells.length)).toEqual([10, 8, 6, 4]);
    });

    it('marks exactly the attributes the programme trains, by weight', () => {
      expect(row.cells.filter((c) => c.weight > 0).map((c) => [c.key, c.weight])).toEqual([
        ['finishing', 3],
        ['composure', 2],
        ['pace', 1],
      ]);

      expect(cell('finishing').tintClass).toBe('bg-sky-500/45');
      expect(cell('composure').tintClass).toBe('bg-sky-500/30');
      expect(cell('pace').tintClass).toBe('bg-sky-500/15');
      expect(cell('passing').tintClass).toBe('');
      expect(cell('passing').marker).toBe('');
      expect(cell('finishing').marker).toBe('•••');
    });

    it('keeps the band as the text colour: strong, low and average, lighter on a tint', () => {
      expect(cell('finishing').textClass).toBe('text-emerald-200');
      expect(cell('composure').textClass).toBe('text-red-200');
      expect(cell('pace').textClass).toBe('text-amber-200');

      // Untrained cells keep the band colours the squad profile uses.
      expect(cell('passing').textClass).toBe('text-amber-300');
    });

    it('says in words what colour says: the value, its band, and the weight when trained', () => {
      expect(cell('finishing').readOut).toBe('17, Strong, in training, core focus');
      expect(cell('composure').readOut).toBe('5, Low, in training, important focus');
      expect(cell('passing').readOut).toBe('10, Average');
    });

    it('selects the position default as the empty value, and an override by its code', () => {
      expect(row.selected).toBe('');
      expect(row.programmeLabel).toBe('Forward');
      expect(row.defaultOptionLabel).toBe('Position default (Forward)');

      const [chosen] = trainingRows(
        [player({ programme: 'recovery', isDefaultProgramme: false, focusVersion: 2 })],
        PROGRAMMES,
      );

      expect(chosen.selected).toBe('recovery');
      expect(chosen.programmeLabel).toBe('Recovery');
      expect(chosen.cells.every((c) => c.weight === 0)).toBe(true);
    });
  });
});
