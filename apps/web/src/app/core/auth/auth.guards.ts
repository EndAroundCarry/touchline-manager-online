import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionStore } from './session-store';

/**
 * The authenticated area.
 *
 * The session is already decided by the bootstrap initializer, so a guard never has to wait: by the
 * time a route activates, the store knows whether there is a session.
 */
export const requireAuthentication: CanActivateFn = (_route, state) => {
  const store = inject(SessionStore);
  const router = inject(Router);

  if (store.isAuthenticated()) {
    return true;
  }

  // The attempted destination is carried through so the manager lands where they were going after
  // signing in, rather than being dumped on a default page.
  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/**
 * The signed-out screens.
 *
 * A signed-in manager sent to sign-in again should be moved on rather than shown a form that would
 * silently replace their session.
 */
export const requireAnonymous: CanActivateFn = () => {
  const store = inject(SessionStore);
  const router = inject(Router);

  return store.isAuthenticated() ? router.createUrlTree(['/settings']) : true;
};
