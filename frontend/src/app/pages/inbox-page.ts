import { Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Session, yearLabel } from '../core/session';
import { Icon } from '../core/icon';
import { ReceiptInbox } from '../receipts/receipt-inbox';
import { AiStatus } from '../core/ai-status';
import { ReceiptList, ReceiptPage } from '../receipts/receipt-list';
import { TranslatePipe } from '../core/i18n';

@Component({
  selector: 'app-inbox-page',
  imports: [TranslatePipe, CurrencyPipe, DatePipe, RouterLink, Icon, ReceiptInbox, ReceiptList, AiStatus],
  template: `
    @if (session.account(); as user) {
      <div class="page">
        <section class="page-heading">
          <div>
            <span class="eyebrow">{{ (session.firstName() ? 'G’day, {name}' : 'G’day') | t: { name: session.firstName() } }}</span>
            <h1>{{ 'Your receipts have a happy place.' | t }}</h1>
            <p>{{ 'Send them in as you go. KuTax will read, sort and keep them ready for tax time.' | t }}</p>
          </div>
          <div class="pill">
            @if (reviewed() !== null) {
              <span class="ring" [style.--done]="reviewed() + '%'" aria-hidden="true">{{ reviewed() }}%</span>
              <span><small>{{ 'Receipts reviewed' | t }}</small><strong>{{ reviewed() === 100 ? ('All checked' | t) : reviewed() + ('% reviewed' | t) }}</strong></span>
            } @else {
              <span class="ring ring--empty" aria-hidden="true"><app-icon name="spark" [size]="18" /></span>
              <span><small>{{ label(user.financialYearStart) }}</small><strong>{{ 'Fresh start' | t }}</strong></span>
            }
          </div>
        </section>
        <p class="muted small year">{{ label(user.financialYearStart) }} · {{ user.financialYearFrom | date: ('d MMM yyyy' | t) }} – {{ user.financialYearTo | date: ('d MMM yyyy' | t) }}</p>

        @if (next(); as step) {
          <section class="card next-step" aria-labelledby="next-step-title">
            <div class="step-top">
              <span class="step-count">{{ step.step === 4 && step.done ? ('All done' | t) : ('Step {n} of 4' | t: { n: step.step }) }}</span>
              @if (deadline(); as d) {
                <span class="deadline" [class.deadline--soon]="d.days <= 14" [class.deadline--past]="d.days < 0" role="status">
                  <app-icon name="calendar" [size]="16" />
                  @if (d.days > 1) { {{ '{n} days left to lodge yourself (31 October)' | t: { n: d.days } }} }
                  @else if (d.days === 1) { {{ '1 day left to lodge yourself (31 October)' | t }} }
                  @else if (d.days === 0) { {{ 'Last day to lodge yourself: today' | t }} }
                  @else { {{ 'The 31 October date has passed: a registered tax agent can usually still lodge for you' | t }} }
                </span>
              }
            </div>
            <h2 id="next-step-title">{{ step.title | t: step.params }}</h2>
            <p>{{ step.detail | t: step.params }}</p>
            @if (step.link) {
              <a class="btn btn--primary next-go" [routerLink]="step.link" [fragment]="step.fragment">{{ step.action | t }}<app-icon name="arrow" [size]="18" /></a>
            } @else if (step.action) {
              <button class="btn btn--primary next-go" type="button" (click)="toUpload()">{{ step.action | t }}<app-icon name="arrow" [size]="18" /></button>
            } @else {
              <span class="reading-now"><span class="spinner" aria-hidden="true"></span>{{ 'Reading…' | t }}</span>
            }
          </section>
        }
        <app-receipt-inbox id="upload" (imported)="reload()" />
        <app-ai-status />

        <section class="stats card" [attr.aria-label]="'Receipt summary' | t">
          <div class="stat">
            <span class="thumb tone-blue"><app-icon name="archive" /></span>
            <span><small>{{ 'Documents uploaded' | t }}</small><strong>{{ page()?.counts?.total ?? '–' }}</strong></span>
          </div>
          <a class="stat stat--link" routerLink="/review/quick">
            <span class="thumb tone-orange"><app-icon name="clock" /></span>
            <span><small>{{ 'Ready for a quick check' | t }}</small><strong>{{ quick()?.ready ?? '–' }} <em>{{ quick()?.ready ? ('Review now' | t) : quick()?.needsCheck ? (((quick()!.needsCheck === 1 ? '{n} receipt needs a closer look' : '{n} receipts need a closer look') | t: { n: quick()!.needsCheck })) : ('All caught up' | t) }}</em></strong></span>
            <app-icon name="arrow" />
          </a>
          <a class="stat stat--link" routerLink="/summary">
            <span class="thumb tone-mint"><app-icon name="spark" /></span>
            <span><small>{{ 'Approved deductions · {year}' | t: { year: label(user.financialYearStart) } }}</small>
              <strong>{{ approved() === null ? '–' : (approved() | currency: ('AUD' | t) : 'symbol-narrow') }}</strong></span>
            <app-icon name="arrow" />
          </a>
        </section>

        <section class="card section" aria-labelledby="recent-title">
          <div class="section-heading">
            <div><span class="kicker">{{ 'Your receipts' | t }}</span><h2 id="recent-title">{{ 'Recently added' | t }}</h2></div>
            @if (page()?.counts?.processing) { <span class="chip chip--info">{{ '{n} being read' | t: { n: page()!.counts.processing } }}</span> }
          </div>
          @if (message()) { <p class="notice notice--error" role="alert">{{ message() | t }}</p> }
          @if (page(); as result) {
            @if (result.items.length) {
              <app-receipt-list [items]="result.items" />
              @if (result.total > result.pageSize) {
                <div class="row pager">
                  <button class="btn btn--small" type="button" (click)="load(result.page - 1)" [disabled]="result.page <= 1">{{ 'Previous' | t }}</button>
                  <span class="muted small">{{ 'Page {n} of {total}' | t: { n: result.page, total: pages() } }}</span>
                  <button class="btn btn--small" type="button" (click)="load(result.page + 1)" [disabled]="result.page >= pages()">{{ 'Next' | t }}</button>
                </div>
              }
            } @else {
              <p class="empty"><strong>{{ 'No receipts yet' | t }}</strong>{{ 'Add your first receipt above — a photo is fine.' | t }}</p>
            }
          }
        </section>
      </div>
    }
  `,
  styles: `
    .year { margin: -16px 0 20px; }
    .pill { align-items: center; background: var(--white); border: 1px solid var(--line); border-radius: 18px; display: flex; flex: none; gap: 12px; padding: 9px 16px 9px 9px; }
    .pill small, .pill strong { display: block; }
    .pill small { color: var(--ink-soft); font-size: 12px; }
    .ring { --done: 0%; align-items: center; background: conic-gradient(var(--purple) var(--done), var(--purple-soft) 0); border-radius: 50%;
      display: flex; font-family: var(--mono); font-size: 11px; height: 48px; isolation: isolate; justify-content: center; position: relative; width: 48px; }
    .ring::after { background: var(--white); border-radius: 50%; content: ''; inset: 5px; position: absolute; z-index: -1; }
    .ring--empty { --done: 0%; color: var(--purple); }
    .stats { display: grid; gap: 4px; grid-template-columns: 1fr 1.2fr 1.2fr; margin-top: 24px; padding: 10px; }
    .stat { align-items: center; border-radius: 14px; color: inherit; display: flex; font-weight: 400; gap: 13px; min-height: 64px; padding: 10px 14px; text-decoration: none; }
    .stat > span:nth-child(2) { flex: 1; min-width: 0; }
    .stat small, .stat strong { display: block; }
    .stat small { color: var(--ink-soft); font-size: 12px; margin-bottom: 2px; }
    .stat strong { font-size: 18px; }
    .stat em { color: var(--purple-ink); font-size: 12px; font-style: normal; margin-left: 6px; }
    .stat--link:hover { background: var(--orange-soft); }
    .pager { justify-content: center; margin-top: 16px; }
    .next-step { background: var(--yellow); border: 2px solid var(--ink); box-shadow: 6px 6px 0 var(--ink); display: grid; gap: 8px; justify-items: start; margin-bottom: 18px; padding: 22px 24px; }
    .next-step h2 { font-size: clamp(22px, 3.4vw, 30px); margin: 0; }
    .next-step p { margin: 0; max-width: 60ch; }
    .step-top { align-items: center; display: flex; flex-wrap: wrap; gap: 8px 12px; justify-content: space-between; width: 100%; }
    .deadline { align-items: center; background: var(--white); border: 2px solid var(--ink); border-radius: 999px; display: inline-flex; font-size: 12px; font-weight: 800; gap: 6px; padding: 4px 10px; }
    .deadline--soon { background: var(--orange-soft); }
    .deadline--past { background: var(--orange); }
    .step-count { font-family: var(--mono); font-size: 12px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; }
    .next-go { font-size: 16px; margin-top: 6px; min-height: 48px; padding: 0 22px; }
    .reading-now { align-items: center; display: flex; gap: 8px; font-weight: 700; }
    .tick { align-items: center; background: var(--white); border: 2px solid var(--ink); border-radius: 50%; display: flex; flex: none; font-size: 12px; font-weight: 800; height: 28px; justify-content: center; width: 28px; }
    .done .tick { background: var(--mint); }
    @media (max-width: 700px) {
      .pill { margin-top: 16px; width: fit-content; }
      .year { margin-top: 0; }
      .stats { grid-template-columns: 1fr; }
      .stat + .stat { border-top: 1px solid var(--line); border-radius: 0; }
    }
  `,
})
export class InboxPage implements OnDestroy {
  readonly session = inject(Session);
  /**
   * People lodging their own return have until 31 October after the financial year ends. Shown from 1 July until a
   * month after that date, for the year chosen at the top.
   */
  readonly deadline = computed(() => {
    const start = this.session.account()?.financialYearStart;
    if (!start) return null;
    const today = new Date();
    const day = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate());
    const opens = Date.UTC(start + 1, 6, 1);
    const due = Date.UTC(start + 1, 9, 31);
    const days = Math.round((due - day) / 86_400_000);
    return day < opens || days < -30 ? null : { days };
  });
  private readonly http = inject(HttpClient);
  readonly page = signal<ReceiptPage | null>(null);
  readonly approved = signal<number | null>(null);
  readonly packs = signal<number | null>(null);
  /** The same numbers as the quick review page for this year: ready to approve together, and needing a closer look. */
  readonly quick = signal<{ ready: number; needsCheck: number } | null>(null);
  /** Work left in the tax summary for this year: claims to approve or fix, and receipt exceptions. */
  readonly todos = signal<number | null>(null);
  /** The one thing to do next, in order: add receipts, approve or check them, finish the summary, prepare the tax pack. */
  readonly next = computed(() => {
    const counts = this.page()?.counts;
    const quick = this.quick();
    if (!counts) return null;
    const year = yearLabel(this.session.account()?.financialYearStart ?? 0);
    const firstToCheck = this.page()?.items.find((i) => i.reviewState === 'NeedsReview')?.id;
    type Step = { step: number; title: string; detail: string; action?: string; link?: string | string[]; fragment?: string; params?: Record<string, string | number>; done?: boolean };
    const steps: (Step | false)[] = [
      counts.total === 0 && { step: 1, title: 'Add your first receipt', detail: 'Take a photo or choose a PDF. KuTax reads the details for you.', action: 'Add receipts' },
      !!quick?.ready && { step: 2, title: quick.ready === 1 ? '1 receipt is ready to approve' : '{n} receipts are ready to approve together', detail: 'KuTax read them clearly. Check the kind of expense and approve them in one go.', action: 'Approve them', link: '/review/quick', params: { n: quick.ready } },
      counts.needsReview > 0 && { step: 2, title: counts.needsReview === 1 ? '1 receipt needs you to check it' : '{n} receipts need you to check them', detail: 'Compare what KuTax read with the original, choose the kind of expense and confirm.', action: 'Start checking', link: firstToCheck ? ['/review', firstToCheck] : '/review', params: { n: counts.needsReview } },
      counts.processing > 0 && { step: 2, title: counts.processing === 1 ? 'Reading 1 receipt' : 'Reading {n} receipts', detail: 'This takes a moment. You can keep adding receipts.', params: { n: counts.processing } },
      !!this.todos() && { step: 3, title: this.todos() === 1 ? '1 thing to finish in your tax summary' : '{n} things to finish in your tax summary', detail: 'Approve what’s ready and fix what’s missing.', action: 'Finish them', link: '/summary', params: { n: this.todos()! } },
      (this.approved() ?? 0) > 0 && this.packs() === 0 && { step: 4, title: 'Your deductions for {year} are ready', detail: 'Prepare your tax pack: it shows exactly what to enter in myTax.', action: 'Prepare tax pack', link: '/summary', fragment: 'tax-pack', params: { year } },
      (this.packs() ?? 0) > 0 && { step: 4, done: true, title: 'All done for {year}', detail: 'Your tax pack is ready. Enter the figures in myTax, or add more receipts any time.', action: 'Open tax pack', link: '/summary', fragment: 'tax-pack', params: { year } },
    ];
    return steps.find((s): s is Step => !!s) ?? null;
  });
  toUpload() {
    document.getElementById('upload')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
  readonly message = signal('');
  readonly pages = computed(() => Math.max(1, Math.ceil((this.page()?.total ?? 0) / (this.page()?.pageSize ?? 50))));
  readonly reviewed = computed(() => {
    const counts = this.page()?.counts;
    const readable = counts ? counts.total - counts.blocked : 0;
    return counts && readable > 0 ? Math.round(((counts.confirmed + (counts.notClaimed ?? 0)) / readable) * 100) : null;
  });
  readonly label = yearLabel;
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    void this.load(1);
    effect(() => {
      const year = this.session.account()?.financialYearStart;
      if (year) {
        void this.loadApproved(year);
        void this.loadPacks(year);
        void this.loadQuick(year);
      }
    });
  }

  ngOnDestroy() {
    clearTimeout(this.timer);
  }

  reload() {
    void this.load(1);
    void this.session.refreshCounts();
  }

  async load(page: number) {
    try {
      const result = await firstValueFrom(this.http.get<ReceiptPage>(`/api/documents/?page=${page}`));
      this.page.set(result);
      this.session.counts.set(result.counts);
      const year = this.session.account()?.financialYearStart;
      // The year effect does the first load; this keeps it current as new receipts are read.
      if (year && page === 1 && this.quick() !== null) void this.loadQuick(year);
      this.message.set('');
      // Refresh until newly added receipts have been checked and read.
      clearTimeout(this.timer);
      if (result.counts.processing) this.timer = setTimeout(() => void this.load(result.page), 4000);
    } catch {
      this.message.set('We couldn’t load your receipts. Refresh to try again.');
    }
  }

  private async loadQuick(year: number) {
    try {
      const result = await firstValueFrom(this.http.get<{ items: unknown[]; needsCheck: number }>(`/api/quick-review/${year}`));
      this.quick.set({ ready: result.items.length, needsCheck: result.needsCheck });
    } catch {
      this.quick.set(null);
    }
  }

  private async loadPacks(year: number) {
    try {
      const packs = await firstValueFrom(this.http.get<unknown[]>(`/api/tax-years/${year}/exports`));
      if (year === this.session.account()?.financialYearStart) this.packs.set(packs.length);
    } catch {
      this.packs.set(null);
    }
  }

  private async loadApproved(year: number) {
    try {
      const report = await firstValueFrom(this.http.get<{ approvedTotal: number; rows: { status: string }[]; issues: unknown[] }>(`/api/tax-years/${year}/claims`));
      if (year !== this.session.account()?.financialYearStart) return;
      this.approved.set(report.approvedTotal);
      const rows = report.rows ?? [];
      this.todos.set((rows.some((r) => r.status === 'Candidate') ? 1 : 0) + rows.filter((r) => r.status === 'NeedsReview' || r.status === 'NeedsEvidence').length + (report.issues?.length ?? 0));
    } catch {
      this.approved.set(null);
    }
  }
}
