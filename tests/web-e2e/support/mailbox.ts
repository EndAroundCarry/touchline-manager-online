import { APIRequestContext } from '@playwright/test';

/**
 * The transactional mailbox.
 *
 * The verification and reset links are only ever delivered by email, so a journey that wants to
 * complete one has to read the message. Locally that mailbox is the Compose mail catcher, which
 * exposes the messages as JSON rather than requiring an IMAP client.
 */

const mailboxBaseUrl = process.env['E2E_MAILBOX_URL'] ?? 'http://localhost:8025';

/** How long to give the mail catcher before declaring the message lost. */
const deliveryTimeoutMs = 20_000;

const pollIntervalMs = 500;

interface MessageSummary {
  readonly ID: string;
  readonly Subject: string;
  readonly Created: string;
}

interface MailboxPage {
  readonly messages: readonly MessageSummary[];
}

interface MessageDocument {
  readonly Text: string;
}

/** Reads every message delivered to one address. */
async function messagesFor(
  request: APIRequestContext,
  address: string,
): Promise<readonly MessageSummary[]> {
  const response = await request.get(`${mailboxBaseUrl}/api/v1/search`, {
    params: { query: `to:${address}` },
  });

  if (!response.ok()) {
    throw new Error(
      `The mail catcher could not be searched (${response.status()}). Is it running on ${mailboxBaseUrl}?`,
    );
  }

  const page = (await response.json()) as MailboxPage;

  return page.messages ?? [];
}

/** Waits for the first email for an address whose subject contains a phrase, and returns its link. */
export async function waitForEmailLink(
  request: APIRequestContext,
  address: string,
  subjectContains: string,
): Promise<string> {
  const deadline = Date.now() + deliveryTimeoutMs;
  let delivered = 0;

  while (Date.now() < deadline) {
    const candidates = (await messagesFor(request, address))
      .filter((message) => message.Subject.includes(subjectContains))
      .sort((left, right) => right.Created.localeCompare(left.Created));

    delivered = candidates.length;

    if (candidates.length > 0) {
      const document = await request.get(`${mailboxBaseUrl}/api/v1/message/${candidates[0].ID}`);

      if (document.ok()) {
        const body = (await document.json()) as MessageDocument;
        const link = extractLink(body.Text);

        if (link !== null) {
          return link;
        }
      }
    }

    await new Promise((resolve) => setTimeout(resolve, pollIntervalMs));
  }

  throw new Error(
    `No "${subjectContains}" email reached ${address} within ${deliveryTimeoutMs}ms ` +
      `(saw ${delivered} matching message(s) for that address).`,
  );
}

/** Pulls the confirmation or reset URL out of a plain-text email body. */
export function extractLink(text: string): string | null {
  const match = /https?:\/\/\S+\/(?:verify-email|reset-password)\?[^\s]+/.exec(text);

  return match === null ? null : match[0];
}

/**
 * Reduces an emailed link to a path the browser can open against the configured base URL.
 *
 * The link is built from the API's `Auth:ClientBaseUrl`, which is the web client's public address.
 * Navigating by path rather than by the literal URL keeps a journey working when the suite is
 * pointed at a different host (`E2E_WEB_URL`) than the one in the email configuration.
 */
export function pathOf(link: string): string {
  const url = new URL(link);

  return `${url.pathname}${url.search}`;
}
