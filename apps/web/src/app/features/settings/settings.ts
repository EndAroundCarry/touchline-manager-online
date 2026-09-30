import { Component, computed, effect, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiError } from '../../core/api/api-error';
import { AuthApi } from '../../core/auth/auth-api';
import { MaintenanceStore } from '../../core/maintenance/maintenance-store';
import { AccountExport } from '../../core/auth/auth.models';
import { SessionStore } from '../../core/auth/session-store';
import { NotificationPreferencesStore } from '../../core/notifications/notification-preferences-store';
import {
  NOTIFICATION_SWITCHES,
  UpdateNotificationPreferencesRequest,
} from '../../core/notifications/notification-preferences.models';
import { ActiveSession } from '../../core/sessions/sessions.models';
import { SessionsStore } from '../../core/sessions/sessions-store';
import { formatInstant } from '../../core/world/presentation';
import { OnboardingStore } from '../../core/world/onboarding-store';
import {
  CHECKBOX_INPUT,
  CHECKBOX_ROW,
  DESTRUCTIVE_BUTTON,
  FIELD_ERROR,
  FORM_ERROR,
  LINK,
  PAGE_HEADING,
  PRIMARY_BUTTON,
  SECONDARY_BUTTON,
  SELECT_INPUT,
  STATUS_MESSAGE,
  TEXT_INPUT,
} from '../../shared/forms/control-styles';
import { controlError, errorId } from '../../shared/forms/form-support';

/** Requires the deletion confirmation to be ticked. */
function mustBeAccepted(control: AbstractControl): ValidationErrors | null {
  return control.value === true ? null : { mustBeTrue: true };
}

/** The locales the formatting picker offers. A locale only affects how numbers and dates are rendered. */
const LOCALE_OPTIONS: readonly string[] = [
  'en-GB',
  'en-US',
  'en-IE',
  'de-DE',
  'es-ES',
  'fr-FR',
  'it-IT',
  'ro-RO',
];

/** A small, always-valid set of zones for a runtime without a full time-zone database. */
const FALLBACK_TIME_ZONES: readonly string[] = [
  'UTC',
  'Europe/London',
  'Europe/Dublin',
  'Europe/Madrid',
  'Europe/Berlin',
  'Europe/Rome',
  'Europe/Paris',
  'Europe/Bucharest',
  'America/New_York',
  'America/Los_Angeles',
];

/** Every zone the runtime knows, or the fallback set when it will not enumerate them. */
function supportedTimeZones(): readonly string[] {
  const intl = Intl as unknown as { supportedValuesOf?: (key: string) => string[] };

  try {
    const values = intl.supportedValuesOf?.('timeZone');

    return values !== undefined && values.length > 0 ? values : FALLBACK_TIME_ZONES;
  } catch {
    return FALLBACK_TIME_ZONES;
  }
}

/** Ensures the current value is selectable even when the runtime does not list it. */
function withCurrent(options: readonly string[], current: string | null): readonly string[] {
  if (current === null || current.length === 0 || options.includes(current)) {
    return options;
  }

  return [current, ...options];
}

/**
 * Account settings.
 *
 * Covers the profile, the manager name under an optimistic concurrency check, formatting preferences
 * (locale and time zone, `CAL-4`), notification switches, the session list (`F-07`), the account-data
 * export (master plan §12.4), and closing the account.
 */
@Component({
  selector: 'app-settings',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './settings.html',
})
export class Settings {
  private readonly store = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly maintenance = inject(MaintenanceStore);
  private readonly authApi = inject(AuthApi);
  private readonly notificationsStore = inject(NotificationPreferencesStore);
  private readonly sessionsStore = inject(SessionsStore);
  private readonly onboarding = inject(OnboardingStore);

  /** The signed-in account. */
  protected readonly user = this.store.user;

  /** Whether the address is confirmed. */
  protected readonly isVerified = this.store.isVerified;

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);

  /** Whether a write is allowed; every account change is refused offline or read-only (Â§11.4). */
  protected readonly canMutate = this.maintenance.canMutate;

  protected readonly nameForm = inject(FormBuilder).nonNullable.group({
    displayName: [
      '',
      [
        Validators.required,
        Validators.minLength(3),
        Validators.maxLength(32),
        Validators.pattern("^[A-Za-z0-9][A-Za-z0-9 _'-]*$"),
      ],
    ],
  });

  protected readonly nameSubmitting = signal(false);
  protected readonly nameSaved = signal(false);
  protected readonly nameError = signal<string | null>(null);
  protected readonly nameConflict = signal(false);

  /** The manager profile, which carries the formatting preferences. */
  protected readonly manager = computed(() => this.onboarding.state()?.manager ?? null);

  protected readonly prefsForm = inject(FormBuilder).nonNullable.group({
    locale: ['', [Validators.required]],
    timeZone: ['', [Validators.required]],
  });

  protected readonly prefsSubmitting = signal(false);
  protected readonly prefsSaved = signal(false);
  protected readonly prefsError = signal<string | null>(null);
  protected readonly prefsConflict = signal(false);

  /** The time zones the picker offers, with the manager's current one always present. */
  protected readonly timeZoneOptions = computed(() =>
    withCurrent(supportedTimeZones(), this.manager()?.timeZone ?? null),
  );

  /** The locales the picker offers, with the manager's current one always present. */
  protected readonly localeOptions = computed(() =>
    withCurrent(LOCALE_OPTIONS, this.manager()?.locale ?? null),
  );

  protected readonly deleteForm = inject(FormBuilder).nonNullable.group({
    password: ['', [Validators.required, Validators.maxLength(128)]],
    confirm: [false, [mustBeAccepted]],
  });

  protected readonly deleteSubmitting = signal(false);
  protected readonly deleteError = signal<string | null>(null);

  protected readonly signingOut = signal(false);

  /** The account's active sessions (`F-07`). */
  protected readonly sessions = this.sessionsStore.sessions;
  protected readonly sessionsLoading = this.sessionsStore.loading;
  protected readonly sessionsError = this.sessionsStore.error;
  protected readonly revokingId = this.sessionsStore.revokingId;

  /** The account-data export's in-flight state. */
  protected readonly exporting = signal(false);
  protected readonly exportError = signal<string | null>(null);
  protected readonly exportedAt = signal<string | null>(null);

  protected readonly textInputClass = TEXT_INPUT;
  protected readonly selectClass = SELECT_INPUT;
  protected readonly primaryButtonClass = PRIMARY_BUTTON;
  protected readonly secondaryButtonClass = SECONDARY_BUTTON;
  protected readonly destructiveButtonClass = DESTRUCTIVE_BUTTON;
  protected readonly pageHeadingClass = PAGE_HEADING;
  protected readonly formErrorClass = FORM_ERROR;
  protected readonly fieldErrorClass = FIELD_ERROR;
  protected readonly statusMessageClass = STATUS_MESSAGE;
  protected readonly linkClass = LINK;
  protected readonly checkboxClass = CHECKBOX_INPUT;
  protected readonly checkboxRowClass = CHECKBOX_ROW;

  /** The notification switches the screen shows, in order (`COM-4`). */
  protected readonly notificationSwitches = NOTIFICATION_SWITCHES;

  protected readonly notificationsLoading = this.notificationsStore.loading;
  protected readonly notificationsSaving = this.notificationsStore.saving;
  protected readonly notificationsSaved = this.notificationsStore.saved;
  protected readonly notificationsError = this.notificationsStore.error;
  protected readonly notificationsConflict = this.notificationsStore.conflict;

  /** The manager's in-progress switches, seeded from the server's state when it loads. */
  protected readonly notifyDraft = signal<UpdateNotificationPreferencesRequest>({
    emailDeadlineReminders: true,
    emailInactivityWarnings: true,
    emailMarketMessages: true,
    emailNewsDigest: true,
  });

  /** The element id the display-name error will carry. */
  protected readonly displayNameErrorId = errorId('displayName');

  /** The element id the delete-password error will carry. */
  protected readonly deletePasswordErrorId = errorId('deletePassword');

  constructor() {
    this.reload();
    this.notificationsStore.load();
    this.sessionsStore.load();
    this.loadManager();

    // Seed the draft from the loaded preferences, and re-seed it after a conflict reloads them, so the
    // save always starts from what is actually stored (`CONC-1`).
    effect(() => {
      const stored = this.notificationsStore.preferences();

      if (stored !== null) {
        this.notifyDraft.set({
          emailDeadlineReminders: stored.emailDeadlineReminders,
          emailInactivityWarnings: stored.emailInactivityWarnings,
          emailMarketMessages: stored.emailMarketMessages,
          emailNewsDigest: stored.emailNewsDigest,
        });
      }
    });

    // Seed the preference form once the manager profile arrives, and after a conflict reloads it. An
    // in-progress edit is left alone, so a background reload cannot discard the manager's choice.
    effect(() => {
      const manager = this.manager();

      if (manager !== null && this.prefsForm.pristine) {
        this.prefsForm.setValue({ locale: manager.locale, timeZone: manager.timeZone });
      }
    });
  }

  /** Reads the manager profile, which carries the formatting preferences. */
  private loadManager(): void {
    this.onboarding.loadState().subscribe({ error: () => undefined });
  }

  /** The current value of a notification switch. */
  protected notificationValue(field: keyof UpdateNotificationPreferencesRequest): boolean {
    return this.notifyDraft()[field];
  }

  /** Changes a switch in the draft. */
  protected setNotification(
    field: keyof UpdateNotificationPreferencesRequest,
    value: boolean,
  ): void {
    this.notifyDraft.update((draft) => ({ ...draft, [field]: value }));
  }

  /** Saves the notification switches. */
  protected saveNotifications(): void {
    this.notificationsStore.save(this.notifyDraft());
  }

  /** Re-reads the notification switches after a failure. */
  protected reloadNotifications(): void {
    this.notificationsStore.load();
  }

  /** Saves the formatting preferences (`CAL-4`). */
  protected savePreferences(): void {
    if (this.prefsSubmitting() || this.manager() === null) {
      return;
    }

    this.prefsSaved.set(false);
    this.prefsError.set(null);
    this.prefsConflict.set(false);

    if (this.prefsForm.invalid) {
      this.prefsForm.markAllAsTouched();

      return;
    }

    this.prefsSubmitting.set(true);

    const { locale, timeZone } = this.prefsForm.getRawValue();

    this.onboarding.updateManagerProfile(locale, timeZone).subscribe({
      next: () => {
        this.prefsForm.markAsPristine();
        this.prefsSubmitting.set(false);
        this.prefsSaved.set(true);
      },
      error: (error: unknown) => {
        this.prefsSubmitting.set(false);

        if (error instanceof ApiError && error.isPreconditionFailed) {
          // The profile changed elsewhere. Re-read it and let the manager reapply, rather than
          // overwriting the other change.
          this.prefsConflict.set(true);
          this.loadManager();

          return;
        }

        this.prefsError.set(
          error instanceof ApiError ? error.detail : 'Your preferences could not be saved.',
        );
      },
    });
  }

  /** Revokes one of the account's sessions, leaving the current one alone (`F-07`). */
  protected revokeSession(sessionId: string): void {
    this.sessionsStore.revoke(sessionId);
  }

  /** Re-reads the session list after a failure. */
  protected reloadSessions(): void {
    this.sessionsStore.load();
  }

  /** When a session was started, in the manager's chosen zone. */
  protected sessionIssued(session: ActiveSession): string {
    return formatInstant(session.issuedAt);
  }

  /** When a session was last used, or a plain "never". */
  protected sessionLastUsed(session: ActiveSession): string {
    return session.lastUsedAt === null ? 'Not used yet' : formatInstant(session.lastUsedAt);
  }

  /** Downloads the account's own data as a JSON file (`F-07`, master plan §12.4). */
  protected exportData(): void {
    if (this.exporting()) {
      return;
    }

    this.exporting.set(true);
    this.exportError.set(null);
    this.exportedAt.set(null);

    this.authApi.exportAccountData().subscribe({
      next: (data) => {
        this.exporting.set(false);
        this.exportedAt.set(data.generatedAt);
        this.download(data);
      },
      error: (error: unknown) => {
        this.exporting.set(false);
        this.exportError.set(
          error instanceof ApiError ? error.detail : 'Your data could not be exported.',
        );
      },
    });
  }

  private download(data: AccountExport): void {
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');

    link.href = url;
    link.download = `touchline-manager-account-${data.generatedAt.slice(0, 10)}.json`;
    link.click();

    URL.revokeObjectURL(url);
  }

  /** Reloads the profile and the entity tag the next write must carry. */
  protected reload(): void {
    this.loading.set(true);
    this.loadError.set(null);

    this.store.loadProfile().subscribe({
      next: (profile) => {
        this.nameForm.patchValue({ displayName: profile.displayName });
        this.applyVerificationToNameForm(profile.emailVerified);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(
          error instanceof ApiError ? error.detail : 'Your profile could not be loaded.',
        );
      },
    });
  }

  /**
   * Enables the manager name only for a confirmed address.
   *
   * The control's own disabled state is used rather than a `disabled` binding on the input. With a
   * reactive form the control owns that property — `FormControlName` re-applies it on every change —
   * so an attribute binding is silently ignored and the field stays editable while the screen claims
   * otherwise.
   */
  private applyVerificationToNameForm(verified: boolean): void {
    const control = this.nameForm.controls.displayName;

    if (verified) {
      control.enable({ emitEvent: false });
    } else {
      control.disable({ emitEvent: false });
    }
  }

  /** Resolves the message under the display-name field. */
  protected displayNameError(): string | null {
    return controlError(this.nameForm.get('displayName'), new Map(), 'DisplayName');
  }

  /** Saves the display name. */
  protected saveDisplayName(): void {
    if (this.nameSubmitting()) {
      return;
    }

    this.nameSaved.set(false);
    this.nameError.set(null);
    this.nameConflict.set(false);

    if (this.nameForm.invalid) {
      this.nameForm.markAllAsTouched();

      return;
    }

    this.nameSubmitting.set(true);

    this.store.updateDisplayName(this.nameForm.getRawValue().displayName).subscribe({
      next: () => this.nameSaved.set(true),
      error: (error: unknown) => {
        this.nameSubmitting.set(false);

        if (error instanceof ApiError) {
          if (error.isPreconditionFailed) {
            // Somebody changed the profile underneath this screen. Show the new state and let the
            // manager decide, rather than overwriting the other change.
            this.nameConflict.set(true);
            this.reload();

            return;
          }

          this.nameError.set(error.detail);

          return;
        }

        this.nameError.set('The change could not be saved. Try again.');
      },
      complete: () => this.nameSubmitting.set(false),
    });
  }

  /** Signs out of this device. */
  protected signOut(): void {
    this.signingOut.set(true);
    this.store.logout().subscribe(() => {
      this.sessionsStore.clear();
      void this.router.navigateByUrl('/login');
    });
  }

  /** Signs out of every device. */
  protected signOutEverywhere(): void {
    this.signingOut.set(true);
    this.store.logoutAll().subscribe(() => {
      this.sessionsStore.clear();
      void this.router.navigateByUrl('/login');
    });
  }

  /** Closes the account. */
  protected deleteAccount(): void {
    if (this.deleteSubmitting()) {
      return;
    }

    this.deleteError.set(null);

    if (this.deleteForm.invalid) {
      this.deleteForm.markAllAsTouched();

      return;
    }

    this.deleteSubmitting.set(true);

    this.store.deleteAccount(this.deleteForm.getRawValue().password).subscribe({
      next: () => {
        void this.router.navigateByUrl('/welcome');
      },
      error: (error: unknown) => {
        this.deleteSubmitting.set(false);
        this.deleteError.set(
          error instanceof ApiError ? error.detail : 'The account could not be closed. Try again.',
        );
      },
    });
  }

  /** The element id a field's error message will carry. */
  protected idFor(controlName: string): string {
    return errorId(controlName);
  }
}
