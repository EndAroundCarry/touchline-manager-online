/**
 * Client-side presentation helpers for the training module.
 *
 * Small and pure, so they can be unit tested without a component. The server owns the programme catalogue
 * (which attributes each programme trains, and how strongly) and sends it with the training read, so nothing
 * here repeats a weight: these functions only turn that catalogue and the squad's attributes into the rows,
 * cells, and words the screen draws, and fall back to the raw code rather than showing nothing when a new
 * one appears.
 */

import { attributeBand } from '../squad/squad-presentation';
import type { PerformanceBand } from '../squad/squad-presentation';
import type { PlayerAttributes } from '../squad/squad.models';
import type { TrainingPlayer, TrainingProgramme } from './training.models';

/** The value of the "no override" choice, which returns a player to the programme for their position (`TRN-2`). */
export const POSITION_DEFAULT = '';

/** A named choice for a select, in the order the server sent the options. */
export interface TrainingOption {
  /** The stable code the server expects, or an empty string for "the position default". */
  readonly value: string;

  /** The label shown in the select. */
  readonly label: string;
}

const INTENSITY_LABELS: Record<string, string> = {
  light: 'Light',
  normal: 'Normal',
  intense: 'Intense',
};

/** Names an intensity, falling back to the code. */
export function intensityLabel(code: string): string {
  return INTENSITY_LABELS[code] ?? code;
}

/** Turns the server's intensity codes into labelled options. */
export function intensityOptions(codes: readonly string[]): readonly TrainingOption[] {
  return codes.map((code) => ({ value: code, label: intensityLabel(code) }));
}

/** Words for an effective date, in the viewer's locale, from the server's ISO date. */
export function effectiveDateLabel(isoDate: string, locale: string): string {
  const date = new Date(`${isoDate}T00:00:00Z`);

  return Number.isNaN(date.getTime())
    ? isoDate
    : new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', year: 'numeric' }).format(
        date,
      );
}

// --- Programmes ---------------------------------------------------------------------------------------------

/** How much a programme cares about an attribute: 0 when it does not train it, then 1–3 as the server sends. */
export type ProgrammeWeight = 0 | 1 | 2 | 3;

/** What a weight is called, and how the screen marks it so colour is never the only signal (ADR-0039). */
export interface WeightStyle {
  /** The weight, 1–3. */
  readonly weight: 1 | 2 | 3;

  /** The word for it, in the legend, the programme list, and the read-out. */
  readonly label: string;

  /** The background tint: the darker, the more the programme cares. */
  readonly tintClass: string;

  /** A small mark that repeats the weight without colour. */
  readonly marker: string;
}

/**
 * The three weights, heaviest first.
 *
 * Tint is the background channel and the band colour is the text channel, so the two do not fight. The
 * tints are translucent sky blue over the dark panel and stop at 45% on purpose: the text on a tinted cell is
 * the 200 shade of its band colour ({@link TRAINED_TEXT_CLASSES}), which keeps at least 5.4:1 against the
 * strongest tint, where the 300 shade the table uses elsewhere would fall below 4.5:1.
 */
export const WEIGHT_STYLES: readonly WeightStyle[] = [
  { weight: 3, label: 'Core', tintClass: 'bg-sky-500/45', marker: '•••' },
  { weight: 2, label: 'Important', tintClass: 'bg-sky-500/30', marker: '••' },
  { weight: 1, label: 'Supporting', tintClass: 'bg-sky-500/15', marker: '•' },
];

/** The text colour of a trained cell: the band's own hue, one step lighter so it holds its contrast on a tint. */
const TRAINED_TEXT_CLASSES: Record<PerformanceBand, string> = {
  low: 'text-red-200',
  average: 'text-amber-200',
  strong: 'text-emerald-200',
};

/** Finds the style for a weight, or null for a weight of zero or one the client does not know. */
export function weightStyle(weight: number): WeightStyle | null {
  return WEIGHT_STYLES.find((style) => style.weight === weight) ?? null;
}

/** Names a programme from the catalogue, falling back to the code. */
export function programmeLabel(programmes: readonly TrainingProgramme[], code: string): string {
  return programmes.find((programme) => programme.code === code)?.label ?? code;
}

/** The programmes a manager may choose, in the order the server sent them. */
export function programmeChoices(
  programmes: readonly TrainingProgramme[],
): readonly TrainingOption[] {
  return programmes.map((programme) => ({ value: programme.code, label: programme.label }));
}

/** The label of the select's first option, which names the programme a player would fall back to. */
export function defaultOptionLabel(
  programmes: readonly TrainingProgramme[],
  defaultProgramme: string,
): string {
  return `Position default (${programmeLabel(programmes, defaultProgramme)})`;
}

/**
 * The attributes a programme trains and the weight of each, keyed by attribute code.
 *
 * An unknown programme, or recovery, trains nothing, so the map is empty rather than absent and a caller
 * never needs to special-case it.
 */
export function trainedAttributes(
  programmes: readonly TrainingProgramme[],
  code: string,
): ReadonlyMap<string, number> {
  const programme = programmes.find((candidate) => candidate.code === code);

  return new Map(
    (programme?.attributes ?? []).map((attribute) => [attribute.name, attribute.weight]),
  );
}

/** One weight's attributes within a programme, already named. */
export interface ProgrammeWeightGroup {
  /** The weight's word. */
  readonly label: string;

  /** The attribute names at that weight, in the catalogue's order. */
  readonly attributes: readonly string[];
}

/** A programme's attributes grouped by weight, heaviest first, leaving out a weight that has none. */
export function programmeWeightGroups(
  programme: TrainingProgramme,
): readonly ProgrammeWeightGroup[] {
  return WEIGHT_STYLES.map((style) => ({
    label: style.label,
    attributes: programme.attributes
      .filter((attribute) => attribute.weight === style.weight)
      .map((attribute) => attributeLabel(attribute.name)),
  })).filter((group) => group.attributes.length > 0);
}

// --- Attribute columns --------------------------------------------------------------------------------------

/** A family of attributes, by the key it has in {@link PlayerAttributes}. */
export type AttributeFamilyKey = keyof PlayerAttributes;

/** One of the twenty-eight attribute columns. */
export interface AttributeColumn {
  /** The attribute code, as the catalogue names it, e.g. `firstTouch`. */
  readonly key: string;

  /** The family the attribute belongs to. */
  readonly family: AttributeFamilyKey;

  /** The full name. */
  readonly label: string;

  /** The short heading, so twenty-eight columns fit a screen. */
  readonly abbr: string;

  /** Reads the attribute's value from a player's set. */
  readonly value: (attributes: PlayerAttributes) => number;
}

/** One family and its heading, in the order the table lays them out. */
export interface AttributeFamilyColumns {
  readonly key: AttributeFamilyKey;
  readonly label: string;
  readonly columns: readonly AttributeColumn[];
}

function column<F extends AttributeFamilyKey>(
  family: F,
  key: keyof PlayerAttributes[F] & string,
  label: string,
  abbr: string,
): AttributeColumn {
  return { key, family, label, abbr, value: (attributes) => attributes[family][key] as number };
}

/** The twenty-eight attributes, family by family, in the order the profile reads them. */
export const ATTRIBUTE_COLUMNS: readonly AttributeColumn[] = [
  column('technical', 'finishing', 'Finishing', 'Fin'),
  column('technical', 'passing', 'Passing', 'Pas'),
  column('technical', 'crossing', 'Crossing', 'Cro'),
  column('technical', 'dribbling', 'Dribbling', 'Dri'),
  column('technical', 'firstTouch', 'First touch', 'FT'),
  column('technical', 'tackling', 'Tackling', 'Tck'),
  column('technical', 'marking', 'Marking', 'Mrk'),
  column('technical', 'heading', 'Heading', 'Hdg'),
  column('technical', 'technique', 'Technique', 'Tec'),
  column('technical', 'setPieces', 'Set pieces', 'SP'),
  column('mental', 'decisions', 'Decisions', 'Dec'),
  column('mental', 'vision', 'Vision', 'Vis'),
  column('mental', 'positioning', 'Positioning', 'Pos'),
  column('mental', 'composure', 'Composure', 'Cmp'),
  column('mental', 'anticipation', 'Anticipation', 'Ant'),
  column('mental', 'workRate', 'Work rate', 'WR'),
  column('mental', 'aggression', 'Aggression', 'Agg'),
  column('mental', 'leadership', 'Leadership', 'Ldr'),
  column('physical', 'pace', 'Pace', 'Pac'),
  column('physical', 'acceleration', 'Acceleration', 'Acc'),
  column('physical', 'stamina', 'Stamina', 'Sta'),
  column('physical', 'strength', 'Strength', 'Str'),
  column('physical', 'agility', 'Agility', 'Agi'),
  column('physical', 'jumpingReach', 'Jumping reach', 'Jmp'),
  column('goalkeeping', 'handling', 'Handling', 'Han'),
  column('goalkeeping', 'reflexes', 'Reflexes', 'Ref'),
  column('goalkeeping', 'oneOnOnes', 'One-on-ones', '1v1'),
  column('goalkeeping', 'aerialAbility', 'Aerial ability', 'Aer'),
];

const FAMILY_LABELS: Record<AttributeFamilyKey, string> = {
  technical: 'Technical',
  mental: 'Mental',
  physical: 'Physical',
  goalkeeping: 'Goalkeeping',
};

/** The families in table order, each with its columns, for a spanning group header. */
export const ATTRIBUTE_FAMILY_COLUMNS: readonly AttributeFamilyColumns[] = (
  ['technical', 'mental', 'physical', 'goalkeeping'] as const
).map((key) => ({
  key,
  label: FAMILY_LABELS[key],
  columns: ATTRIBUTE_COLUMNS.filter((candidate) => candidate.family === key),
}));

/** Names an attribute from its code, falling back to the code. */
export function attributeLabel(key: string): string {
  return ATTRIBUTE_COLUMNS.find((candidate) => candidate.key === key)?.label ?? key;
}

// --- Rows ---------------------------------------------------------------------------------------------------

/** One attribute of one player, with everything the table and the card need to draw it. */
export interface AttributeCell {
  readonly key: string;
  readonly family: AttributeFamilyKey;
  readonly label: string;
  readonly abbr: string;
  readonly value: number;

  /** How much the player's programme trains it: 0 when it does not. */
  readonly weight: ProgrammeWeight;

  /** The text colour: the band's, or its darker shade on a tint. */
  readonly textClass: string;

  /** The background tint, or an empty string when the programme does not train the attribute. */
  readonly tintClass: string;

  /** A mark repeating the weight without colour, or an empty string. */
  readonly marker: string;

  /** What a screen reader hears: the value, its band, and, when trained, the weight. */
  readonly readOut: string;
}

/** One family's cells, for the cards and for the table's group header. */
export interface AttributeCellGroup {
  readonly key: AttributeFamilyKey;
  readonly label: string;
  readonly cells: readonly AttributeCell[];
}

/** One player as a row of the training table or a card. */
export interface TrainingRow {
  readonly player: TrainingPlayer;

  /** The programme's name. */
  readonly programmeLabel: string;

  /** The select's value: the override's code, or the empty string for the position default. */
  readonly selected: string;

  /** The label of the select's first option. */
  readonly defaultOptionLabel: string;

  /** The twenty-eight cells, in column order. */
  readonly cells: readonly AttributeCell[];

  /** The same cells by family. */
  readonly groups: readonly AttributeCellGroup[];
}

/** Builds one attribute cell, tinting it when the player's programme trains the attribute. */
function attributeCell(
  attributeColumn: AttributeColumn,
  attributes: PlayerAttributes,
  trained: ReadonlyMap<string, number>,
): AttributeCell {
  const value = attributeColumn.value(attributes);
  const band = attributeBand(value);
  const style = weightStyle(trained.get(attributeColumn.key) ?? 0);

  return {
    key: attributeColumn.key,
    family: attributeColumn.family,
    label: attributeColumn.label,
    abbr: attributeColumn.abbr,
    value,
    weight: style?.weight ?? 0,
    textClass: style === null ? band.className : TRAINED_TEXT_CLASSES[band.band],
    tintClass: style?.tintClass ?? '',
    marker: style?.marker ?? '',
    readOut:
      style === null
        ? `${value}, ${band.label}`
        : `${value}, ${band.label}, in training, ${style.label.toLowerCase()} focus`,
  };
}

/** Builds the training table's rows: each player's attributes marked by what their programme trains. */
export function trainingRows(
  players: readonly TrainingPlayer[],
  programmes: readonly TrainingProgramme[],
): readonly TrainingRow[] {
  return players.map((player) => {
    const trained = trainedAttributes(programmes, player.programme);
    const cells = ATTRIBUTE_COLUMNS.map((attributeColumn) =>
      attributeCell(attributeColumn, player.attributes, trained),
    );

    return {
      player,
      programmeLabel: programmeLabel(programmes, player.programme),
      selected: player.isDefaultProgramme ? POSITION_DEFAULT : player.programme,
      defaultOptionLabel: defaultOptionLabel(programmes, player.defaultProgramme),
      cells,
      groups: ATTRIBUTE_FAMILY_COLUMNS.map((family) => ({
        key: family.key,
        label: family.label,
        cells: cells.filter((cell) => cell.family === family.key),
      })),
    };
  });
}
