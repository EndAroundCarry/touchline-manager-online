import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StructurePicture } from './structure-picture';

/**
 * The structure picture.
 *
 * What it must get right: it says in words what it shows; it paints the club's two colours in; and a colour that is
 * not a plain hex colour never reaches an attribute.
 */
describe('Structure picture', () => {
  let fixture: ComponentFixture<StructurePicture>;

  async function render(primary: string | null, secondary: string | null): Promise<SVGElement> {
    TestBed.configureTestingModule({ imports: [StructurePicture] });
    fixture = TestBed.createComponent(StructurePicture);
    fixture.componentRef.setInput('kind', 'merchandise');
    fixture.componentRef.setInput('level', 4);
    fixture.componentRef.setInput('primaryColour', primary);
    fixture.componentRef.setInput('secondaryColour', secondary);
    await fixture.whenStable();

    return (fixture.nativeElement as HTMLElement).querySelector('svg')!;
  }

  const paints = (svg: SVGElement): string[] =>
    [...svg.querySelectorAll('path')].flatMap((path) =>
      ['fill', 'stroke'].map((name) => path.getAttribute(name)).filter((value) => value !== null),
    );

  it('names the building and its level', async () => {
    const svg = await render('#b3202a', '#ffffff');

    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toBe(
      "Merchandise shop at level 4 of 10, seen from a corner, in the club's two colours.",
    );
    expect(svg.getAttribute('data-level')).toBe('4');
  });

  it("paints the club's colours", async () => {
    const svg = await render('#B3202A', '#123456');

    expect(paints(svg)).toContain('#b3202a');
    expect(paints(svg)).toContain('#123456');
    expect(paints(svg)).not.toContain('primary');
    expect(paints(svg)).not.toContain('secondary');
  });

  it('replaces a colour that is not plain hex', async () => {
    const svg = await render('url(javascript:alert(1))', null);

    expect(paints(svg).some((paint) => paint.includes('url'))).toBe(false);
    expect(paints(svg)).toContain('#1f4e79');
  });
});
