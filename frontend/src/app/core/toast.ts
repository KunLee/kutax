import { Component, Injectable, inject, signal } from '@angular/core';
import { Icon } from './icon';
import { TranslatePipe } from './i18n';

export interface ToastMessage { text: string; action?: { label: string; run: () => Promise<void> | void }; }

/** One short message at a time, shown above the tab bar for 10 seconds and kept across page changes. */
@Injectable({ providedIn: 'root' })
export class Toasts {
  readonly current = signal<ToastMessage | null>(null);
  readonly busy = signal(false);
  private timer: ReturnType<typeof setTimeout> | undefined;

  show(message: ToastMessage, seconds = 10) {
    clearTimeout(this.timer);
    this.current.set(message);
    this.timer = setTimeout(() => this.current.set(null), seconds * 1000);
  }

  close() {
    clearTimeout(this.timer);
    this.current.set(null);
  }

  async act() {
    const action = this.current()?.action;
    if (!action || this.busy()) return;
    this.busy.set(true);
    try {
      await action.run();
    } finally {
      this.busy.set(false);
    }
  }
}

@Component({
  selector: 'app-toast',
  imports: [TranslatePipe, Icon],
  template: `
    @if (toasts.current(); as t) {
      <div class="toast" role="status">
        <span>{{ t.text | t }}</span>
        @if (t.action) { <button class="btn btn--small" type="button" (click)="toasts.act()" [disabled]="toasts.busy()">{{ t.action.label | t }}</button> }
        <button class="btn btn--small btn--quiet close" type="button" (click)="toasts.close()" [attr.aria-label]="'Dismiss' | t"><app-icon name="close" [size]="16" /></button>
      </div>
    }
  `,
  styles: `
    .toast { align-items: center; background: var(--ink); border-radius: 16px; bottom: calc(var(--tabbar, 0px) + 16px); box-shadow: var(--shadow); color: var(--white); display: flex; gap: 10px; left: 50%; max-width: min(560px, calc(100vw - 32px)); padding: 10px 10px 10px 16px; position: fixed; transform: translateX(-50%); width: max-content; z-index: 70; }
    .toast span { flex: 1; font-size: 14px; }
    .close { color: var(--white); }
  `,
})
export class Toast {
  readonly toasts = inject(Toasts);
}
