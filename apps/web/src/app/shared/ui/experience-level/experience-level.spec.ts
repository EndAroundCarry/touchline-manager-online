import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlayerExperience } from '../../../core/squad/squad.models';
import { ExperienceLevel } from './experience-level';

/**
 * The experience display. The pips are decoration: the level is also written, and read out in full, so the
 * test checks the words and the sr-only sentence, not only the pips.
 */
describe('ExperienceLevel', () => {
  let fixture: ComponentFixture<ExperienceLevel>;
  let root: HTMLElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ExperienceLevel] }).compileComponents();

    fixture = TestBed.createComponent(ExperienceLevel);
    root = fixture.nativeElement as HTMLElement;
  });

  async function render(experience: PlayerExperience, compact = false): Promise<void> {
    fixture.componentRef.setInput('experience', experience);
    fixture.componentRef.setInput('compact', compact);

    await fixture.whenStable();
  }

  function text(testId: string): string {
    return root.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? '';
  }

  function filledPips(): number {
    return root.querySelectorAll('[data-testid="experience-pips"] > span.bg-accent').length;
  }

  it('says there is no level yet and offers no bonus before the first step', async () => {
    await render({
      points: 12.5,
      level: 0,
      maxLevel: 5,
      skillBonusPercent: 0,
      nextLevelPoints: 20,
    });

    expect(text('experience-headline')).toBe('No level yet · 12.5 / 100');
    expect(root.textContent).toContain('No bonus yet');
    expect(filledPips()).toBe(0);
    expect(text('experience-spoken')).toBe(
      'Experience: no level yet, 12.5 of 100 points. The next level is at 20 points.',
    );
  });

  it('fills the pips up to the level and writes the bonus and the next step', async () => {
    await render({
      points: 47.5,
      level: 2,
      maxLevel: 5,
      skillBonusPercent: 2,
      nextLevelPoints: 60,
    });

    expect(text('experience-headline')).toBe('Level 2 · 47.5 / 100');
    expect(text('experience-bonus')).toBe('+2% skills in matches');
    expect(text('experience-next')).toBe('Next level at 60');
    expect(filledPips()).toBe(2);
    expect(root.querySelectorAll('[data-testid="experience-pips"] > span')).toHaveLength(5);
    expect(text('experience-spoken')).toBe(
      'Experience: level 2 of 5, 47.5 of 100 points, plus 2 per cent to skills in matches. ' +
        'The next level is at 60 points.',
    );
  });

  it('fills every pip at the highest level and offers no next step', async () => {
    await render({
      points: 100,
      level: 5,
      maxLevel: 5,
      skillBonusPercent: 5,
      nextLevelPoints: null,
    });

    expect(text('experience-headline')).toBe('Level 5 · 100 / 100');
    expect(filledPips()).toBe(5);
    expect(root.querySelector('[data-testid="experience-next"]')).toBeNull();
    expect(text('experience-spoken')).toBe(
      'Experience: level 5 of 5, 100 of 100 points, plus 5 per cent to skills in matches.',
    );
  });

  it('keeps the compact form to one line, with the bonus beside the level', async () => {
    await render(
      { points: 47.5, level: 2, maxLevel: 5, skillBonusPercent: 2, nextLevelPoints: 60 },
      true,
    );

    expect(root.querySelector('p')).toBeNull();
    expect(text('experience-bonus')).toBe('+2% skills in matches');
    expect(root.querySelector('[data-testid="experience-next"]')).toBeNull();
  });

  it('hides the pips and the visible words from assistive technology, which hears the sentence', async () => {
    await render({
      points: 47.5,
      level: 2,
      maxLevel: 5,
      skillBonusPercent: 2,
      nextLevelPoints: 60,
    });

    const hidden = root.querySelectorAll('[aria-hidden="true"]');

    expect(hidden.length).toBeGreaterThan(0);
    expect(root.querySelector('.sr-only[data-testid="experience-spoken"]')).not.toBeNull();
  });
});
