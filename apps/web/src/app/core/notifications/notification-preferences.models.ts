/** A manager's notification preferences (`COM-4`, master plan §10.7). */
export interface NotificationPreferences {
  readonly emailDeadlineReminders: boolean;
  readonly emailInactivityWarnings: boolean;
  readonly emailMarketMessages: boolean;
  readonly emailNewsDigest: boolean;
  readonly version: number;
  readonly serverTime: string;
}

/** The switches a manager may change. */
export interface UpdateNotificationPreferencesRequest {
  readonly emailDeadlineReminders: boolean;
  readonly emailInactivityWarnings: boolean;
  readonly emailMarketMessages: boolean;
  readonly emailNewsDigest: boolean;
}

/** The four switches, in the order the screen shows them. */
export const NOTIFICATION_SWITCHES: readonly {
  readonly field: keyof UpdateNotificationPreferencesRequest;
  readonly label: string;
  readonly description: string;
}[] = [
  {
    field: 'emailDeadlineReminders',
    label: 'Deadline reminders',
    description: 'An email before a matchday’s team sheet locks.',
  },
  {
    field: 'emailInactivityWarnings',
    label: 'Inactivity warnings',
    description: 'An email when your club is about to be handed to the AI.',
  },
  {
    field: 'emailMarketMessages',
    label: 'Transfer market',
    description: 'An email when you are outbid or a transfer completes.',
  },
  {
    field: 'emailNewsDigest',
    label: 'News digest',
    description: 'An email summary of division news.',
  },
];
