import { Component, inject, input, OnInit, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { Icon } from '../core/icon';
import { TranslatePipe } from '../core/i18n';
import { Session } from '../core/session';
import { Person, rememberInvite, Sharing } from '../core/sharing';

/** Opened from an invitation email once signed in: who it's from, and accepting it. */
@Component({
  selector: 'app-invite-page',
  imports: [TranslatePipe, RouterLink, Icon],
  template: `
    <section class="page narrow">
      <div class="card invite">
        <span class="thumb tone-purple"><app-icon name="users" /></span>
        @if (loading()) {
          <p>{{ 'Opening the invitation…' | t }}</p>
        } @else if (about(); as info) {
          <span class="kicker">{{ 'Invitation' | t }}</span>
          <h1>{{ '{name} invited you to help with their tax records' | t: { name: name(info.from) } }}</h1>
          <p class="muted">{{ 'You’ll be able to see, add to and prepare their tax records, switching between theirs and your own at any time. They can see what you do, and either of you can end it whenever you like.' | t }}</p>
          @if (info.state !== 'Pending') {
            <p class="notice notice--warning">{{ 'This invitation has expired or was already used. Ask {name} for a new one.' | t: { name: name(info.from) } }}</p>
          } @else if (info.email !== myEmail()) {
            <p class="notice notice--warning">{{ 'This invitation is for {email}. Sign out and sign in (or create an account) with that address to accept it.' | t: { email: info.email } }}</p>
          } @else {
            <div class="row">
              <button class="btn btn--primary" type="button" (click)="accept()" [disabled]="busy()" [attr.aria-busy]="busy()"><app-icon name="check" [size]="18" />{{ 'Accept' | t }}</button>
              <a class="btn btn--quiet" routerLink="/">{{ 'Not now' | t }}</a>
            </div>
          }
          @if (message()) { <p class="notice notice--error" role="alert">{{ message() | t }}</p> }
        } @else {
          <p class="notice notice--warning">{{ 'This invitation link isn’t valid. Ask for a new one.' | t }}</p>
        }
      </div>
    </section>
  `,
  styles: `
    .narrow { max-width: 640px; }
    .invite { display: grid; gap: 12px; justify-items: start; padding: 28px; }
    .invite h1 { font-size: clamp(24px, 4vw, 32px); margin: 0; }
  `,
})
export class InvitePage implements OnInit {
  private readonly sharing = inject(Sharing);
  private readonly session = inject(Session);
  readonly token = input.required<string>();
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly about = signal<{ from: Person | null; email: string; state: string } | null>(null);
  readonly myEmail = () => (this.session.account()?.email ?? '').trim().toLowerCase();
  name(person: Person | null) { return person?.name || person?.email || '—'; }

  async ngOnInit() {
    rememberInvite(null);
    try {
      this.about.set(await this.sharing.invitation(this.token()));
    } catch {
      this.about.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  async accept() {
    const about = this.about();
    if (!about || this.busy()) return;
    this.busy.set(true);
    this.message.set('');
    try {
      const result = await this.sharing.acceptLink(this.token());
      // Straight to their records; the account menu switches back.
      this.sharing.switchTo({ ownerId: result.ownerId, name: about.from?.name ?? '', email: about.from?.email ?? '' });
    } catch (error) {
      this.message.set(error instanceof HttpErrorResponse ? error.error?.detail ?? 'We couldn’t accept the invitation. Try again.' : 'We couldn’t accept the invitation. Try again.');
      this.busy.set(false);
    }
  }
}
