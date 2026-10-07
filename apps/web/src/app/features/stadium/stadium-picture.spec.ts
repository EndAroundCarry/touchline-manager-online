import { ComponentFixture, TestBed } from '@angular/core/testing';
import { buildScene } from '../../core/stadium/stadium-scene';
import { StadiumStandCode } from '../../core/stadium/stadium.models';
import { StadiumPicture } from './stadium-picture';

/**
 * The stadium picture.
 *
 * What it must get right: it paints the scene of the level it is given, in the club's two colours and nothing but a
 * plain colour; it lights the kind of place it is told to and no other; it names the stands only when asked; and it
 * says what it shows in words, or is hidden from assistive technology when the screen already says so.
 */

describe('Stadium picture', () => {
  let fixture: ComponentFixture<StadiumPicture>;
  let svg: SVGSVGElement;

  async function render(inputs: Record<string, unknown> = {}): Promise<void> {
    fixture = TestBed.createComponent(StadiumPicture);
    fixture.componentRef.setInput('level', 4);

    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }

    await fixture.whenStable();
    svg = (fixture.nativeElement as HTMLElement).querySelector('svg')!;
  }

  async function set(name: string, value: unknown): Promise<void> {
    fixture.componentRef.setInput(name, value);
    await fixture.whenStable();
  }

  const paths = (role?: string) =>
    Array.from(svg.querySelectorAll(role === undefined ? 'path' : `path[data-role="${role}"]`));

  const strokes = (role: string) =>
    Array.from(new Set(paths(role).map((path) => path.getAttribute('stroke'))));

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [StadiumPicture] });
  });

  it('paints every shape of the level it is given, in the frame the level has', async () => {
    await render({ level: 6 });

    const scene = buildScene(6);

    expect(paths()).toHaveLength(scene.parts.length);
    expect(svg.getAttribute('data-level')).toBe('6');
    expect(svg.getAttribute('viewBox')).toBe(
      `${scene.view.x} ${scene.view.y} ${scene.view.width} ${scene.view.height}`,
    );
  });

  it("draws the seats in the club's two colours, the first colour with the second over it", async () => {
    await render({ primaryColour: '#8C2F39', secondaryColour: '#F2E3E5' });

    expect(strokes('seat')).toEqual(['#8c2f39']);
    expect(strokes('seat-alt')).toEqual(['#f2e3e5']);

    // The boards and the trim of the roofs take the colours too.
    const fills = new Set(paths('board').map((path) => path.getAttribute('fill')));

    expect(fills).toEqual(new Set(['#8c2f39', '#f2e3e5']));
    expect(new Set(paths('roof-trim').map((path) => path.getAttribute('fill')))).toEqual(
      new Set(['#f2e3e5']),
    );
  });

  it('dashes a seat so a row reads as seats and not as a stripe', async () => {
    await render();

    for (const path of paths('seat')) {
      expect(path.getAttribute('stroke-dasharray')).toMatch(/^[\d.]+ [\d.]+$/);
      expect(Number(path.getAttribute('stroke-width'))).toBeGreaterThan(0);
    }
  });

  it('replaces a colour that is not a plain hex colour before it reaches the drawing', async () => {
    await render({
      primaryColour: 'url(javascript:alert(1))',
      secondaryColour: '#8c2f39" onload="x',
    });

    expect(strokes('seat')).toEqual(['#1f4e79']);
    expect(strokes('seat-alt')).toEqual(['#d6e4f0']);
    expect(svg.outerHTML).not.toContain('javascript');
    expect(svg.outerHTML).not.toContain('onload');
  });

  it('lights no kind of place until it is told to', async () => {
    await render({ level: 8 });

    const planes = paths('region');

    expect(planes.length).toBeGreaterThan(0);
    expect(planes.every((plane) => plane.getAttribute('opacity') === '0')).toBe(true);
  });

  it('lights the sectors of the kind it is told to, and only those', async () => {
    await render({ level: 8 });

    for (const kind of ['standing', 'seating', 'covered_seating', 'vip'] as StadiumStandCode[]) {
      await set('highlight', kind);

      const lit = paths('region').filter((plane) => plane.getAttribute('opacity') !== '0');

      expect(lit.length, kind).toBeGreaterThan(0);
      expect(
        lit.every((plane) => plane.getAttribute('data-region') === kind),
        kind,
      ).toBe(true);
      expect(svg.getAttribute('data-highlight')).toBe(kind);
    }

    await set('highlight', null);

    expect(paths('region').every((plane) => plane.getAttribute('opacity') === '0')).toBe(true);
  });

  it('names the stands only when asked, and keeps a name the same size whatever the frame', async () => {
    await render({ level: 2 });

    expect(svg.querySelector('[data-testid="stadium-labels"]')).toBeNull();

    await set('labels', true);

    const names = Array.from(svg.querySelectorAll('[data-testid="stadium-labels"] text')).map(
      (text) => text.textContent,
    );

    expect(names).toEqual(['Main stand', 'East end', 'Opposite stand']);

    const small = Number(
      svg.querySelector('[data-testid="stadium-labels"]')!.getAttribute('font-size'),
    );

    await set('level', 10);

    const large = Number(
      svg.querySelector('[data-testid="stadium-labels"]')!.getAttribute('font-size'),
    );

    // A smaller ground fills a smaller part of the drawing, so its names are drawn smaller in it.
    expect(small).toBeLessThan(large);
  });

  it('draws fewer rows for a thumbnail', async () => {
    await render({ level: 8 });
    const full = paths().length;

    await set('detail', 'low');

    expect(paths().length).toBeLessThan(full);
  });

  it('says what it shows, or hides itself when the screen already says so', async () => {
    await render({ level: 3 });

    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toContain('level 3 of 10');
    expect(svg.getAttribute('aria-hidden')).toBeNull();

    await set('decorative', true);

    expect(svg.getAttribute('role')).toBeNull();
    expect(svg.getAttribute('aria-label')).toBeNull();
    expect(svg.getAttribute('aria-hidden')).toBe('true');
  });

  it('draws a level it has no plan for as the nearest one', async () => {
    await render({ level: 99 });

    expect(paths()).toHaveLength(buildScene(10).parts.length);
    expect(svg.getAttribute('aria-label')).toContain('level 10 of 10');
  });
});
