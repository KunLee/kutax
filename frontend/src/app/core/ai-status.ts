import { Component, inject, input, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Session } from './session';
import { Icon } from './icon';
import { TranslatePipe } from './i18n';

/**
 * Whether KuTax reads receipts automatically, with the one tap that turns it on. People who can't change the setup
 * aren't told about it: without AI they simply type the details.
 */
@Component({
  selector: 'app-ai-status',
  imports: [TranslatePipe, RouterLink, Icon],
  template: `
    @switch (session.aiState()) {
      @case ('off') {
        @if (session.account()?.isAdministrator) {
          <p class="notice notice--warning ai" role="status"><app-icon name="spark" [size]="18" />
            <span><strong>{{ 'AI reading is off' | t }}</strong> — {{ 'you’ll type each receipt’s details yourself.' | t }} <a routerLink="/settings" [queryParams]="{ tab: 'ai' }">{{ 'Set up AI for everyone' | t }}</a>.</span></p>
        }
      }
      @case ('consent') {
        @if (!answered()) {
        <p class="notice ai" role="status"><app-icon name="spark" [size]="18" />
          <span><strong>{{ 'Let AI read your receipts?' | t }}</strong> {{ 'It fills in the merchant, date and amount; you just check them.' | t }}</span>
          <button class="btn btn--small btn--primary" type="button" (click)="turnOn()" [disabled]="busy()" [attr.aria-busy]="busy()">{{ 'Use AI' | t }}</button></p>
        }
      }
      @case ('on') {
        @if (!compact()) {
          <p class="notice notice--success ai" role="status"><app-icon name="spark" [size]="18" /><span><strong>{{ 'AI reading is on.' | t }}</strong> {{ 'KuTax fills in the details; you check and confirm them.' | t }}</span></p>
        }
      }
    }
  `,
  styles: `.ai { align-items: center; display: flex; gap: 10px; } .ai > span { flex: 1; } .ai app-icon { flex: none; }`,
})
export class AiStatus {
  readonly session = inject(Session);
  private readonly http = inject(HttpClient);
  /** Hide the "on" confirmation where it would only add noise. */
  readonly compact = input(false);
  readonly busy = signal(false);
  /** Asked once already (at the first upload): no more reminders; Settings → AI can still turn it on. */
  answered() {
    try { return localStorage.getItem('kutax.aiAsked.' + (this.session.account()?.id ?? '')) === '1'; } catch { return false; }
  }

  /** One tap: agrees to the current provider notice (the details stay in Settings → AI). */
  async turnOn() {
    if (this.busy()) return;
    this.busy.set(true);
    try {
      const privacy = await firstValueFrom(this.http.get<{ noticeVersion: string }>('/api/privacy'));
      await firstValueFrom(this.http.put('/api/privacy', { externalAiConsent: true, noticeVersion: privacy.noticeVersion }));
      await this.session.refreshAi();
    } catch {
      // Stays as it was; Settings → AI explains and can turn it on.
    } finally {
      this.busy.set(false);
    }
  }
}
