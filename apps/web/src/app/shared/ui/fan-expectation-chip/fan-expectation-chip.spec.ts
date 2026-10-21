import { ComponentFixture, TestBed } from '@angular/core/testing';
import { expectation } from '../../../core/fans/fan-test-data';
import { FanExpectationChip } from './fan-expectation-chip';

/**
 * The expectation chip.
 *
 * It must say the label in words (the tint is never alone), give the chances and the predicted score under it,
 * and tint a hopeful label as a win, an even one as a draw and a bleak one as a defeat.
 */
describe('FanExpectationChip', () => {
  let fixture: ComponentFixture<FanExpectationChip>;
  let root: HTMLElement;

  function render(inputs: Record<string, unknown>): void {
    fixture = TestBed.createComponent(FanExpectationChip);

    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }

    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const label = () => root.querySelector('[data-testid="fan-expectation-label"]')!;
  const detail = () => root.querySelector('[data-testid="fan-expectation-detail"]');

  it('writes the label out and says who is speaking for a screen reader', () => {
    render({ expectation: expectation() });

    expect(label().textContent).toContain('Should win');
    expect(label().querySelector('.sr-only')?.textContent).toContain('The fans expect');
  });

  it('gives the chances and the predicted score, the club first', () => {
    render({ expectation: expectation() });

    expect(detail()?.textContent).toContain('58% win, 24% draw, 18% loss');
    expect(detail()?.textContent).toContain('predicted 2–1');
  });

  it('can leave the detail out', () => {
    render({ expectation: expectation(), detail: false });

    expect(detail()).toBeNull();
  });

  it('says when the expectation can still change, and stays quiet once it is fixed', () => {
    render({ expectation: expectation(), frozen: false });
    expect(detail()?.textContent).toContain('can still change');

    fixture.componentRef.setInput('frozen', true);
    fixture.detectChanges();
    expect(detail()?.textContent).not.toContain('can still change');
  });

  it.each([
    ['should_win', 'emerald'],
    ['could_go_either_way', 'amber'],
    ['damage_limitation', 'red'],
  ])('tints %s in the %s family', (labelCode, family) => {
    render({ expectation: expectation({ labelCode, labelPhrase: 'Whatever' }) });

    expect(label().className).toContain(family);
  });
});
