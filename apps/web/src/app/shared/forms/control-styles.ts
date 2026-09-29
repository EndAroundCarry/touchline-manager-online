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
export const TEXT_INPUT = `${TOUCH_TARGET} mt-1 block w-full rounded border border-slate-300 bg-white px-3 py-2 text-base text-slate-900 shadow-sm focus:border-slate-500 focus:outline-none disabled:bg-slate-100 disabled:text-slate-500`;

/** A labelled select, matching the text input treatment. */
export const SELECT_INPUT = `${TOUCH_TARGET} mt-1 block w-full rounded border border-slate-300 bg-white px-3 py-2 text-base text-slate-900 shadow-sm focus:border-slate-500 focus:outline-none disabled:bg-slate-100 disabled:text-slate-500`;

/** A checkbox. The box itself stays small; the row that wraps it is the touch target. */
export const CHECKBOX_INPUT =
  'mt-0.5 h-5 w-5 rounded border-slate-300 text-slate-900 focus:ring-slate-500';

/**
 * The label row around a checkbox, sized so the whole row is tappable rather than the 20px box
 * alone. Pair it with {@link CHECKBOX_INPUT}.
 */
export const CHECKBOX_ROW = `${TOUCH_TARGET} flex items-center gap-2`;

/** The primary submit action on a form. */
export const PRIMARY_BUTTON = `${TOUCH_TARGET} inline-flex items-center justify-center gap-2 rounded bg-slate-900 px-4 py-2 text-sm font-semibold text-white hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-60`;

/** A secondary action, such as signing out. */
export const SECONDARY_BUTTON = `${TOUCH_TARGET} inline-flex items-center justify-center gap-2 rounded border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-800 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-60`;

/** A destructive action, such as closing an account. */
export const DESTRUCTIVE_BUTTON = `${TOUCH_TARGET} inline-flex items-center justify-center gap-2 rounded border border-red-700 bg-white px-4 py-2 text-sm font-semibold text-red-700 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-60`;

/** An inline text link. */
export const LINK = 'font-medium text-slate-900 underline underline-offset-2 hover:text-slate-600';

/**
 * A link used as a row or card action rather than inside prose, so it needs the same touch target as
 * a button. {@link LINK} stays bare for links within a sentence.
 */
export const LINK_ACTION = `${TOUCH_TARGET} inline-flex items-center font-medium text-slate-900 underline underline-offset-2 hover:text-slate-600`;

/** A form-level error, announced to assistive technology. */
export const FORM_ERROR =
  'mt-4 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm font-medium text-red-800';

/** A success or status message, announced to assistive technology. */
export const STATUS_MESSAGE =
  'mt-4 rounded border border-emerald-300 bg-emerald-50 px-3 py-2 text-sm font-medium text-emerald-900';

/** A field-level error. */
export const FIELD_ERROR = 'mt-1 text-sm text-red-700';

/** The card that holds a public form. */
export const FORM_CARD = 'w-full max-w-md rounded border border-slate-200 bg-white p-6 shadow-sm';

/** A standard page heading. */
export const PAGE_HEADING = 'text-2xl font-bold tracking-tight';
