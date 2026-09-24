import { signal } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { requireAnonymous, requireAuthentication } from './auth.guards';
import { SessionStore } from './session-store';

/**
 * The guards are the only thing standing between a route and the wrong audience, so both directions
 * are asserted: the manager area bounces a signed-out visitor, and the sign-in screens bounce a
 * manager who already has a session.
 */

const route = {} as ActivatedRouteSnapshot;

function stateFor(url: string): RouterStateSnapshot {
  return { url } as RouterStateSnapshot;
}

describe('auth guards', () => {
  const isAuthenticated = signal(false);

  beforeEach(() => {
    isAuthenticated.set(false);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SessionStore, useValue: { isAuthenticated: () => isAuthenticated() } },
      ],
    });
  });

  describe('requireAuthentication', () => {
    it('admits a manager who holds a session', () => {
      isAuthenticated.set(true);

      const result = TestBed.runInInjectionContext(() =>
        requireAuthentication(route, stateFor('/settings')),
      );

      expect(result).toBe(true);
    });

    it('redirects a signed-out visitor to sign-in, remembering where they were going', () => {
      const result = TestBed.runInInjectionContext(() =>
        requireAuthentication(route, stateFor('/settings')),
      );

      expect(result).toBeInstanceOf(UrlTree);
      expect((result as UrlTree).toString()).toBe('/login?returnUrl=%2Fsettings');
    });
  });

  describe('requireAnonymous', () => {
    it('admits a signed-out visitor to the sign-in screens', () => {
      const result = TestBed.runInInjectionContext(() =>
        requireAnonymous(route, stateFor('/login')),
      );

      expect(result).toBe(true);
    });

    it('moves a signed-in manager away from a form that would replace their session', () => {
      isAuthenticated.set(true);

      const result = TestBed.runInInjectionContext(() =>
        requireAnonymous(route, stateFor('/login')),
      );

      expect(result).toBeInstanceOf(UrlTree);
      expect((result as UrlTree).toString()).toBe('/settings');
    });
  });
});
