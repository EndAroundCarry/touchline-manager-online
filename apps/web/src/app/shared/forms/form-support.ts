import { AbstractControl } from '@angular/forms';

/**
 * Resolves the message to show beneath one field.
 *
 * Client-side rules win while the manager is typing, because they are immediate and specific. Server
 * messages take over afterwards: the server is the authority on what is actually acceptable, and its
 * per-field `errors` map is keyed by the request property name (for example `DisplayName`), not by the
 * form control name.
 */
export function controlError(
  control: AbstractControl | null,
  serverErrors: ReadonlyMap<string, string[]>,
  serverField: string,
): string | null {
  if (control !== null && (control.touched || control.dirty) && control.invalid) {
    if (control.hasError('required') || control.hasError('mustBeTrue')) {
      return control.hasError('mustBeTrue')
        ? 'The terms of service and privacy policy must be accepted.'
        : 'This field is required.';
    }

    if (control.hasError('email')) {
      return 'Enter a valid email address.';
    }

    if (control.hasError('minlength')) {
      return 'Too short.';
    }

    if (control.hasError('maxlength')) {
      return 'Too long.';
    }

    if (control.hasError('pattern')) {
      return 'Use letters, digits, spaces, apostrophes, hyphens and underscores only.';
    }

    return 'Check this value.';
  }

  const messages = serverErrors.get(serverField);

  return messages !== undefined && messages.length > 0 ? messages[0] : null;
}

/** A stable element id for a field's error message, so it can be referenced by `aria-describedby`. */
export function errorId(controlName: string): string {
  return `${controlName}-error`;
}
