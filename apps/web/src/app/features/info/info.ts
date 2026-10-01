import { Component, OnInit, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StatusStore } from '../../core/status/status-store';
import { LINK_ACTION, PAGE_HEADING } from '../../shared/forms/control-styles';

/** The identifiers of the authored information documents, one per route. */
export type InfoDocumentId = 'rules' | 'privacy' | 'terms' | 'support';

/** A destination offered by a document section. */
export interface InfoLink {
  /** Link text that describes its destination out of context (`ACC-6`). */
  readonly label: string;

  /** An absolute in-app route that exists in `app.routes.ts`, so a link never falls through to the catch-all. */
  readonly path: string;
}

/** One section of an information document. */
export interface InfoSection {
  /** A stable anchor id, used as the section's `id` and `aria-labelledby` target. */
  readonly id: string;

  readonly heading: string;

  /** Body paragraphs, when the section reads as prose. */
  readonly paragraphs?: readonly string[];

  /** Body bullets, when the section reads as a list. */
  readonly bullets?: readonly string[];

  readonly links?: readonly InfoLink[];
}

/** One player-facing information document. */
export interface InfoDocument {
  readonly id: InfoDocumentId;

  readonly title: string;

  /** One sentence saying what the document covers. */
  readonly summary: string;

  /** Whether the document is versioned and shows the version the server records on consent (`LGL-1`). */
  readonly versioned?: boolean;

  readonly sections: readonly InfoSection[];
}

/**
 * The player-facing information pages (master plan §16 Stage 15, `F-55`, ADR-0050).
 *
 * Copy is held here rather than read from the server, following `features/help/` (`F-53`), so a page can
 * never render stale or empty. Every rule stated here restates a settled rule from
 * `docs/product/game-rules.md`; the privacy retention values are the ones fixed in
 * `docs/security/data-classification.md` §3.
 *
 * The wording obeys `docs/product/content-and-fictional-data-policy.md`: second person, present tense, fact
 * then consequence then action (`VOI-2`, `VOI-3`), glossary vocabulary (§2.2), and no hidden value —
 * `MAT-11` and `VOI-11` keep hidden potential, seeds, internal valuations and detection thresholds
 * server-side, so none of them is stated here.
 *
 * The rules and support pages are not versioned documents; the terms and privacy pages are, and show the
 * version `Auth:TermsVersion`/`Auth:PrivacyVersion` publishes so a page cannot disagree with the consent the
 * server records (`LGL-1`).
 */
export const INFO_DOCUMENTS: readonly InfoDocument[] = [
  {
    id: 'rules',
    title: 'Game rules',
    summary: 'The rules the server enforces, the shape of a season, and what this version of the game includes.',
    sections: [
      {
        id: 'competition',
        heading: 'The competition',
        bullets: [
          'Six countries each run their own national pyramid. You manage one club in one of them.',
          'Every division has 18 clubs, and one season is 34 matchdays: you meet each rival once at home and once away.',
          "A win is worth three points, a draw one, and a defeat none. Clubs level on points are separated by goal difference, then goals scored, then a fixed list of further criteria shown on your division's rules screen.",
          'At the rollover three clubs are promoted and three relegated between neighbouring divisions. The lowest division relegates nobody, and the top division promotes nobody, because there is no division beyond it.',
        ],
        links: [{ label: "Open your division's table", path: '/competitions' }],
      },
      {
        id: 'cadence',
        heading: 'Matchdays and deadlines',
        bullets: [
          'Matchdays are Tuesday, Thursday and Sunday, and every fixture kicks off at 19:00 UTC.',
          'Your team sheet locks 30 minutes before kick-off. The server then plays the match from the frozen snapshot, so nothing you change after the lock can affect it.',
          'Deadlines are stored in UTC and shown in your own time zone, with that zone named beside the moment so it is unambiguous wherever you are.',
        ],
        links: [
          { label: 'See your next deadline', path: '/fixtures' },
          { label: 'Choose your time zone', path: '/settings' },
        ],
      },
      {
        id: 'managing',
        heading: 'Managing a club',
        bullets: [
          'You inherit a club that is already running: its squad, contracts, cash and table position are exactly as they were.',
          'You set the formation, roles, instructions and team sheet before each deadline, and you train the squad between matchdays.',
          'Player attributes are exact and public, so scouting is a search rather than a gamble.',
          'Transfers run as timed auctions: a listing stays open for at least 48 hours and never resolves within six hours of a kick-off.',
        ],
        links: [
          { label: 'Open your squad', path: '/squad' },
          { label: 'Open the transfer market', path: '/transfers' },
        ],
      },
      {
        id: 'inactivity',
        heading: 'If you stop playing',
        bullets: [
          'Ten days without signing in brings a warning, and your club keeps running as normal.',
          'Fourteen days hands the routine decisions to the AI, which keeps the side legal without spending your money.',
          'Twenty-one days closes your tenure and returns the club to the game. Signing in at any point is how you take full control back.',
        ],
      },
      {
        id: 'scope',
        heading: 'What this version includes',
        paragraphs: [
          'This is a closed beta of the core management loop, and its scope is frozen so that what is here can be made solid before anything is added.',
        ],
        bullets: [
          'In this version: one persistent world, six national pyramids, squads, contracts, tactics, training, fixtures, tables, results, statistics, the club ledger, the transfer market, the inbox and news feed, and the season rollover.',
          'Not in this version: domestic cups, continental competitions, staff, a youth academy, loans, private negotiations, transfer windows, and any form of player-to-player messaging.',
        ],
      },
      {
        id: 'fictional',
        heading: 'Fictional world',
        paragraphs: [
          'Every club, player, competition, division, badge and kit in this game is generated fiction. No real club, real player, real competition or licensed mark appears anywhere in it.',
          'Country names are used as geography only, and the league names are generic or invented.',
        ],
      },
    ],
  },
  {
    id: 'privacy',
    title: 'Privacy',
    summary: 'What the game holds about you, why, how long it keeps it, and what you can do about it.',
    versioned: true,
    sections: [
      {
        id: 'what-we-hold',
        heading: 'What we hold',
        bullets: [
          'Your account: your email address, your manager name, a one-way hash of your password, your language and time zone, and your sign-in sessions.',
          'Your consent: the terms and privacy versions you accepted, with the time of acceptance.',
          'Your play: the club you manage, your squad decisions, your transfers, your matches, your club finances, and the audit trail of actions taken on your account.',
          'Security: a hashed client address and device reference used to detect abuse. These are kept as hashes, never as raw values.',
        ],
      },
      {
        id: 'retention',
        heading: 'How long we keep it',
        paragraphs: [
          'Competition records outlive the people who made them, because a league table is only meaningful if it cannot be rewritten. The rest is kept only as long as it is useful.',
        ],
        bullets: [
          'Matches, tables, season history, transfers, the ledger and the audit trail: kept permanently, because they are the record of the competition.',
          'Security addresses and device hashes: 90 days.',
          'Support correspondence: 24 months.',
          'Raw email delivery events: a few days, as our email provider defines.',
          'Sign-in sessions: until they expire or you end them.',
          'Multi-factor codes and email links: until they are used, reset, or expire.',
        ],
      },
      {
        id: 'export-deletion',
        heading: 'Access, export and deletion',
        bullets: [
          'You can download everything the game holds about you as one file from your settings.',
          'You can delete your account from your settings. Deletion closes your tenure, ends every session, waits out a short cooling period, and then anonymizes your identity.',
          'Deletion does not rewrite history. Matches, transfers, tables, finances and audit entries stay, attached to an anonymized record, so the competition stays intact.',
        ],
        links: [{ label: 'Open your account settings', path: '/settings' }],
      },
      {
        id: 'what-we-do-not',
        heading: 'What we do not do',
        bullets: [
          'We do not use third-party advertising trackers.',
          'We do not fingerprint your device beyond the risk hash used to detect abuse.',
          'We do not sell your personal data.',
          'Our own analytics are counts — how far people get through onboarding, how many clubs are held — and carry no per-manager detail.',
        ],
      },
      {
        id: 'real-money',
        heading: 'No real money',
        paragraphs: [
          'Nothing in this game has real-money value. There is no premium currency, no loot boxes, and no way to buy a competitive advantage. You cannot trade anything in the game for money, and you cannot trade with another manager outside an auction.',
        ],
      },
      {
        id: 'questions',
        heading: 'Questions',
        paragraphs: [
          'If you have a question about your data, the support page explains how to reach the operator team and what to include.',
        ],
        links: [{ label: 'Read the support page', path: '/support' }],
      },
    ],
  },
  {
    id: 'terms',
    title: 'Terms of service',
    summary: 'The agreement between you and Touchline Manager for using the game.',
    versioned: true,
    sections: [
      {
        id: 'acceptance',
        heading: 'Accepting these terms',
        paragraphs: [
          'You accept these terms and the privacy policy when you create an account. The version you accepted is recorded with the time of acceptance.',
          'If we change these terms, we publish a new version with a new version number, and continuing to use the game accepts it.',
        ],
        links: [{ label: 'Read the privacy policy', path: '/privacy' }],
      },
      {
        id: 'account',
        heading: 'Your account',
        bullets: [
          'One account, one manager career. You may not hold two clubs, and you may not share or sell an account.',
          'Keep your password and any multi-factor codes to yourself. You are responsible for what is done through your account.',
          'Give an email address you can receive mail at, so you can confirm your account and recover it.',
        ],
      },
      {
        id: 'fair-play',
        heading: 'Fair play',
        bullets: [
          'Do not cheat, and do not exploit a defect. If you find one, report it rather than profiting from it.',
          'Do not use automated access that harms the service or other managers.',
          'Do not collude with another manager to move players or money outside the game’s own market rules.',
          'The server is the only authority on results. There is no way to simulate, influence or replay a match from a client.',
        ],
      },
      {
        id: 'fictional',
        heading: 'A fictional world',
        paragraphs: [
          'All clubs, players, competitions, badges and kits are generated fiction. Nothing in the game has real-money value, and nothing may be traded for money.',
        ],
      },
      {
        id: 'availability',
        heading: 'Availability and changes',
        bullets: [
          'The game runs a Tuesday, Thursday and Sunday cadence, and results are published automatically after each kick-off.',
          'During maintenance the game may be read-only for a while: your screens stay readable, but commands are paused. The reason is shown in a banner.',
          'Features, rules and the match engine may change during the beta. A change to the rules is published with a new version.',
        ],
        links: [{ label: 'Check the service status', path: '/status' }],
      },
      {
        id: 'ending',
        heading: 'Ending your account',
        bullets: [
          'You can delete your account at any time from your settings.',
          'We may suspend or close an account that breaks these terms, such as one used to cheat or to abuse another manager.',
          'When an account is deleted or closed, the competition records it produced are anonymized and kept.',
        ],
        links: [{ label: 'Open your account settings', path: '/settings' }],
      },
    ],
  },
  {
    id: 'support',
    title: 'Support',
    summary: 'How to report a problem, and what to include so it can be found quickly.',
    sections: [
      {
        id: 'reference',
        heading: 'Your support reference',
        paragraphs: [
          'Every response the game sends carries a reference, and the footer of the app shows the one for your current session. It identifies the exact request, so it is the single most useful thing to include in a report.',
        ],
      },
      {
        id: 'reporting',
        heading: 'Reporting a problem',
        bullets: [
          'Quote the reference shown in the footer.',
          'Say what you did, in what order, and the time it happened — your local time is fine.',
          'Say what you expected and what actually happened.',
          'If a screen shows a message, include it word for word.',
        ],
      },
      {
        id: 'before-you-report',
        heading: 'Before you report',
        bullets: [
          'Check the service status page: a planned maintenance window, and the reason for it, is shown there and in the banner at the top of the app.',
          'A result can appear a few minutes after kick-off while publication completes. If it is later than that, it is worth a report.',
          'A locked team sheet cannot be changed, and a match already played cannot be replayed. That is the rule rather than a fault.',
        ],
        links: [{ label: 'Check the service status', path: '/status' }],
      },
      {
        id: 'no-chat',
        heading: 'No in-game messaging',
        paragraphs: [
          'This version has no chat, no forums and no private messages between managers. Reports go to the operator team, not to another player.',
        ],
      },
      {
        id: 'what-happens-next',
        heading: 'What happens next',
        bullets: [
          'An operator uses the reference to find the exact request, fixture, matchday or job behind the problem.',
          'A money error is corrected by posting a correcting entry that preserves the original, never by editing the ledger.',
          'A result that was published incorrectly is investigated against the frozen snapshot it was played from.',
        ],
      },
    ],
  },
];

/**
 * An information page — rules, privacy, terms or support (master plan §16 Stage 15, `F-55`, ADR-0050).
 *
 * The document is selected by the route's `data.document`, bound to the `document` input, so one component
 * serves all four and a new page is a route plus a document in {@link INFO_DOCUMENTS}. The terms and privacy
 * pages show the published version once the shared status read resolves.
 */
@Component({
  selector: 'app-info',
  imports: [RouterLink],
  templateUrl: './info.html',
})
export class Info implements OnInit {
  private readonly status = inject(StatusStore);

  /** The document to render, bound from the route's `data.document`. */
  readonly document = input.required<InfoDocumentId>();

  /** Reads the published version once, for the documents that show one (`LGL-1`). */
  ngOnInit(): void {
    if (this.doc().versioned === true) {
      this.status.refresh();
    }
  }

  /** The authored document for this route. */
  protected readonly doc = computed(() => {
    const found = INFO_DOCUMENTS.find((candidate) => candidate.id === this.document());

    if (found === undefined) {
      throw new Error(`Unknown information document: ${this.document()}`);
    }

    return found;
  });

  /** The published version of this document, or null for a document that is not versioned. */
  protected readonly version = computed(() => {
    const status = this.status.status();

    if (status === null) {
      return null;
    }

    switch (this.document()) {
      case 'terms':
        return status.documents.termsVersion;
      case 'privacy':
        return status.documents.privacyVersion;
      default:
        return null;
    }
  });

  /** Whether a versioned document's version could not be read. */
  protected readonly versionFailed = computed(() => this.doc().versioned === true && this.version() === null && this.status.failed());

  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly linkActionClass = LINK_ACTION;
}
