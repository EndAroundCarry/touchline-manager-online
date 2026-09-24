import { FormControl, Validators } from '@angular/forms';
import { controlError, errorId } from './form-support';

/**
 * Field messages have one ordering rule: the immediate client-side rule wins while the manager is
 * editing, and the server's per-field answer takes over afterwards. The tests pin that precedence,
 * because reversing it produces the worst possible wording — a stale server complaint about a value
 * the manager has already corrected.
 */

function touched<TControl extends FormControl>(control: TControl): TControl {
  control.markAsTouched();

  return control;
}

describe('controlError', () => {
  it('says nothing about an untouched field', () => {
    const control = new FormControl('', Validators.required);

    expect(controlError(control, new Map(), 'Email')).toBeNull();
  });

  it('reports a missing required value once the field has been visited', () => {
    const control = touched(new FormControl('', Validators.required));

    expect(controlError(control, new Map(), 'Email')).toBe('This field is required.');
  });

  it('explains an unaccepted consent checkbox in its own words', () => {
    const control = touched(new FormControl(false, Validators.requiredTrue));
    control.setErrors({ mustBeTrue: true });

    expect(controlError(control, new Map(), 'AcceptTerms')).toBe(
      'The terms of service and privacy policy must be accepted.',
    );
  });

  it('maps each client-side rule to a specific sentence', () => {
    expect(
      controlError(touched(new FormControl('nope', Validators.email)), new Map(), 'Email'),
    ).toBe('Enter a valid email address.');

    expect(
      controlError(touched(new FormControl('ab', Validators.minLength(5))), new Map(), 'Password'),
    ).toBe('Too short.');

    expect(
      controlError(
        touched(new FormControl('a'.repeat(9), Validators.maxLength(4))),
        new Map(),
        'Password',
      ),
    ).toBe('Too long.');

    expect(
      controlError(
        touched(new FormControl('!!', Validators.pattern(/^[a-z]+$/))),
        new Map(),
        'DisplayName',
      ),
    ).toBe('Use letters, digits, spaces, apostrophes, hyphens and underscores only.');
  });

  it('falls back to a generic sentence for an unrecognised rule', () => {
    const control = touched(new FormControl('x'));
    control.setErrors({ somethingNew: true });

    expect(controlError(control, new Map(), 'Email')).toBe('Check this value.');
  });

  it('lets the client rule win over a stale server message', () => {
    const control = touched(new FormControl('', Validators.required));
    const serverErrors = new Map([['Email', ['That address is already registered.']]]);

    expect(controlError(control, serverErrors, 'Email')).toBe('This field is required.');
  });

  it('shows the server message once the client has nothing to complain about', () => {
    // The server keys its errors by request property name, not by form control name.
    const control = touched(new FormControl('taken@example.com', Validators.email));
    const serverErrors = new Map([['Email', ['That address is already registered.']]]);

    expect(controlError(control, serverErrors, 'Email')).toBe(
      'That address is already registered.',
    );
  });

  it('shows only the first server message when several are returned', () => {
    const control = new FormControl('taken@example.com');
    const serverErrors = new Map([['Email', ['First.', 'Second.']]]);

    expect(controlError(control, serverErrors, 'Email')).toBe('First.');
  });

  it('tolerates a missing control', () => {
    expect(controlError(null, new Map([['Email', ['Server said so.']]]), 'Email')).toBe(
      'Server said so.',
    );
  });
});

describe('errorId', () => {
  it('builds a stable id for the described-by relationship', () => {
    expect(errorId('displayName')).toBe('displayName-error');
  });
});
