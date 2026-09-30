import AxeBuilder from '@axe-core/playwright';
import { Locator, Page, expect } from '@playwright/test';

/**
 * The accessibility gate (`§11.3`, `§15.6`).
 *
 * Master plan §15.6 asks for "automated axe checks plus manual keyboard/screen-reader checks for core
 * routes", and §11.3 fixes the standard at WCAG 2.2 AA. These helpers run axe against the rendered page
 * and report violations by element, so a regression is actionable rather than a bare "axe failed".
 */

/**
 * The axe tags the gate is filtered to.
 *
 * Not axe's whole rule set: these are exactly the WCAG 2.2 A and AA success criteria, so the gate matches
 * the standard the product claims and excludes best-practice-only rules that would make it noisy.
 */
export const WCAG_22_AA_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] as const;

/** One axe violation, flattened to what a failure message needs. */
export interface A11yViolation {
  readonly id: string;
  readonly impact: string;
  readonly help: string;
  readonly targets: readonly string[];
}

/** Runs axe over the current page and returns its WCAG 2.2 AA violations. */
export async function collectA11yViolations(page: Page): Promise<A11yViolation[]> {
  const results = await new AxeBuilder({ page }).withTags([...WCAG_22_AA_TAGS]).analyze();

  return results.violations.map((violation) => ({
    id: violation.id,
    impact: violation.impact ?? 'unknown',
    help: violation.help,
    targets: violation.nodes.map((node) => node.target.join(' ')),
  }));
}

/** Renders violations as one line per rule, naming the offending element. */
export function renderA11yViolations(violations: readonly A11yViolation[]): string {
  return violations
    .map(
      (violation) =>
        `- ${violation.impact} [${violation.id}] ${violation.help} → ${violation.targets.join(' | ')}`,
    )
    .join('\n');
}

/** Fails if the current page has any WCAG 2.2 AA violation. */
export async function expectNoA11yViolations(page: Page): Promise<void> {
  const violations = await collectA11yViolations(page);

  expect(
    violations,
    `axe reported WCAG 2.2 AA violations:\n${renderA11yViolations(violations)}`,
  ).toEqual([]);
}

/**
 * Fails if a focused control does not show a visible focus ring.
 *
 * The shared control constants carry `focus:outline-none`; the ring survives only because the global
 * `:focus-visible` rule in `styles.css` is unlayered and therefore wins. This guards that arrangement, so
 * a change to either side cannot silently remove the focus indicator (WCAG 2.4.7).
 */
export async function expectFocusRing(locator: Locator): Promise<void> {
  await locator.focus();

  const outline = await locator.evaluate((element) => {
    const style = getComputedStyle(element);

    return { style: style.outlineStyle, width: Number.parseFloat(style.outlineWidth) };
  });

  expect(outline.style, 'a focused control should show a visible outline').not.toBe('none');
  expect(outline.width, 'the focus outline should be at least 2px').toBeGreaterThanOrEqual(2);
}
