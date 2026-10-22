/**
 * Transport shapes for the community forum.
 *
 * Hand-written mirror of `Tactical11Manager.Contracts.Forum`, like the inbox and message models and for the same
 * reason: the compiler is the check that the two stay in step. Everything is `readonly`.
 */

/** The longest thread title, in characters. */
export const TITLE_MAX_CHARS = 150;

/** The shortest thread title, in characters. */
export const TITLE_MIN_CHARS = 3;

/** The longest post, in characters. */
export const BODY_MAX_CHARS = 10_000;

/** The shortest opening post, in characters. */
export const THREAD_BODY_MIN_CHARS = 3;

/** One subforum inside a section. */
export interface ForumSubforum {
  readonly id: string;
  readonly sectionId: string;
  readonly name: string;
  readonly description: string;
  /** False for Announcements, where only the game's staff start threads. */
  readonly canCreateThreads: boolean;
  readonly threadCount: number;
  readonly postCount: number;
  readonly latestActivityAt: string | null;
}

/** The international forum, or the forum of the manager's country. */
export interface ForumSection {
  readonly id: string;
  readonly title: string;
  readonly description: string;
  /** What language to write in, or null when the section has no such rule. */
  readonly languageNotice: string | null;
  readonly countryId: string | null;
  readonly countryName: string | null;
  readonly subforums: readonly ForumSubforum[];
}

/** Every section the manager may open. */
export interface ForumOverview {
  readonly sections: readonly ForumSection[];
  readonly serverTime: string;
}

/** One line of a subforum's thread list. */
export interface ForumThreadSummary {
  readonly id: string;
  readonly subforumId: string;
  readonly title: string;
  readonly authorManagerId: string;
  readonly authorManagerName: string;
  readonly authorClubName: string | null;
  readonly isPinned: boolean;
  readonly isAnnouncement: boolean;
  readonly replyCount: number;
  readonly createdAt: string;
  readonly lastActivityAt: string;
  readonly latestReplyAuthorName: string | null;
}

/** A subforum and its threads, pinned first and then by latest activity. */
export interface ForumThreadList {
  readonly subforumId: string;
  readonly subforumName: string;
  readonly sectionId: string;
  readonly sectionTitle: string;
  readonly languageNotice: string | null;
  readonly canCreateThreads: boolean;
  readonly threads: readonly ForumThreadSummary[];
}

/** One post in a thread; number 1 is the opening post. */
export interface ForumPost {
  readonly id: string;
  readonly threadId: string;
  readonly postNumber: number;
  readonly authorManagerId: string;
  readonly authorManagerName: string;
  readonly authorClubName: string | null;
  /** True for a post by the game's staff. */
  readonly isStaff: boolean;
  /** Always shown as text, never as markup. */
  readonly body: string;
  readonly createdAt: string;
}

/** A thread with all its posts. */
export interface ForumThreadDetail {
  readonly id: string;
  readonly subforumId: string;
  readonly subforumName: string;
  readonly sectionId: string;
  readonly sectionTitle: string;
  readonly languageNotice: string | null;
  readonly title: string;
  readonly authorManagerId: string;
  readonly authorManagerName: string;
  readonly authorClubName: string | null;
  readonly isPinned: boolean;
  readonly isAnnouncement: boolean;
  readonly canReply: boolean;
  readonly createdAt: string;
  readonly posts: readonly ForumPost[];
}

/** What starting a thread returns. */
export interface CreatedForumThread {
  readonly threadId: string;
}
