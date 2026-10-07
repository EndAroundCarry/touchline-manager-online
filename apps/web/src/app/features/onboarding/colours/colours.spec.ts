import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ApiError } from '../../../core/api/api-error';
import { MaintenanceStore } from '../../../core/maintenance/maintenance-store';
import { OnboardingStore } from '../../../core/world/onboarding-store';
import { OnboardingState } from '../../../core/world/world.models';
import { ClubColoursChoice } from './colours';

/**
 * The colours step that follows taking a club over.
 *
 * The club arrives in colours that were generated for it, so the step starts on them and confirming is one
 * click. Saving moves on to the dashboard; a refused save stays put and says why, because the manager can
 * still fix the colours or leave them.
 */
describe('ClubColoursChoice', () => {
  function state(tenure: boolean): OnboardingState {
    return {
      manager: null,
      tenure: tenure
        ? ({
            clubName: 'Ashvale United',
            primaryColour: '#1f4e79',
            secondaryColour: '#d6e4f0',
            hasChosenColours: false,
          } as unknown as OnboardingState['tenure'])
        : null,
      serverTime: '2026-09-01T00:00:00Z',
    };
  }

  let store: { loadState: ReturnType<typeof vi.fn>; changeClubColours: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<ClubColoursChoice>;
  let root: HTMLElement;
  let navigate: ReturnType<typeof vi.spyOn>;

  async function open(loaded: OnboardingState): Promise<void> {
    store.loadState.mockReturnValue(of(loaded));

    await TestBed.configureTestingModule({
      imports: [ClubColoursChoice],
      providers: [
        provideRouter([]),
        { provide: OnboardingStore, useValue: store },
        { provide: MaintenanceStore, useValue: { canMutate: signal(true) } },
      ],
    }).compileComponents();

    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(ClubColoursChoice);
    root = fixture.nativeElement as HTMLElement;

    await fixture.whenStable();
  }

  function confirmButton(): HTMLButtonElement {
    return Array.from(root.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Confirm colours',
    )!;
  }

  beforeEach(() => {
    store = { loadState: vi.fn(), changeClubColours: vi.fn() };
  });

  it('starts on the colours the club was generated with, named for the club', async () => {
    await open(state(true));

    expect(root.textContent).toContain('Ashvale United');
    expect((root.querySelector('#kit-primary-hex') as HTMLInputElement).value).toBe('#1f4e79');
    expect((root.querySelector('#kit-secondary-hex') as HTMLInputElement).value).toBe('#d6e4f0');
  });

  it('saves the pair as it stands and opens the dashboard', async () => {
    store.changeClubColours.mockReturnValue(
      of({ clubId: 'club-1', primaryColour: '#1f4e79', secondaryColour: '#d6e4f0' }),
    );
    await open(state(true));

    confirmButton().click();
    await fixture.whenStable();

    expect(store.changeClubColours).toHaveBeenCalledWith('#1f4e79', '#d6e4f0');
    expect(navigate).toHaveBeenCalledWith('/dashboard');
  });

  it('saves a colour chosen from the picker', async () => {
    store.changeClubColours.mockReturnValue(
      of({ clubId: 'club-1', primaryColour: '#c0392b', secondaryColour: '#d6e4f0' }),
    );
    await open(state(true));

    const spectrum = root.querySelector('#kit-primary-spectrum') as HTMLInputElement;

    spectrum.value = '#c0392b';
    spectrum.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    confirmButton().click();
    await fixture.whenStable();

    expect(store.changeClubColours).toHaveBeenCalledWith('#c0392b', '#d6e4f0');
  });

  it('will not save the same colour twice', async () => {
    await open(state(true));

    const hex = root.querySelector('#kit-secondary-hex') as HTMLInputElement;

    hex.value = '#1f4e79';
    hex.dispatchEvent(new Event('input'));
    await fixture.whenStable();

    expect(confirmButton().disabled).toBe(true);
    expect(store.changeClubColours).not.toHaveBeenCalled();
  });

  it('stays on the step and says why when the save is refused', async () => {
    store.changeClubColours.mockReturnValue(
      throwError(
        () =>
          new ApiError(400, 'VALIDATION_FAILED', 'Choose two different colours.', null, new Map()),
      ),
    );
    await open(state(true));

    confirmButton().click();
    await fixture.whenStable();

    expect(navigate).not.toHaveBeenCalled();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain('Choose two different');
    expect(confirmButton().disabled).toBe(false);
  });

  it('offers the colours later, in Settings, without saving anything', async () => {
    await open(state(true));

    const link = Array.from(root.querySelectorAll('a')).find((anchor) =>
      anchor.textContent?.includes('Decide later'),
    )!;

    expect(link.getAttribute('href')).toBe('/dashboard');
    expect(store.changeClubColours).not.toHaveBeenCalled();
  });

  it('points a manager with no club back to choosing one', async () => {
    await open(state(false));

    expect(root.textContent).toContain('You do not manage a club yet.');
    expect(root.querySelector('app-kit-colour-picker')).toBeNull();
  });
});
