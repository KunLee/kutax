import { inject, Injectable, signal } from '@angular/core';
import { Location } from '@angular/common';
import { NavigationEnd, NavigationStart, Router } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';

/**
 * Remembers the page someone came from before opening the review section (say, the tax summary), so "Back" returns
 * there rather than to the previous receipt. Started by the app shell so it sees the navigation into review.
 */
@Injectable({ providedIn: 'root' })
export class ReturnTo {
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  /** The last URL outside /review, or null when review was opened directly. */
  readonly origin = signal<string | null>(null);
  /** Navigations inside review since leaving the origin; 1 means the browser's back is exactly the origin. */
  private steps = 0;
  /** How far the origin page was scrolled when it was left. */
  private scroll = 0;

  constructor() {
    this.router.events.subscribe((event) => {
      if (event instanceof NavigationStart && event.url.startsWith('/review') && !this.router.url.startsWith('/review')) this.scroll = scrollY;
      if (!(event instanceof NavigationEnd)) return;
      const url = event.urlAfterRedirects;
      if (url.startsWith('/review')) this.steps++;
      else { this.origin.set(url); this.steps = 0; }
    });
  }

  /** Where Back goes, named for the button. */
  label() {
    const origin = this.origin();
    if (!origin || origin.startsWith('/review')) return 'Back to queue';
    if (origin.startsWith('/summary')) return 'Back to tax summary';
    if (origin.startsWith('/history')) return 'Back to tax pack';
    if (origin === '/' || origin.startsWith('/?')) return 'Back to inbox';
    return 'Back';
  }

  async back() {
    const origin = this.origin();
    if (!origin) { await this.router.navigateByUrl('/review'); return; }
    // Straight back from the receipt opened there keeps browser history tidy; otherwise go to it directly.
    const top = this.scroll;
    if (this.steps === 1) {
      const arrived = firstValueFrom(this.router.events.pipe(filter((event) => event instanceof NavigationEnd)));
      this.location.back();
      await arrived;
    } else await this.router.navigateByUrl(origin);
    this.restoreScroll(top);
  }

  /** The origin page loads its data after arriving, so wait until it's tall enough, then return to the same spot. */
  private restoreScroll(top: number, tries = 40) {
    if (top <= 0) return;
    if (document.documentElement.scrollHeight - innerHeight >= top || tries <= 0) { scrollTo({ top, behavior: 'instant' }); return; }
    setTimeout(() => this.restoreScroll(top, tries - 1), 75);
  }
}
