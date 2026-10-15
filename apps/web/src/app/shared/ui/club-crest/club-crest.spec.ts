import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ClubCrest } from './club-crest';

@Component({
  imports: [ClubCrest],
  template: `
    <app-club-crest
      id="first"
      [shape]="shape()"
      [pattern]="pattern()"
      [primary]="primary()"
      [secondary]="secondary()"
      [label]="label()"
    />
    <app-club-crest
      id="second"
      shape="round"
      pattern="star"
      primary="#12284c"
      secondary="#fcd116"
    />
  `,
})
class Host {
  readonly shape = signal('shield');
  readonly pattern = signal('stripes');
  readonly primary = signal('#c0392b');
  readonly secondary = signal('#fcd116');
  readonly label = signal('');
}

/**
 * The crest drawing: a shape wearing a kind of stripes, in the club's two colours.
 *
 * What matters is that the colours really are the club's, that the shape and the stripes change on their own
 * (any shape can wear any stripes), that a crest can be told to assistive technology only when it stands
 * alone, and that two crests on one page do not borrow each other's clip.
 */
describe('ClubCrest', () => {
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

  function crest(id: string): SVGSVGElement {
    return root.querySelector<SVGSVGElement>(`#${id} svg`)!;
  }

  function outline(): string {
    return crest('first').querySelector('clipPath path')!.getAttribute('d')!;
  }

  function laidOn(): string[] {
    return Array.from(crest('first').querySelectorAll('g > path[fill="#fcd116"]')).map((path) =>
      path.getAttribute('d')!,
    );
  }

  it('paints the field in the first colour and everything laid on it in the second', () => {
    expect(crest('first').querySelector('rect')!.getAttribute('fill')).toBe('#c0392b');
    expect(laidOn().length).toBeGreaterThanOrEqual(2);
  });

  it('changes the shape without changing the stripes', async () => {
    const shield = outline();
    const stripes = laidOn();

    host.shape.set('round');
    await fixture.whenStable();

    expect(outline()).not.toBe(shield);
    expect(laidOn()).toEqual(stripes);
  });

  it('changes the stripes without changing the shape', async () => {
    const shield = outline();
    const stripes = laidOn();

    host.pattern.set('hoops');
    await fixture.whenStable();

    expect(outline()).toBe(shield);
    expect(laidOn()).not.toEqual(stripes);
  });

  it('draws plain stripes as nothing but the field and the border', async () => {
    host.pattern.set('plain');
    await fixture.whenStable();

    expect(laidOn()).toHaveLength(0);
  });

  it('follows a change of colour', async () => {
    host.primary.set('#12284c');
    await fixture.whenStable();

    expect(crest('first').querySelector('rect')!.getAttribute('fill')).toBe('#12284c');
  });

  it('draws a code it does not know as the default rather than nothing', async () => {
    host.shape.set('from-a-newer-server');
    host.pattern.set('from-a-newer-server');
    await fixture.whenStable();

    expect(outline()).toBeTruthy();
    expect(laidOn().length).toBeGreaterThan(0);
  });

  it('is hidden from assistive technology unless it is given a label', async () => {
    expect(crest('first').getAttribute('aria-hidden')).toBe('true');
    expect(crest('first').getAttribute('role')).toBeNull();

    host.label.set('Ashvale United crest');
    await fixture.whenStable();

    expect(crest('first').getAttribute('role')).toBe('img');
    expect(crest('first').getAttribute('aria-label')).toBe('Ashvale United crest');
    expect(crest('first').getAttribute('aria-hidden')).toBeNull();
  });

  it('gives each crest its own clip so two on a page do not clip one another', () => {
    const first = crest('first').querySelector('clipPath')!.id;
    const second = crest('second').querySelector('clipPath')!.id;

    expect(first).not.toBe(second);
    expect(crest('first').querySelector('g')!.getAttribute('clip-path')).toBe(`url(#${first})`);
    expect(crest('second').querySelector('g')!.getAttribute('clip-path')).toBe(`url(#${second})`);
  });
});
