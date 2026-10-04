/**
 * The shared control styling.
 *
 * Exported as constants rather than repeated as class literals so every form field looks and behaves
 * the same, and so a change to the input treatment is one edit rather than a hunt. Tailwind utilities
 * rather than component styles: the utilities load last in the documented layer order, so a page can
 * still override a control without `!important`.
 */

/**
 * The minimum size of a touch target, as a Tailwind height. Forty-four CSS pixels is the point a
 * thumb can hit reliably; every actionable control is built to at least this tall (master plan
 * §11.3). It is applied unconditionally rather than behind `pointer: coarse` so a desktop and a
 * phone render the same control the same way, and so the breakpoint suite can measure it.
 */
const TOUCH_TARGET = 'min-h-11';

/** A labelled text input. */
export const TEXT_INPUT = `${TOUCH_TARGET} mt-1 block w-full rounded border border-line-strong bg-panel px-3 py-2 text-base text-ink shadow-sm focus:border-accent focus:outline-none disabled:bg-raised disabled:text-muted`;

/** A labelled select, matching the text input treatment. */
export const SELECT_INPUT = `${TOUCH_TARGET} mt-1 block w-full rounded border border-line-strong bg-panel px-3 py-2 text-base text-ink shadow-sm focus:border-accent focus:outline-none disabled:bg-raised disabled:text-muted`;

/** A checkbox. The box itself stays small; the row that wraps it is the touch target. */
export const CHECKBOX_INPUT =
  'mt-0.5 h-5 w-5 rounded border-line-strong accent-[var(--color-accent)] focus:ring-accent';

/**
 * The label row around a checkbox, sized so the whole row is tappable rather than the 20px box
 * alone. Pair it with {@link CHECKBOX_INPUT}.
 */
export const CHECKBOX_ROW = `${TOUCH_TARGET} flex items-center gap-2`;

/** The primary submit action on a form. */
export const PRIMARY_BUTTON = `${TOUCH_TARGET} inline-flex items-center justify-center gap-2 rounded bg-accent px-4 py-2 text-sm font-semibold text-on-accent hover:bg-accent-strong disabled:cursor-not-allowed disabled:opacity-60`;

/** A secondary action, such as signing out. */
export const SECONDARY_BUTTON = `${TOUCH_TARGET} inline-flex items-center justify-center gap-2 rounded border border-line-strong bg-transparent px-4 py-2 text-sm font-semibold text-ink hover:bg-raised disabled:cursor-not-allowed disabled:opacity-60`;

/** A destructive action, such as closing an account. */
export const DESTRUCTIVE_BUTTON = `${TOUCH_TARGET} inline-flex items-center justify-center gap-2 rounded border border-red-500/60 bg-transparent px-4 py-2 text-sm font-semibold text-red-300 hover:bg-red-500/10 disabled:cursor-not-allowed disabled:opacity-60`;

/** An inline text link. */
export const LINK = 'font-medium text-accent underline underline-offset-2 hover:text-accent-strong';

/**
 * A link used as a row or card action rather than inside prose, so it needs the same touch target as
 * a button. {@link LINK} stays bare for links within a sentence.
 */
export const LINK_ACTION = `${TOUCH_TARGET} inline-flex items-center font-medium text-accent underline underline-offset-2 hover:text-accent-strong`;

/** A form-level error, announced to assistive technology. */
export const FORM_ERROR =
  'mt-4 rounded border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm font-medium text-red-200';

/** A success or status message, announced to assistive technology. */
export const STATUS_MESSAGE =
  'mt-4 rounded border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-sm font-medium text-emerald-200';

/** A field-level error. */
export const FIELD_ERROR = 'mt-1 text-sm text-red-300';

/** The card that holds a public form. */
export const FORM_CARD = 'w-full max-w-md rounded border border-line bg-panel p-6 shadow-sm';

/** A standard page heading. */
export const PAGE_HEADING = 'font-display text-3xl font-semibold tracking-tight';
