import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { KitColourPicker } from './kit-colour-picker';

@Component({
  imports: [KitColourPicker],
  template: `<app-kit-colour-picker
    #picker
    [(primary)]="primary"
    [(secondary)]="secondary"
    [disabled]="disabled()"
  />`,
})
class Host {
  readonly primary = signal('#1f4e79');
  readonly secondary = signal('#d6e4f0');
  readonly disabled = signal(false);
}

/**
 * The kit colour picker: two colours, each from the spectrum picker, a hex field or a shortcut swatch.
 *
 * What the manager must be able to rely on is that the model only ever holds whole, valid colours — a
 * half-typed hex code never reaches it — and that choosing the same colour twice is named rather than
 * silently accepted.
 */
describe('KitColourPicker', () => {
  let fixture: ComponentFixture<Host>;
  let root: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();

    fixture = TestBed.createComponent(Host);
    root = fixture.nativeElement as HTMLElement;

    await fixture.whenStable();
  });

  function input(id: string): HTMLInputElement {
    return root.querySelector<HTMLInputElement>(`#${id}`)!;
  }

  async function type(id: string, value: string): Promise<void> {
    const field = input(id);

    field.value = value;
    field.dispatchEvent(new Event('input'));

    await fixture.whenStable();
  }

  it('offers the full-spectrum picker and a hex field for each colour, starting on the current pair', () => {
    expect(input('kit-primary-spectrum').type).toBe('color');
    expect(input('kit-primary-spectrum').value).toBe('#1f4e79');
    expect(input('kit-secondary-spectrum').value).toBe('#d6e4f0');
    expect(input('kit-primary-hex').value).toBe('#1f4e79');
    expect(input('kit-secondary-hex').value).toBe('#d6e4f0');
  });

  it('takes a colour from the spectrum picker', async () => {
    await type('kit-primary-spectrum', '#c0392b');

    expect(fixture.componentInstance.primary()).toBe('#c0392b');
    expect(input('kit-primary-hex').value).toBe('#c0392b');
  });

  it('takes a typed colour once it is whole, with or without the hash and in any case', async () => {
    await type('kit-secondary-hex', 'FCD116');

    expect(fixture.componentInstance.secondary()).toBe('#fcd116');
  });

  it('keeps a half-typed colour out of the model and says what it needs', async () => {
    await type('kit-primary-hex', '#12');

    expect(fixture.componentInstance.primary()).toBe('#1f4e79');
    expect(input('kit-primary-hex').value).toBe('#12');
    expect(root.textContent).toContain('Use six digits');
  });

  it('puts a half-typed colour back to the colour in use when the field is left', async () => {
    await type('kit-primary-hex', '#12');

    input('kit-primary-hex').dispatchEvent(new Event('blur'));
    await fixture.whenStable();

    expect(input('kit-primary-hex').value).toBe('#1f4e79');
    expect(root.textContent).not.toContain('Use six digits');
  });

  it('takes a colour from a shortcut swatch and marks it as the one in use', async () => {
    const swatch = root.querySelector<HTMLButtonElement>('button[aria-label="Red"]')!;

    swatch.click();
    await fixture.whenStable();

    expect(fixture.componentInstance.primary()).toBe('#d62828');
    expect(swatch.getAttribute('aria-pressed')).toBe('true');
  });

  it('names choosing the same colour twice, and is not valid then', async () => {
    const picker = fixture.debugElement.children[0].componentInstance as KitColourPicker;

    expect(picker.valid()).toBe(true);
    expect(root.querySelector('[data-testid="same-colour"]')).toBeNull();

    await type('kit-secondary-hex', '#1f4e79');

    expect(picker.valid()).toBe(false);
    expect(root.querySelector('[data-testid="same-colour"]')).not.toBeNull();
  });

  it('draws the shirt in the first colour with the second on the sleeves and collar', () => {
    const preview = root.querySelector('[data-testid="kit-preview"] svg')!;

    expect(preview.getAttribute('aria-label')).toContain('#1f4e79');
    expect(preview.getAttribute('aria-label')).toContain('#d6e4f0');
    expect(preview.querySelectorAll('[fill="#1f4e79"]')).toHaveLength(1);
    expect(preview.querySelectorAll('[fill="#d6e4f0"]')).toHaveLength(2);
  });

  it('cannot be edited while disabled', async () => {
    fixture.componentInstance.disabled.set(true);
    await fixture.whenStable();

    expect(root.querySelector('fieldset')!.disabled).toBe(true);
  });
});
