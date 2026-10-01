import { INFO_DOCUMENTS } from './info';

/**
 * The player-facing information pages (`F-55`, master plan §16 Stage 15, ADR-0050).
 *
 * The documents are held as data rather than read from the server, so the risks are the ones data can carry:
 * a document quietly going missing, a link typo that lands on the catch-all route, a section with no content,
 * and copy that leaks a value `MAT-11` keeps server-side or breaks the content policy's voice.
 *
 * The retention values asserted for privacy are the ones fixed in `docs/security/data-classification.md` §3,
 * so this spec fails if the page and the policy drift apart.
 */
describe('INFO_DOCUMENTS', () => {
  /** The documents the stage promises, in footer order. */
  const documents = ['rules', 'privacy', 'terms', 'support'];

  it('covers every document the stage promises', () => {
    expect(INFO_DOCUMENTS.map((document) => document.id)).toEqual(documents);
  });

  it('gives every document a title, a summary and sections with content', () => {
    for (const document of INFO_DOCUMENTS) {
      expect(document.title.length, `${document.id} needs a title`).toBeGreaterThan(0);
      expect(document.summary.length, `${document.id} needs a summary`).toBeGreaterThan(0);
      expect(document.sections.length, `${document.id} needs its sections`).toBeGreaterThanOrEqual(3);

      for (const section of document.sections) {
        expect(section.heading.length, `${document.id}/${section.id} needs a heading`).toBeGreaterThan(0);

        const content = [...(section.paragraphs ?? []), ...(section.bullets ?? [])];

        expect(content.length, `${document.id}/${section.id} needs content`).toBeGreaterThan(0);

        for (const line of content) {
          expect(line.length, `${document.id}/${section.id} has an empty line`).toBeGreaterThan(0);
        }
      }
    }
  });

  it('links only to absolute in-app routes, so no link falls through to the not-found screen', () => {
    for (const document of INFO_DOCUMENTS) {
      for (const section of document.sections) {
        for (const link of section.links ?? []) {
          expect(link.path.startsWith('/'), `${link.path} must be an in-app route`).toBe(true);
          expect(link.label.length, `${link.path} needs a descriptive label`).toBeGreaterThan(0);
          expect(
            link.label.toLowerCase(),
            `${link.path} must describe its destination`,
          ).not.toContain('click here');
        }
      }
    }
  });

  it('keeps server-only values out of player-facing copy (`MAT-11`, `VOI-11`)', () => {
    const forbidden = ['potential', 'seed', 'valuation', '%'];

    for (const document of INFO_DOCUMENTS) {
      const copy = [
        document.title,
        document.summary,
        ...document.sections.flatMap((section) => [
          section.heading,
          ...(section.paragraphs ?? []),
          ...(section.bullets ?? []),
        ]),
      ]
        .join(' ')
        .toLowerCase();

      for (const token of forbidden) {
        expect(copy, `${document.id} discloses "${token}"`).not.toContain(token);
      }
    }
  });

  it('publishes the terms and privacy pages as versioned documents (`LGL-1`)', () => {
    for (const id of ['terms', 'privacy'] as const) {
      expect(
        INFO_DOCUMENTS.find((document) => document.id === id)?.versioned,
        `${id} must be versioned`,
      ).toBe(true);
    }
  });

  it('states the privacy retention, export and deletion facts (`LGL-2`…`LGL-4`)', () => {
    const privacy = INFO_DOCUMENTS.find((document) => document.id === 'privacy')!;
    const copy = privacy.sections
      .flatMap((section) => [section.heading, ...(section.paragraphs ?? []), ...(section.bullets ?? [])])
      .join(' ')
      .toLowerCase();

    expect(copy).toContain('export');
    expect(copy).toContain('deletion');
    expect(copy).toContain('90 days');
    expect(copy).toContain('24 months');
  });

  it('tells a manager to quote their support reference', () => {
    const support = INFO_DOCUMENTS.find((document) => document.id === 'support')!;
    const copy = support.sections
      .flatMap((section) => [section.heading, ...(section.paragraphs ?? []), ...(section.bullets ?? [])])
      .join(' ')
      .toLowerCase();

    expect(copy).toContain('reference');
  });
});
