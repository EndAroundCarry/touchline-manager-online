import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LINK_ACTION, PAGE_HEADING } from '../../shared/forms/control-styles';

/** A destination offered by a help topic. */
export interface HelpLink {
  /** Link text that describes its destination out of context (`ACC-6`). */
  readonly label: string;

  /** An absolute in-app route that exists in `app.routes.ts`, so a link never falls through to the catch-all. */
  readonly path: string;
}

/** One help subject: what it is, the settled rules behind it, and the screens it leads to. */
export interface HelpTopic {
  /** A stable anchor id, used as the section's `id` and `aria-labelledby` target. */
  readonly id: string;

  readonly title: string;

  /** One sentence saying what the subject covers. */
  readonly summary: string;

  /** The rules a manager needs, in plain language. */
  readonly points: readonly string[];

  readonly links: readonly HelpLink[];
}

/**
 * The guided help content (`F-53`, master plan §16 Stage 13).
 *
 * Copy is held here rather than read from the server, following `features/welcome/`, so the page can never
 * render stale or empty. Every point restates a settled rule from `docs/product/game-rules.md`, cited
 * beside it so a reader can check the page against the rule set:
 *
 * - rules: `WORLD-4`, `CAL-1`, `TBL-1`, `TBL-11`, `PR-1`, `PR-2`, `PR-4`, `OCC-1`-`OCC-3`
 * - deadlines: `CAL-2`, `CAL-3`, `CAL-4`
 * - tactics: `TAC-1`-`TAC-6`, `TAC-8`, `TAC-10`, `INS-1`-`INS-10`
 * - market: `SCT-1`, `SCT-3`, `TRF-2`-`TRF-7`, `TRF-13`, `FIN-10`, `FIN-15`, `INS-12`
 * - season: `CAL-1`, `CAL-6`, `PR-4`, `PR-7`, `CON-6`, `CON-8`, `CON-10`, `CON-11`
 *
 * The wording obeys `docs/product/content-and-fictional-data-policy.md`: second person, present tense,
 * fact then consequence then action (`VOI-2`, `VOI-3`), deadlines stated as absolute moments (`VOI-4`),
 * glossary vocabulary (§2.2), and no hidden value -- `MAT-11` and `VOI-11` keep hidden potential, seeds,
 * internal valuations and detection thresholds server-side, so none of them is stated here.
 */
export const HELP_TOPICS: readonly HelpTopic[] = [
  {
    id: 'rules',
    title: 'How the competition works',
    summary:
      'Every division runs by the same settled rules, and the server is the only authority on them.',
    points: [
      'Every division has 18 clubs, and one season is 34 matchdays: you meet each rival once at home and once away.',
      "A win is worth three points, a draw one, and a defeat none. Clubs that finish level are separated by goal difference, then goals scored, then a fixed list of further criteria: your division's rules screen shows that list, and the draw the season was given.",
      'At the end of a season three clubs are promoted and three relegated between neighbouring divisions. The lowest division relegates nobody, and the top division promotes nobody, because there is no division beyond it.',
      'If you stop signing in, the game looks after your club before it takes it from you: ten days without a login brings a warning, fourteen hands routine decisions to the AI, and twenty-one closes your tenure. Signing in at any point is how you take full control back.',
    ],
    links: [{ label: "Open your division's table", path: '/competitions' }],
  },
  {
    id: 'deadlines',
    title: 'Matchdays and deadlines',
    summary: 'Three matchdays a week, and a deadline to beat before each one.',
    points: [
      'Matchdays are Tuesday, Thursday and Sunday, and every fixture kicks off at 19:00 UTC.',
      'Your team sheet locks 30 minutes before kick-off. The server then plays the match from the frozen snapshot, so nothing you change after the lock can affect it.',
      "Deadlines are stored in UTC and shown in your own time zone, with that zone named beside the moment so it is unambiguous wherever you are. You choose the zone in your settings, and the server's clock stays the authority either way.",
    ],
    links: [
      { label: 'See your next deadline', path: '/fixtures' },
      { label: 'Choose your time zone', path: '/settings' },
    ],
  },
  {
    id: 'tactics',
    title: 'Formations, roles and instructions',
    summary:
      'Pick a shape, fill all eleven places, and give the side its instructions before the lock.',
    points: [
      'Six formations are in this version. A plan names exactly eleven places, and a saved side is either complete or empty, so you never take the pitch a player short by accident.',
      'Every place has a position family and a role. A player used out of position carries a familiarity penalty, so the right player in the right place is worth more than the best player in any place.',
      'Eight team instructions set your mentality, tempo, passing, width, pressing, defensive line, tackling and time wasting. Each has a bounded effect and a cost, so an aggressive setting buys you something and gives something up.',
      'You can move a place with the arrow keys as well as by dragging it, so the board is usable from the keyboard.',
    ],
    links: [{ label: 'Open your tactics board', path: '/tactics' }],
  },
  {
    id: 'market',
    title: 'Scouting and the transfer market',
    summary: 'Search the player database, shortlist what you like, and buy through timed auctions.',
    points: [
      'Scouting is a search of the public player database. Attributes are exact and visible, and your shortlists stay private to you.',
      'Transfers run as timed auctions. A listing stays open for at least 48 hours and never resolves within six hours of a kick-off, so you always know when the decision falls.',
      'Bids go up rather than down, and each one must clear a minimum increment. You may hold one active bid per listing, and the leading bid has its funds reserved until somebody outbids you and the reservation is released.',
      'You cannot bid beyond the cash you have available once your existing reservations are counted.',
      'Clubs whose manager has stepped away trade under exactly the same eligibility and affordability rules you do.',
      'Direct offers, private negotiation, loans, swaps and transfer windows are not in this version. The auction is the only way a player moves.',
    ],
    links: [
      { label: 'Search for players', path: '/scouting' },
      { label: 'Open the transfer market', path: '/transfers' },
    ],
  },
  {
    id: 'season',
    title: 'The season and the rollover',
    summary: 'A season is 34 matchdays, then a short rollover, then the next one begins.',
    points: [
      'A season runs its 34 matchdays, then the game takes seven days to close the season and open the next one.',
      'Promotion and relegation are applied only at the rollover, once every result and every table is final. A club that moves keeps its squad, its contracts, its cash and its history.',
      'Contracts advance a year at the rollover rather than on a real-world anniversary. The board automatically renews every expiring contract at the rollover, so you never lose a player when a deal runs out.',
      'An older player may announce that the coming season is their last. The announcement reaches your inbox, so you can plan for the place it leaves.',
      "Finished seasons are kept. Your club's earlier seasons stay readable, with the division it played in and where it finished.",
    ],
    links: [
      { label: 'Open your season history', path: '/history' },
      { label: "Open your division's table", path: '/competitions' },
    ],
  },
];

/**
 * Help -- how the game works (master plan §16 Stage 13, `F-53`).
 *
 * The reference half of the guided help: the five subjects the stage promises, each stating the rules and
 * pointing at the screen that owns them. It reads nothing from the server, so it is as available offline as
 * the explanation it carries.
 */
@Component({
  selector: 'app-help',
  imports: [RouterLink],
  templateUrl: './help.html',
})
export class Help {
  protected readonly topics = HELP_TOPICS;
  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly linkActionClass = LINK_ACTION;
}
