import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CREST_PATTERNS, CREST_SHAPES } from '../club-crest/crest-designs';
import { CrestPicker } from './crest-picker';

@Component({
  imports: [CrestPicker],
  template: `<app-crest-picker
    [(shape)]="shape"
    [(pattern)]="pattern"
    [primary]="primary()"
    [secondary]="secondary()"
    [disabled]="disabled()"
  />`,
})
class Host {
  readonly shape = signal('shield');
  readonly pattern = signal('stripes');
  readonly primary = signal('#c0392b');
  readonly secondary = signal('#fcd116');
  readonly disabled = signal(false);
}

/**
 * The crest picker: a shape from one dropdown and the stripes from another.
 *
 * The manager must see what is chosen in each, be able to change one without the other (any shape can wear
 * any stripes), and see the preview follow both the choice and the colours above it.
 */
describe('CrestPicker', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;
  let root: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();

    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    root = fixture.nativeElement as HTMLElement;

    await fixture.whenStable();
  });

  function select(label: string): HTMLSelectElement {
    const field = Array.from(root.querySelectorAll('label')).find(
      (candidate) => candidate.textContent?.trim() === label,
    )!;

    return root.querySelector<HTMLSelectElement>(`#${field.htmlFor}`)!;
  }

  async function choose(label: string, code: string): Promise<void> {
    const dropdown = select(label);

    dropdown.value = code;
    dropdown.dispatchEvent(new Event('change'));

    await fixture.whenStable();
  }

  function optionLabels(label: string): string[] {
    return Array.from(select(label).options).map((option) => option.textContent!.trim());
  }

  it('offers every shape in one dropdown and every kind of stripes in another', () => {
    expect(CREST_SHAPES).toHaveLength(20);
    expect(optionLabels('Crest shape')).toEqual(CREST_SHAPES.map((shape) => shape.label));
    expect(optionLabels('Stripes')).toEqual(CREST_PATTERNS.map((pattern) => pattern.label));
  });

  it('shows the current shape and stripes as the ones chosen', () => {
    expect(select('Crest shape').value).toBe('shield');
    expect(select('Stripes').value).toBe('stripes');
  });

  it('changes the shape and leaves the stripes alone', async () => {
    await choose('Crest shape', 'hexagon');

    expect(host.shape()).toBe('hexagon');
    expect(host.pattern()).toBe('stripes');
  });

  it('changes the stripes and leaves the shape alone', async () => {
    await choose('Stripes', 'saltire');

    expect(host.pattern()).toBe('saltire');
    expect(host.shape()).toBe('shield');
  });

  it('lets any shape wear any stripes', async () => {
    for (const shape of CREST_SHAPES) {
      for (const pattern of CREST_PATTERNS) {
        host.shape.set(shape.code);
        host.pattern.set(pattern.code);
        await fixture.whenStable();

        expect(select('Crest shape').value).toBe(shape.code);
        expect(select('Stripes').value).toBe(pattern.code);
        expect(root.querySelector('[data-testid="crest-preview"] svg')).not.toBeNull();
      }
    }
  });

  it('draws the preview in the club’s own colours and follows a change of them', async () => {
    const field = (): string | null =>
      root.querySelector('[data-testid="crest-preview"] svg > g > rect')!.getAttribute('fill');

    expect(field()).toBe('#c0392b');

    host.primary.set('#12284c');
    await fixture.whenStable();

    expect(field()).toBe('#12284c');
  });

  it('follows the choice in the preview', async () => {
    const outline = (): string =>
      root.querySelector('[data-testid="crest-preview"] clipPath path')!.getAttribute('d')!;
    const before = outline();

    await choose('Crest shape', 'round');

    expect(outline()).not.toBe(before);
  });

  it('cannot be changed while a save is under way', async () => {
    host.disabled.set(true);
    await fixture.whenStable();

    expect(root.querySelector('fieldset')!.disabled).toBe(true);
    expect(select('Crest shape').matches(':disabled')).toBe(true);
    expect(select('Stripes').matches(':disabled')).toBe(true);
  });
});
