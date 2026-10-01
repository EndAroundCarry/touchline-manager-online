/**
 * Transport shapes for the public service status (master plan §16 Stage 15, `F-55`, ADR-0050).
 *
 * Hand-written mirror of `TouchlineManager.Contracts.Status`, like the other module models and for the same
 * reason: the generated client is a later stage, and until then the compiler is the check that these stay in
 * step. Everything is `readonly`.
 *
 * The season and the next matchday are nullable, because the server answers before a world is seeded and the
 * page must be able to say so rather than render a zero that looks like a real season.
 */

/** The versions of the versioned documents a consent records (`LGL-1`). */
export interface DocumentVersions {
  readonly termsVersion: string;
  readonly privacyVersion: string;
}

/** The public service status the status page and the versioned pages read. */
export interface PublicStatus {
  readonly serverTime: string;
  readonly readOnly: boolean;
  readonly readOnlyMessage: string | null;
  readonly seasonNumber: number | null;
  readonly nextMatchdayAt: string | null;
  readonly documents: DocumentVersions;
}
