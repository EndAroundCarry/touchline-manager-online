/**
 * Client-side presentation helpers for the training module.
 *
 * Small and pure, so they can be unit tested without a component. The server already sends the *codes* a
 * manager may choose — the enumerations live in one place (`TrainingMapping`) — so these functions only turn
 * those codes into words, and fall back to the raw code rather than showing nothing when a new one appears.
 */

/** The value of the "no individual focus" choice, which returns a player to the club's team plan (`TRN-2`). */
export const TEAM_PLAN = '';

/** A named choice for a select, in the order the server sent the options. */
export interface TrainingOption {
  /** The stable code the server expects, or an empty string for "the team plan". */
  readonly value: string;

  /** The label shown in the select. */
  readonly label: string;
}

const TEAM_FOCUS_LABELS: Record<string, string> = {
  balanced: 'Balanced',
  recovery: 'Recovery',
  fitness: 'Fitness',
  attacking: 'Attacking',
  defending: 'Defending',
  technical: 'Technical',
  tactical: 'Tactical',
};

const INTENSITY_LABELS: Record<string, string> = {
  light: 'Light',
  normal: 'Normal',
  intense: 'Intense',
};

const FOCUS_FAMILY_LABELS: Record<string, string> = {
  technical: 'Technical',
  mental: 'Mental',
  physical: 'Physical',
  goalkeeping: 'Goalkeeping',
};

/** Names a club-wide training focus (`TRN-1`), falling back to the code. */
export function teamFocusLabel(code: string): string {
  return TEAM_FOCUS_LABELS[code] ?? code;
}

/** Names an intensity, falling back to the code. */
export function intensityLabel(code: string): string {
  return INTENSITY_LABELS[code] ?? code;
}

/** Names an attribute family a player may focus on (`TRN-2`), falling back to the code. */
export function focusFamilyLabel(code: string): string {
  return FOCUS_FAMILY_LABELS[code] ?? code;
}

/** Turns the server's focus codes into labelled options (`TRN-1`). */
export function teamFocusOptions(codes: readonly string[]): readonly TrainingOption[] {
  return codes.map((code) => ({ value: code, label: teamFocusLabel(code) }));
}

/** Turns the server's intensity codes into labelled options. */
export function intensityOptions(codes: readonly string[]): readonly TrainingOption[] {
  return codes.map((code) => ({ value: code, label: intensityLabel(code) }));
}

/**
 * Turns the server's attribute-family codes into labelled options, with the team plan first.
 *
 * The empty option is the clear: choosing it sends a null family and deletes the focus, which returns the
 * player to the club's plan alone (§10.4).
 */
export function focusOptions(families: readonly string[]): readonly TrainingOption[] {
  return [
    { value: TEAM_PLAN, label: 'Team plan' },
    ...families.map((code) => ({ value: code, label: focusFamilyLabel(code) })),
  ];
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
