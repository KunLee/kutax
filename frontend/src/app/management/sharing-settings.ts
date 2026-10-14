import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Icon } from '../core/icon';
import { TranslatePipe } from '../core/i18n';
import { Activity, InviteResult, Outgoing, Sharing, Suggestion } from '../core/sharing';
import { Toasts } from '../core/toast';

/** What each recorded action means, in plain words. */
const actions: Record<string, string> = {
  ReceiptConfirmed: 'Confirmed a receipt', ReceiptEdited: 'Edited a receipt', ReceiptNotClaimed: 'Skipped a receipt', ReceiptRestored: 'Restored a receipt',
  AllocationsReplaced: 'Split a receipt into expenses', ClaimApproved: 'Approved a claim', EvidenceChanged: 'Changed supporting documents',
  ExportSnapshotApproved: 'Prepared a tax pack', DocumentDeleted: 'Deleted a file', AiConsentGranted: 'Turned on AI reading', AiConsentRevoked: 'Turned off AI reading',
  ProcessingRequested: 'Asked AI to read a receipt again', ProcessingTakenOver: 'Entered a receipt by hand', MyTaxProgressChanged: 'Marked myTax progress',
  SharingAccepted: 'Accepted your invitation', SharingDeclined: 'Declined your invitation', SharingEnded: 'Ended sharing',
};

/**
 * Settings → Sharing: invite someone to act fully on your tax records, see who can, and what they've done; and the
 * people who shared theirs with you.
 */
@Component({
  selector: 'app-sharing-settings',
  imports: [TranslatePipe, DatePipe, FormsModule, Icon],
  template: `
    <section class="card section" aria-labelledby="sharing-title">
      <span class="kicker">{{ 'Sharing' | t }}</span>
      <h2 id="sharing-title">{{ 'Share your tax records' | t }}</h2>
      <p class="muted small">{{ 'Someone you invite can do everything you can with your tax records: see and download them, add receipts, review, approve and prepare tax packs. They switch between yours and their own from the account menu. You can see what they do and stop sharing at any time.' | t }}</p>
      <form class="invite" (ngSubmit)="invite()">
        <label class="sr-only" for="share-email">{{ 'Their email address' | t }}</label>
        <div class="combo">
          <input class="input" id="share-email" name="email" type="email" inputmode="email" autocomplete="off" required maxlength="254" [placeholder]="'Their email address' | t" [(ngModel)]="email" [disabled]="busy()"
            role="combobox" aria-autocomplete="list" aria-controls="share-suggestions" [attr.aria-expanded]="suggestions().length > 0" [attr.aria-activedescendant]="active() >= 0 ? 'share-suggestion-' + active() : null"
            (ngModelChange)="typed($event)" (focus)="typed(email)" (blur)="close()" (keydown)="key($event)" />
          @if (suggestions().length) {
            <ul class="suggestions" id="share-suggestions" role="listbox" [attr.aria-label]="'Suggestions' | t">
              @for (person of suggestions(); track person.email; let i = $index) {
                <li [id]="'share-suggestion-' + i" role="option" [attr.aria-selected]="i === active()" [class.on]="i === active()" (mousedown)="$event.preventDefault(); pick(person)">
                  <span class="initial" aria-hidden="true">{{ (person.name || person.email).charAt(0).toUpperCase() }}</span>
                  <span class="who"><strong>{{ person.name || person.email }}</strong>@if (person.name) { <small>{{ person.email }}</small> }</span>
                  @if (person.known) { <span class="chip">{{ 'Shared before' | t }}</span> }
                </li>
              }
            </ul>
          }
        </div>
        <button class="btn btn--primary" type="submit" [disabled]="busy() || !email.trim()" [attr.aria-busy]="working() === 'invite'"><app-icon name="users" [size]="18" />{{ 'Invite' | t }}</button>
      </form>
      @if (result(); as sent) {
        <div class="sent" role="status">
          <p class="small">@if (sent.emailed) { <strong>{{ 'Invitation emailed to {email}.' | t: { email: sentTo() } }}</strong> {{ 'It’s valid for 7 days. You can also send them this link:' | t }} }
            @else { <strong>{{ 'Email isn’t set up on this server, so send them this link yourself.' | t }}</strong> {{ 'It’s valid for 7 days and only works for {email}.' | t: { email: sentTo() } }} }</p>
          <div class="row"><input class="input" readonly [value]="sent.link" (focus)="$any($event.target).select()" [attr.aria-label]="'Invitation link' | t" /><button class="btn btn--small" type="button" (click)="copy(sent.link)">{{ 'Copy' | t }}</button></div>
        </div>
      }
      @if (message()) { <p class="notice notice--error" role="alert">{{ message() | t }}</p> }
      @if (state()?.outgoing?.length) {
        <ul class="people">
          @for (person of state()!.outgoing; track person.id) {
            <li>
              <span class="who"><strong>{{ person.person?.name || person.email }}</strong><small>{{ person.email }}</small></span>
              <span class="chip" [class.chip--done]="person.status === 'Active'" [class.chip--review]="person.status === 'Expired'">{{ statusLabel(person) | t }}</span>
              <span class="row actions">
                @if (person.status !== 'Active') { <button class="btn btn--small" type="button" (click)="resend(person)" [disabled]="busy()" [attr.aria-busy]="working() === person.id + ':resend'">{{ 'New link' | t }}</button> }
                @if (confirming() === person.id) {
                  <button class="btn btn--small btn--danger" type="button" (click)="end(person)" [disabled]="busy()" [attr.aria-busy]="working() === person.id + ':end'">{{ (person.status === 'Active' ? 'Yes, stop sharing' : 'Yes, cancel it') | t }}</button>
                  <button class="btn btn--small btn--quiet" type="button" (click)="confirming.set(null)">{{ 'Keep' | t }}</button>
                } @else {
                  <button class="btn btn--small btn--quiet" type="button" (click)="confirming.set(person.id)" [disabled]="busy()">{{ (person.status === 'Active' ? 'Stop sharing' : 'Cancel invitation') | t }}</button>
                }
              </span>
            </li>
          }
        </ul>
      }
    </section>

    @if (state()?.incoming?.length) {
      <section class="card section" aria-labelledby="shared-with-me-title">
        <span class="kicker">{{ 'Sharing' | t }}</span>
        <h2 id="shared-with-me-title">{{ 'Shared with you' | t }}</h2>
        <ul class="people">
          @for (shared of state()!.incoming; track shared.id) {
            <li>
              <span class="who"><strong>{{ shared.person?.name || shared.person?.email }}</strong><small>{{ shared.person?.email }}</small></span>
              <span class="row actions">
                <button class="btn btn--small btn--primary" type="button" (click)="open(shared.ownerId, shared.person?.name ?? '', shared.person?.email ?? '')">{{ 'Open their records' | t }}</button>
                @if (confirming() === shared.id) {
                  <button class="btn btn--small btn--danger" type="button" (click)="leave(shared.id)" [disabled]="busy()">{{ 'Yes, stop helping' | t }}</button>
                  <button class="btn btn--small btn--quiet" type="button" (click)="confirming.set(null)">{{ 'Keep' | t }}</button>
                } @else {
                  <button class="btn btn--small btn--quiet" type="button" (click)="confirming.set(shared.id)">{{ 'Stop helping' | t }}</button>
                }
              </span>
            </li>
          }
        </ul>
      </section>
    }

    @if (activity().length) {
      <section class="card section" aria-labelledby="sharing-activity-title">
        <span class="kicker">{{ 'Sharing' | t }}</span>
        <h2 id="sharing-activity-title">{{ 'What people helping you did' | t }}</h2>
        <ul class="log">
          @for (entry of activity(); track $index) {
            <li><span>{{ entry.createdAt | date: ('d MMM yyyy, h:mm a' | t) }}</span><strong>{{ entry.person?.name || entry.person?.email }}</strong><span>{{ actionLabel(entry.action) | t }}</span></li>
          }
        </ul>
      </section>
    }
  `,
  styles: `
    .invite { display: flex; gap: 8px; margin-top: 12px; }
    .combo { flex: 1; min-width: 0; position: relative; }
    .combo input { width: 100%; }
    .suggestions { background: var(--white); border: 2px solid var(--ink); border-radius: 12px; box-shadow: 4px 4px 0 var(--ink); left: 0; list-style: none; margin: 6px 0 0; max-height: 300px; overflow-y: auto; padding: 4px; position: absolute; right: 0; top: 100%; z-index: 20; }
    .suggestions li { align-items: center; border-radius: 8px; color: var(--ink); cursor: pointer; display: flex; gap: 10px; padding: 8px 10px; }
    .suggestions li.on, .suggestions li:hover { background: var(--yellow-soft); }
    .suggestions .who { flex: 1; min-width: 0; }
    .initial { align-items: center; background: var(--mint); border: 1px solid var(--ink); border-radius: 50%; display: flex; flex: none; font-size: 13px; font-weight: 800; height: 30px; justify-content: center; width: 30px; }
    .sent { background: var(--mint-soft); border-radius: 12px; display: grid; gap: 8px; margin-top: 12px; padding: 12px; }
    .sent p { margin: 0; }
    .sent input { flex: 1; font-family: var(--mono); font-size: 12px; min-width: 0; }
    .people, .log { display: grid; gap: 8px; list-style: none; margin: 14px 0 0; padding: 0; }
    .people li { align-items: center; border: 1px solid var(--line); border-radius: 14px; display: grid; gap: 8px 12px; grid-template-columns: minmax(0, 1fr) auto auto; padding: 10px 12px; }
    .who strong, .who small { display: block; overflow-wrap: anywhere; }
    .who small { color: var(--ink-soft); font-size: 12px; }
    .actions { flex-wrap: wrap; justify-content: flex-end; }
    .log li { border-bottom: 1px dashed var(--line); display: grid; font-size: 13px; gap: 10px; grid-template-columns: 150px 160px minmax(0, 1fr); padding: 6px 0; }
    @media (max-width: 700px) {
      .people li { grid-template-columns: minmax(0, 1fr) auto; }
      .people .actions { grid-column: 1 / -1; justify-content: flex-start; }
      .log li { gap: 2px; grid-template-columns: 1fr; }
    }
  `,
})
export class SharingSettings implements OnInit {
  private readonly sharing = inject(Sharing);
  private readonly toasts = inject(Toasts);
  readonly state = this.sharing.state;
  readonly activity = signal<Activity[]>([]);
  readonly result = signal<InviteResult | null>(null);
  readonly sentTo = signal('');
  readonly message = signal('');
  readonly working = signal<string | null>(null);
  readonly busy = computed(() => this.working() !== null);
  readonly confirming = signal<string | null>(null);
  email = '';
  readonly suggestions = signal<Suggestion[]>([]);
  readonly active = signal(-1);
  private lookup: ReturnType<typeof setTimeout> | undefined;

  async ngOnInit() {
    await this.sharing.load();
    try { this.activity.set((await this.sharing.activity()).slice(0, 30)); } catch { /* optional */ }
  }

  /** Suggests people as you type (a moment after you stop, so not every key asks the server). */
  typed(value: string) {
    clearTimeout(this.lookup);
    const query = value.trim();
    this.lookup = setTimeout(async () => {
      try {
        const people = await this.sharing.people(query);
        if (this.email.trim() !== query) return;
        this.suggestions.set(people.filter((p) => p.email !== query.toLowerCase()));
        this.active.set(-1);
      } catch { this.suggestions.set([]); }
    }, query ? 200 : 0);
  }

  pick(person: Suggestion) {
    this.email = person.email;
    this.close();
  }

  close() {
    clearTimeout(this.lookup);
    this.suggestions.set([]);
    this.active.set(-1);
  }

  key(event: KeyboardEvent) {
    const count = this.suggestions().length;
    if (!count) return;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      this.active.update((i) => (event.key === 'ArrowDown' ? (i + 1) % count : i <= 0 ? count - 1 : i - 1));
    } else if (event.key === 'Enter' && this.active() >= 0) {
      event.preventDefault();
      this.pick(this.suggestions()[this.active()]);
    } else if (event.key === 'Escape') {
      this.close();
    }
  }

  statusLabel(person: Outgoing) {
    return person.status === 'Active' ? 'Has access' : person.status === 'Expired' ? 'Invitation expired' : 'Waiting to accept';
  }

  actionLabel(action: string) {
    return actions[action] ?? action;
  }

  async invite() {
    const email = this.email.trim();
    await this.run('invite', async () => {
      this.result.set(await this.sharing.invite(email));
      this.sentTo.set(email);
      this.email = '';
      this.close();
    });
  }

  async resend(person: Outgoing) {
    await this.run(person.id + ':resend', async () => {
      this.result.set(await this.sharing.resend(person.id));
      this.sentTo.set(person.email);
    });
  }

  async end(person: Outgoing) {
    await this.run(person.id + ':end', async () => {
      await this.sharing.end(person.id);
      this.toasts.show({ text: person.status === 'Active' ? 'Sharing stopped.' : 'Invitation cancelled.' });
    });
  }

  async leave(id: string) {
    await this.run(id + ':end', async () => {
      await this.sharing.end(id);
      this.toasts.show({ text: 'You no longer help with these tax records.' });
    });
  }

  open(ownerId: string, name: string, email: string) {
    this.sharing.switchTo({ ownerId, name, email });
  }

  async copy(link: string) {
    try {
      await navigator.clipboard.writeText(link);
      this.toasts.show({ text: 'Link copied.' });
    } catch {
      this.message.set('Clipboard unavailable. Copy the link shown.');
    }
  }

  private async run(name: string, action: () => Promise<void>) {
    if (this.busy()) return;
    this.working.set(name);
    this.message.set('');
    try {
      await action();
      this.confirming.set(null);
      await this.sharing.load();
    } catch (error) {
      this.message.set(error instanceof HttpErrorResponse ? error.error?.detail ?? 'That didn’t work. Try again.' : 'That didn’t work. Try again.');
    } finally {
      this.working.set(null);
    }
  }
}
