import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Not-found page.
 *
 * It offers a way back rather than a dead end, and it does not blame the user for a stale link.
 */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  template: `
    <section class="mx-auto max-w-xl py-16 text-center">
      <h1 class="text-3xl font-bold tracking-tight">That page does not exist</h1>
      <p class="mt-3 text-slate-700">
        The link may be out of date, or the page may not have been built yet.
      </p>
      <a
        routerLink="/welcome"
        class="mt-6 inline-block rounded bg-slate-900 px-4 py-2 text-sm font-medium text-white hover:bg-slate-700"
      >
        Go to the welcome page
      </a>
    </section>
  `,
})
export class NotFound {}
