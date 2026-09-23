import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * Application root.
 *
 * It renders only the router outlet. Chrome lives in {@link AppShell}, which wraps the routes that
 * need navigation; public screens can therefore render without it.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: '<router-outlet />',
})
export class App {}
