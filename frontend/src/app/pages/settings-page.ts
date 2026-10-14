import { Component, computed, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Session } from '../core/session';
import { Icon } from '../core/icon';
import { AccountSettings } from '../management/account-settings';
import { AiPrivacy } from '../management/ai-privacy';
import { AiSettingsEditor } from '../management/ai-settings';
import { Appearance } from '../management/appearance';
import { TranslatePipe } from '../core/i18n';
import { SharingSettings } from '../management/sharing-settings';

type Tab = 'account' | 'sharing' | 'ai' | 'look' | 'admin';

/** Settings in a few short tabs instead of one long page; the tab is in the address (?tab=ai) so links can open it. */
@Component({
  selector: 'app-settings-page',
  imports: [TranslatePipe, RouterLink, Icon, AccountSettings, AiPrivacy, AiSettingsEditor, Appearance, SharingSettings],
  template: `
    @if (session.account(); as user) {
      <div class="page settings">
        <section class="page-heading"><div><span class="eyebrow">{{ user.email }}</span><h1>{{ 'Settings' | t }}</h1></div></section>
        <div class="segmented settings-tabs" role="tablist" [attr.aria-label]="'Settings' | t">
          @for (item of tabs(); track item.key) {
            <a role="tab" [routerLink]="[]" [queryParams]="{ tab: item.key }" replaceUrl [attr.aria-selected]="current() === item.key" [class.on]="current() === item.key">
              <app-icon [name]="item.icon" [size]="16" />{{ item.label | t }}</a>
          }
        </div>
        <div role="tabpanel">
          @switch (current()) {
            @case ('account') {
              <app-account-settings [account]="user" [embedded]="true" (saved)="session.accountUpdated($event)" />
              <section class="card section">
                <span class="kicker">{{ 'Account' | t }}</span>
                <h2>{{ user.displayName || user.email }}</h2>
                <p class="muted small">{{ user.email }}</p>
                <button class="btn btn--danger" type="button" (click)="signOut()" [disabled]="session.busy()"><app-icon name="logout" [size]="18" />{{ 'Sign out' | t }}</button>
              </section>
            }
            @case ('sharing') { <app-sharing-settings /> }
            @case ('ai') {
              @if (user.isAdministrator) { <app-ai-settings (saved)="privacy.load()" /> }
              <app-ai-privacy #privacy />
            }
            @case ('look') { <app-appearance /> }
            @case ('admin') {
              <section class="card section">
                <span class="kicker">{{ 'Administrator' | t }}</span>
                <h2>{{ 'Run this KuTax' | t }}</h2>
                <div class="row">
                  <a class="btn" routerLink="/users"><app-icon name="users" [size]="18" />{{ 'Manage users' | t }}</a>
                  <a class="btn" routerLink="/rules"><app-icon name="shield" [size]="18" />{{ 'Tax rules' | t }}</a>
                </div>
              </section>
            }
          }
        </div>
      </div>
    }
  `,
  styles: `
    .settings-tabs { display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 16px; }
    .settings-tabs a { align-items: center; border-radius: 10px; color: var(--ink-soft); display: inline-flex; font-size: 14px; font-weight: 800; gap: 6px; min-height: 40px; padding: 0 14px; text-decoration: none; }
    .settings-tabs a.on { background: var(--white); box-shadow: var(--shadow-small); color: var(--ink); }
    .settings-tabs a:hover { color: var(--ink); }
    .settings :is(app-account-settings, app-sharing-settings, app-ai-settings, app-ai-privacy, app-appearance) { display: block; }
  `,
})
export class SettingsPage {
  readonly session = inject(Session);
  private readonly router = inject(Router);
  /** From ?tab= in the address. */
  readonly tab = input<string | undefined>();
  readonly tabs = computed(() => {
    const list: { key: Tab; label: string; icon: 'users' | 'spark' | 'settings' | 'shield' | 'home' }[] = [
      { key: 'account', label: 'You', icon: 'home' },
      { key: 'sharing', label: 'Sharing', icon: 'users' },
      { key: 'ai', label: 'AI', icon: 'spark' },
      { key: 'look', label: 'Look and language', icon: 'settings' },
    ];
    if (this.session.account()?.isAdministrator) list.push({ key: 'admin', label: 'Administrator', icon: 'shield' });
    return list;
  });
  readonly current = computed<Tab>(() => this.tabs().find((t) => t.key === this.tab())?.key ?? 'account');

  async signOut() {
    if (await this.session.signOut()) await this.router.navigateByUrl('/');
  }
}
