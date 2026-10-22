import { ComponentFixture, TestBed } from '@angular/core/testing';
import { wageImpact } from '../../../core/finance/finance-test-data';
import { WageImpact } from '../../../core/finance/finance.models';
import { WageImpactPanel } from './wage-impact';
import { configurePresentation, resetPresentation } from '../../../core/world/presentation';

// The short form of an amount follows the manager's locale, so the specs pin one.
beforeEach(() => configurePresentation({ locale: 'en-GB', timeZone: 'UTC' }));
afterEach(() => resetPresentation());

/**
 * The wage impact panel.
 *
 * The server works out what a deal does to the club; the panel says it in words and figures. It opens by itself
 * when the level asks for a confirm tick and stays folded when the deal fits.
 */
describe('WageImpactPanel', () => {
  let fixture: ComponentFixture<WageImpactPanel>;
  let root: HTMLElement;

  function render(impact: WageImpact, subject?: string): void {
    fixture = TestBed.createComponent(WageImpactPanel);
    fixture.componentRef.setInput('impact', impact);

    if (subject !== undefined) {
      fixture.componentRef.setInput('subject', subject);
    }

    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const details = () => root.querySelector<HTMLDetailsElement>('[data-testid="wage-impact"]')!;
  const billAfter = () => root.querySelector('[data-testid="wage-bill-after"]')!.textContent!;

  it('says the level in words, with the figures under it', () => {
    render(wageImpact());

    expect(details().textContent).toContain('Fits your budget');
    expect(billAfter()).toContain('7.9m');
    expect(billAfter()).toContain('8.4m');
    expect(billAfter()).toContain('66% of income');
    expect(root.querySelector('[data-testid="season-end-cash"]')!.textContent).toContain('2m');
  });

  it('stays folded when the deal fits and opens when it asks for a confirm tick', () => {
    render(wageImpact());
    expect(details().open).toBe(false);

    render(wageImpact({ level: 'danger', needsConfirmation: true, redFromMatchday: 21 }));
    expect(details().open).toBe(true);
  });

  it.each([
    ['stretching', 'Stretches your budget', 'amber'],
    ['danger', 'Danger: you would run out of cash', 'red'],
    ['administration_risk', 'Administration risk', 'red'],
  ] as const)('writes %s as "%s" in the %s family', (level, headline, family) => {
    render(wageImpact({ level, needsConfirmation: level !== 'stretching' }));

    expect(details().textContent).toContain(headline);
    expect(details().className).toContain(family);
  });

  it('says when the club would first be in the red', () => {
    render(wageImpact({ level: 'danger', needsConfirmation: true, redFromMatchday: 21 }));

    expect(root.querySelector('[data-testid="red-from"]')!.textContent).toContain(
      'in the red from about matchday 21',
    );
  });

  it('says nothing about the red when the club would not be in it', () => {
    render(wageImpact());

    expect(root.querySelector('[data-testid="red-from"]')).toBeNull();
  });

  it('names the deal for a screen reader', () => {
    render(wageImpact(), 'renewal');

    expect(details().querySelector('.sr-only')!.textContent).toContain('this renewal');
  });

  it('says the figures are for the smallest bid, so a higher bid is understood to cost more', () => {
    render(wageImpact());

    expect(details().textContent).toContain('smallest bid that is accepted');
  });
});
