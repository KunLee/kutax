import { computed, Component, effect, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { Session } from './core/session';
import { Theme } from './core/theme';
import { Toast } from './core/toast';
import { YearPicker } from './core/year-picker';
import { Icon } from './core/icon';
import { MachineArt } from './core/machine-art';
import { AccountSettings } from './management/account-settings';
import { Account } from './core/account-api';
import { I18n, TranslatePipe } from './core/i18n';
import { Invitation, pendingInvite, Person, rememberInvite, Sharing } from './core/sharing';
import { HttpErrorResponse } from '@angular/common/http';
import { ReturnTo } from './core/return-to';

@Component({
  selector: 'app-root',
  imports: [TranslatePipe, FormsModule, RouterOutlet, RouterLink, RouterLinkActive, Icon, MachineArt, AccountSettings, Toast, YearPicker],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  host: { '(document:keydown.escape)': 'menuOpen.set(false)', '(document:pointerdown)': 'closeMenuOutside($event)' },
})
export class App implements OnInit {
  readonly session = inject(Session);
  readonly theme = inject(Theme);
  readonly i18n = inject(I18n);
  /** Started here so it sees the page someone leaves for review. */
  private readonly returnTo = inject(ReturnTo);
  private readonly router = inject(Router);
  readonly account = this.session.account;
  readonly loading = this.session.loading;
  readonly busy = this.session.busy;
  readonly error = this.session.error;
  readonly notice = this.session.notice;
  readonly years = this.session.years;
  readonly menuOpen = signal(false);
  /** The Tax tab covers the summary and every tax pack, so it stays lit on a pack's page too. */
  readonly sharing = inject(Sharing);
  /** An invitation link opened while signed out: shown above the sign-in form, with its address filled in. */
  readonly invite = signal<{ token: string; from: Person | null; email: string; state: string } | null>(null);
  /** Any press outside the avatar and its menu closes the menu, wherever it lands on the page. */
  closeMenuOutside(event: Event) {
    if (this.menuOpen() && !(event.target instanceof Element && event.target.closest('.account'))) this.menuOpen.set(false);
  }
  readonly mode = signal<'signin' | 'register'>('signin');
  email = '';
  password = '';
  displayName = '';
  confirmPassword = '';
  acknowledged = false;
  readonly mismatch = signal(false);

  constructor() {
    // While anything is being read, keep the header indicator (and every page using the counts) current.
    let poll: ReturnType<typeof setTimeout> | undefined;
    effect(() => {
      const reading = this.session.counts()?.processing ?? 0;
      clearTimeout(poll);
      if (reading && this.account()) poll = setTimeout(() => void this.session.refreshCounts(), 4000);
    });
    this.router.events.pipe(filter((event) => event instanceof NavigationEnd)).subscribe(() => this.menuOpen.set(false));
    // Once signed in: who shares with you and any invitations; and back to an invitation link that brought you here.
    effect(() => {
      if (!this.account()) return;
      void this.sharing.load();
      const token = pendingInvite();
      if (token && !location.pathname.startsWith('/invite/')) void this.router.navigateByUrl('/invite/' + token);
    });
  }

  ngOnInit() {
    const link = /^\/invite\/([A-Za-z0-9_-]{20,100})$/.exec(location.pathname);
    if (link) rememberInvite(link[1]);
    const token = pendingInvite();
    if (token) {
      this.sharing.invitation(token).then((info) => {
        this.invite.set({ token, ...info });
        if (!this.email) this.email = info.email;
      }, () => rememberInvite(null));
    }
    return this.session.start();
  }

  async acceptInvitation(invitation: Invitation) {
    try {
      const result = await this.sharing.accept(invitation.id);
      this.sharing.switchTo({ ownerId: result.ownerId, name: invitation.person?.name ?? '', email: invitation.person?.email ?? '' });
    } catch (error) {
      this.session.error.set(error instanceof HttpErrorResponse ? error.error?.detail ?? 'We couldn’t accept the invitation. Try again.' : 'We couldn’t accept the invitation. Try again.');
    }
  }

  async declineInvitation(id: string) {
    try {
      await this.sharing.decline(id);
    } finally {
      await this.sharing.load();
    }
  }

  async signIn() {
    if (await this.session.signIn(this.email, this.password)) this.password = '';
  }

  switchMode(mode: 'signin' | 'register') {
    this.mode.set(mode);
    this.session.error.set('');
    this.password = '';
    this.confirmPassword = '';
    this.mismatch.set(false);
  }

  async register() {
    this.mismatch.set(this.password !== this.confirmPassword);
    if (this.mismatch()) return;
    if (await this.session.register(this.email, this.displayName, this.password, this.invite()?.token ?? null)) {
      this.password = '';
      this.confirmPassword = '';
      await this.router.navigateByUrl('/');
    }
  }

  async signOut() {
    this.menuOpen.set(false);
    if (await this.session.signOut()) await this.router.navigateByUrl('/');
  }

  accountUpdated(account: Account) {
    this.session.accountUpdated(account);
  }

  selectYear(value: string | number) {
    return this.session.selectYear(value);
  }
}
