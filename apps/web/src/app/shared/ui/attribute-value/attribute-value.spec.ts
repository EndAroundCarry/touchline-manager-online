import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AttributeValue } from './attribute-value';

/**
 * The attribute display, which is the F-17 gate.
 *
 * Master plan §11.3: an attribute's colour must also display a number, an icon, or text. So the test does
 * not merely check that a value rendered — it checks that the band *word* rendered beside it, because a
 * band that only changed the colour would pass a naive test and fail the requirement.
 */
describe('AttributeValue', () => {
  let fixture: ComponentFixture<AttributeValue>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AttributeValue] }).compileComponents();

    fixture = TestBed.createComponent(AttributeValue);
  });

  async function render(label: string, value: number): Promise<string> {
    fixture.componentRef.setInput('label', label);
    fixture.componentRef.setInput('value', value);

    await fixture.whenStable();

    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('renders the number, its label, and the word for its band', async () => {
    const text = await render('Finishing', 17);

    expect(text).toContain('Finishing');
    expect(text).toContain('17');
    expect(text).toContain('Strong');
  });

  it('names a weak value as low rather than leaving it to the colour', async () => {
    const text = await render('Handling', 3);

    expect(text).toContain('3');
    expect(text).toContain('Low');
  });

  it('names a middling value as average', async () => {
    const text = await render('Passing', 10);

    expect(text).toContain('Average');
  });
});
