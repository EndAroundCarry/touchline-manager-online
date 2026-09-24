/**
 * The shared control styling.
 *
 * Exported as constants rather than repeated as class literals so every form field looks and behaves
 * the same, and so a change to the input treatment is one edit rather than a hunt. Tailwind utilities
 * rather than component styles: the utilities load last in the documented layer order, so a page can
 * still override a control without `!important`.
 */

/** A labelled text input. */
export const TEXT_INPUT =
  'mt-1 block w-full rounded border border-slate-300 bg-white px-3 py-2 text-base text-slate-900 shadow-sm focus:border-slate-500 focus:outline-none disabled:bg-slate-100 disabled:text-slate-500';

/** A checkbox with its inline label. */
export const CHECKBOX_INPUT =
  'mt-0.5 h-4 w-4 rounded border-slate-300 text-slate-900 focus:ring-slate-500';

/** The primary submit action on a form. */
export const PRIMARY_BUTTON =
  'inline-flex items-center justify-center gap-2 rounded bg-slate-900 px-4 py-2 text-sm font-semibold text-white hover:bg-slate-700 disabled:cursor-not-allowed disabled:opacity-60';

/** A secondary action, such as signing out. */
export const SECONDARY_BUTTON =
  'inline-flex items-center justify-center gap-2 rounded border border-slate-300 bg-white px-4 py-2 text-sm font-semibold text-slate-800 hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-60';

/** A destructive action, such as closing an account. */
export const DESTRUCTIVE_BUTTON =
  'inline-flex items-center justify-center gap-2 rounded border border-red-700 bg-white px-4 py-2 text-sm font-semibold text-red-700 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-60';

/** An inline text link. */
export const LINK = 'font-medium text-slate-900 underline underline-offset-2 hover:text-slate-600';

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
