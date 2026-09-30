import { HELP_TOPICS } from './help';

/**
 * The guided help content (`F-53`, master plan §16 Stage 13).
 *
 * The topics are held as data rather than read from the server, so the risks are the ones data can carry:
 * a subject quietly going missing, a link typo that lands on the catch-all route, and copy that leaks a
 * value `MAT-11` keeps server-side. These assertions are cheap guards for all three.
 */
describe('HELP_TOPICS', () => {
  /** The subjects master plan §16 Stage 13 names, in the order a new manager needs them. */
  const subjects = ['rules', 'deadlines', 'tactics', 'market', 'season'];

  it('covers every subject the stage promises', () => {
    expect(HELP_TOPICS.map((topic) => topic.id)).toEqual(subjects);
  });

  it('gives every topic a title, a summary, points and somewhere to go', () => {
    for (const topic of HELP_TOPICS) {
      expect(topic.title.length, `${topic.id} needs a title`).toBeGreaterThan(0);
      expect(topic.summary.length, `${topic.id} needs a summary`).toBeGreaterThan(0);
      expect(topic.points.length, `${topic.id} needs its points`).toBeGreaterThanOrEqual(3);
      expect(topic.links.length, `${topic.id} needs a destination`).toBeGreaterThanOrEqual(1);

      for (const point of topic.points) {
        expect(point.length, `${topic.id} has an empty point`).toBeGreaterThan(0);
      }
    }
  });

  it('links only to absolute in-app routes, so no link falls through to the not-found screen', () => {
    for (const topic of HELP_TOPICS) {
      for (const link of topic.links) {
        expect(link.path.startsWith('/'), `${link.path} must be an in-app route`).toBe(true);
        expect(link.label.length, `${link.path} needs a descriptive label`).toBeGreaterThan(0);
        expect(
          link.label.toLowerCase(),
          `${link.path} must describe its destination`,
        ).not.toContain('click here');
      }
    }
  });

  it('keeps server-only values out of manager-facing copy (`MAT-11`, `VOI-11`)', () => {
    const forbidden = ['potential', 'seed', 'valuation', '%'];

    for (const topic of HELP_TOPICS) {
      const copy = [topic.title, topic.summary, ...topic.points].join(' ').toLowerCase();

      for (const token of forbidden) {
        expect(copy, `${topic.id} discloses "${token}"`).not.toContain(token);
      }
    }
  });
});
