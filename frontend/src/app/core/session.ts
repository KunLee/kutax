import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Account, AccountApi } from './account-api';
import { apiError } from '../management/api-error';

export interface DocumentCounts {
  total: number;
  needsReview: number;
  processing: number;
  confirmed: number;
  blocked: number;
  notClaimed: number;
}

/** Signed-in account state shared by the shell and routed pages. */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly api = inject(AccountApi);
  private readonly http = inject(HttpClient);
  readonly account = signal<Account | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly storageReady = signal<boolean | null>(null);
  readonly counts = signal<DocumentCounts | null>(null);
  readonly registrationEnabled = signal(false);
  /** Whether receipts are read automatically for this user: off, needs consent, or on. */
  readonly ai = signal<{ configured: boolean; consent: boolean } | null>(null);
  readonly aiState = computed<'unknown' | 'off' | 'consent' | 'on'>(() => {
    const ai = this.ai();
    return !ai ? 'unknown' : !ai.configured ? 'off' : !ai.consent ? 'consent' : 'on';
  });
  private markReady!: () => void;
  /** Resolves once the initial session check finishes, so route guards see the real account. */
  readonly ready = new Promise<void>((resolve) => (this.markReady = resolve));
  readonly firstName = computed(() => this.account()?.displayName.trim().split(/\s+/)[0] || '');
  readonly initials = computed(() => {
    const account = this.account();
    const source = account?.displayName.trim() || account?.email || '';
    const parts = source.split(/[\s@.]+/).filter(Boolean);
    return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '·';
  });

  /** Financial years with receipts or tax packs. */
  readonly dataYears = signal<number[]>([]);
  readonly years = computed(() => {
    const parts = new Intl.DateTimeFormat('en-AU', {
      timeZone: 'Australia/Perth',
      year: 'numeric',
      month: 'numeric',
    }).formatToParts(new Date());
    const calendarYear = Number(parts.find((p) => p.type === 'year')!.value);
    const month = Number(parts.find((p) => p.type === 'month')!.value);
    const currentYear = calendarYear - (month < 7 ? 1 : 0);
    // The current year and the three before it, plus any year with your records and the one you're on.
    const withData = new Set(this.dataYears());
    const years = new Set([...Array.from({ length: 4 }, (_, i) => currentYear - i), ...withData]);
    if (this.account()) years.add(this.account()!.financialYearStart);
    return [...years]
      .sort((a, b) => b - a)
      .map((start) => ({ start, label: yearLabel(start), hasRecords: withData.has(start), current: start === currentYear }));
  });

  async start() {
    try {
      await this.api.csrf();
      void this.api.registrationEnabled().then((enabled) => this.registrationEnabled.set(enabled), () => undefined);
      this.accountUpdated(await this.api.session());
    } catch (error) {
      if (!(error instanceof HttpErrorResponse && error.status === 401))
        this.error.set('We couldn’t connect to KuTax. Check that the app is running, then refresh.');
    } finally {
      this.loading.set(false);
      this.markReady();
    }
  }

  async signIn(email: string, password: string) {
    if (this.busy()) return false;
    this.busy.set(true);
    this.error.set('');
    try {
      this.accountUpdated(await this.api.login(email.trim(), password));
      return true;
    } catch (error) {
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 401
          ? 'We couldn’t sign you in. Check your details, or try again later if your account is locked.'
          : 'Sign-in is unavailable. Refresh the page and try again.',
      );
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  async register(email: string, displayName: string, password: string, inviteToken: string | null = null) {
    if (this.busy()) return false;
    this.busy.set(true);
    this.error.set('');
    try {
      this.accountUpdated(await this.api.register(email.trim(), displayName.trim(), password, inviteToken));
      this.notice.set('Welcome to KuTax. Your account is ready.');
      return true;
    } catch (error) {
      this.error.set(apiError(error, 'We couldn’t create your account. Check your details and try again.'));
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  async signOut() {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.logout();
      this.account.set(null);
      this.notice.set('');
      this.storageReady.set(null);
      this.counts.set(null);
      return true;
    } catch {
      this.error.set('We couldn’t sign you out. Please try again.');
      return false;
    } finally {
      this.busy.set(false);
    }
  }

  accountUpdated(account: Account) {
    this.account.set(account);
    if (!account.mustChangePassword) {
      void this.checkStorage();
      void this.refreshCounts();
      void this.refreshAi();
      void this.refreshYears();
    }
  }

  async refreshYears() {
    try {
      this.dataYears.set((await firstValueFrom(this.http.get<{ years: number[] }>('/api/tax-years'))).years);
    } catch {
      // The picker still offers recent years.
    }
  }

  async selectYear(value: string | number) {
    const account = this.account();
    if (!account || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      this.account.set(await this.api.selectYear(account.id, Number(value)));
      this.notice.set('Financial year saved.');
    } catch {
      this.error.set('We couldn’t save your financial year. Refresh and try again.');
    } finally {
      this.busy.set(false);
    }
  }

  async refreshCounts() {
    try {
      const page = await firstValueFrom(this.http.get<{ counts: DocumentCounts }>('/api/documents/?state=review'));
      this.counts.set(page.counts);
    } catch {
      // The badge is a convenience; pages report their own load failures.
    }
  }

  async refreshAi() {
    try {
      const privacy = await firstValueFrom(this.http.get<{ configured: boolean; externalAiConsent: boolean }>('/api/privacy'));
      this.ai.set({ configured: privacy.configured, consent: privacy.externalAiConsent });
    } catch {
      this.ai.set(null);
    }
  }

  private async checkStorage() {
    try {
      await this.api.ready();
      this.storageReady.set(true);
    } catch {
      this.storageReady.set(false);
    }
  }
}

export function yearLabel(start: number) {
  return `FY ${start}–${String((start + 1) % 100).padStart(2, '0')}`;
}
