import { Component } from '@angular/core';

@Component({
  selector: 'app-home',
  template: `
    <section
      class="mx-auto max-w-2xl rounded-xl border border-slate-200 bg-white p-6 shadow-sm md:p-8"
    >
      <h1 class="text-2xl font-semibold tracking-tight text-slate-900">
        Touchline Manager Online
      </h1>
      <p class="mt-3 text-sm leading-6 text-slate-600">
        A persistent football-management MMO. Six fictional national pyramids, three matchdays
        every week, and AI managers keeping every vacancy playable until you take over a club.
      </p>
      <p class="mt-4 rounded-md bg-amber-50 px-3 py-2 text-xs leading-5 text-amber-900">
        Stage 1 scaffold complete. Account registration, onboarding, and the club dashboard arrive
        with Stage 2.
      </p>
    </section>
  `,
})
export class Home {}
