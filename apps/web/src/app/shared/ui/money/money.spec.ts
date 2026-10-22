import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Money } from './money';
import { configurePresentation, resetPresentation } from '../../../core/world/presentation';

// The short form of an amount follows the manager's locale, so the specs pin one.
beforeEach(() => configurePresentation({ locale: 'en-GB', timeZone: 'UTC' }));
afterEach(() => resetPresentation());

/**
 * The money amount.
 *
 * Short on the page, exact in the `title`, and signed in words rather than colour.
 */
describe('Money', () => {
  let fixture: ComponentFixture<Money>;
  let root: HTMLElement;

  function render(minor: number, signed = false): void {
    fixture = TestBed.createComponent(Money);
    fixture.componentRef.setInput('minor', minor);
    fixture.componentRef.setInput('signed', signed);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const money = () => root.querySelector<HTMLElement>('[data-testid="money"]')!;

  it('shows the short form and keeps the whole amount on hover', () => {
    render(1_240_000_000);

    expect(money().textContent).toContain('12.4m');
    expect(money().getAttribute('title')).toContain('12,400,000.00');
  });

  it('hands a screen reader the exact figure as well', () => {
    render(85_000_000);

    expect(money().querySelector('.sr-only')?.textContent).toContain('850,000.00');
  });

  it('puts a minus in front of a debt without being asked', () => {
    render(-250_000_000);

    expect(money().textContent?.startsWith('−2.5m')).toBe(true);
  });

  it('puts a plus or a minus in front of a movement when signed', () => {
    render(100_000_000, true);
    expect(money().textContent?.startsWith('+1m')).toBe(true);

    render(-100_000_000, true);
    expect(money().textContent?.startsWith('−1m')).toBe(true);
  });

  it('prints a dash-free zero for a movement that moved nothing', () => {
    render(0, true);

    expect(money().textContent?.startsWith('0')).toBe(true);
  });
});
