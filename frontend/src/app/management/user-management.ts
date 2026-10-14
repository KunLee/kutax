import { Component, inject, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Icon } from '../core/icon';
import { AccountApi, ManagedUser, UserPage } from '../core/account-api';
import { Session } from '../core/session';
import { apiError } from './api-error';
import { TranslatePipe } from '../core/i18n';

/** A strong temporary password: 16 characters with each kind the policy asks for, from the browser's secure random source. */
type Panel = 'reset' | 'delete' | 'note' | 'admin';

export function generatePassword() {
  const sets = ['ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnopqrstuvwxyz', '23456789', '!@#$%*?-+'];
  const all = sets.join('');
  const random = (n: number) => crypto.getRandomValues(new Uint32Array(1))[0] % n;
  const chars = [...sets.map((set) => set[random(set.length)]), ...Array.from({ length: 12 }, () => all[random(all.length)])];
  for (let i = chars.length - 1; i > 0; i--) { const j = random(i + 1); [chars[i], chars[j]] = [chars[j], chars[i]]; }
  return chars.join('');
}

@Component({
  selector: 'app-user-management',
  imports: [TranslatePipe, DatePipe, FormsModule, Icon, RouterLink],
  templateUrl: './user-management.html',
  styleUrl: './management.scss',
})
export class UserManagement implements OnInit {
  private readonly api = inject(AccountApi);
  private readonly session = inject(Session);
  readonly result = signal<UserPage>({ items: [], total: 0, page: 1, pageSize: 25 });
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly noticeParams = signal<Record<string, string>>({});
  /** The row whose reset or delete step is open. */
  /** Which action is running ("<id>:enable", "<id>:reset", "<id>:delete" or "form"), so its button shows it. */
  readonly working = signal<string | null>(null);
  readonly panel = signal<{ id: string; kind: Panel } | null>(null);
  readonly generatePassword = generatePassword;
  search = '';
  email = '';
  displayName = '';
  temporaryPassword = generatePassword();
  resetPassword = '';
  note = '';

  async ngOnInit() {
    await this.load();
  }

  /** Your own account is changed in personal settings, never here. */
  isSelf(user: ManagedUser) {
    return user.id === this.session.account()?.id;
  }

  /** Accounts made before sign-up dates were recorded have none. */
  known(date: string | null | undefined) {
    return !!date && new Date(date).getFullYear() > 2000;
  }

  isOpen(user: ManagedUser, kind: Panel) {
    const open = this.panel();
    return open?.id === user.id && open.kind === kind;
  }

  openPanel(user: ManagedUser, kind: Panel) {
    if (this.isOpen(user, kind)) { this.closePanel(); return; }
    this.panel.set({ id: user.id, kind });
    this.resetPassword = generatePassword();
    this.note = user.note ?? '';
    this.error.set('');
    this.notice.set('');
  }

  closePanel() {
    this.panel.set(null);
    this.resetPassword = '';
  }

  async load(page = 1) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      this.result.set(await this.api.users(this.search.trim(), page));
    } catch (error) {
      this.error.set(apiError(error, 'We couldn’t load accounts. Try again.'));
    } finally {
      this.busy.set(false);
    }
  }

  async save() {
    this.working.set('form');
    await this.perform(
      () => this.api.createUser(this.email.trim(), this.displayName.trim(), this.temporaryPassword),
      'Account created. Share the temporary password privately; the user must change it at sign-in.',
    );
  }

  async saveNote(user: ManagedUser) {
    this.working.set(user.id + ':note');
    await this.perform(() => this.api.saveNote(user.id, this.note.trim()), 'Note saved.');
  }

  async setAdministrator(user: ManagedUser, administrator: boolean) {
    this.working.set(user.id + ':admin');
    await this.perform(
      () => this.api.setAdministrator(user.id, administrator),
      administrator ? '{email} is now an administrator.' : '{email} is no longer an administrator.',
      { email: user.email },
    );
  }

  async setEnabled(user: ManagedUser, enabled: boolean) {
    this.working.set(user.id + ':enable');
    await this.perform(
      () => this.api.setEnabled(user.id, enabled),
      enabled ? 'Account enabled. They can sign in again.' : 'Account disabled. They can’t sign in, and their records are kept.',
    );
  }

  async unlock(user: ManagedUser) {
    await this.perform(() => this.api.unlockUser(user.id), 'Account unlocked.');
  }

  async reset(user: ManagedUser) {
    const password = this.resetPassword;
    this.working.set(user.id + ':reset');
    await this.perform(
      () => this.api.resetPassword(user.id, password),
      'Password reset. Share the temporary password privately; the user must change it at sign-in.',
    );
  }

  async remove(user: ManagedUser) {
    this.working.set(user.id + ':delete');
    await this.perform(() => this.api.deleteUser(user.id), 'Account deleted.');
  }

  async copyPassword() {
    try {
      await navigator.clipboard.writeText(this.resetPassword);
      this.notice.set('Temporary password copied.');
    } catch {
      this.error.set('Clipboard unavailable. Copy the password shown.');
    }
  }

  private async perform(action: () => Promise<unknown>, message: string, params: Record<string, string> = {}) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await action();
      this.closePanel();
      this.email = '';
      this.displayName = '';
      this.temporaryPassword = generatePassword();
      this.notice.set(message);
      this.noticeParams.set(params);
      this.result.set(await this.api.users(this.search.trim(), this.result().page));
    } catch (error) {
      this.error.set(apiError(error, 'We couldn’t save the account. Try again.'));
    } finally {
      this.busy.set(false);
      this.working.set(null);
    }
  }
}
