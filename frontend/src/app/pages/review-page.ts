import { Component, computed, effect, inject, input, OnDestroy, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Session } from '../core/session';
import { Icon } from '../core/icon';
import { ReceiptList, ReceiptPage } from '../receipts/receipt-list';
import { ReturnTo } from '../core/return-to';
import { SelectMenu } from '../core/select-menu';
import { skipReasons } from '../core/skip';
import { yearLabel } from '../core/session';
import { ReceiptReview } from '../receipts/receipt-review';
import { AiStatus } from '../core/ai-status';
import { Toasts } from '../core/toast';
import { I18n, TranslatePipe } from '../core/i18n';
import { categoryLabel } from '../core/categories';

/** The chosen list survives a refresh, so "Next receipt" keeps following it. */
const filterKey = 'kutax.review.filter';
const sortKey = 'kutax.review.sort';
type Sort = 'added' | 'date' | 'date-asc' | 'merchant' | 'amount';
const yearKey = 'kutax.review.thisYear';
function savedFlag(key: string) {
  try {
    return sessionStorage.getItem(key) === '1';
  } catch {
    return false;
  }
}
function saveFlag(key: string, on: boolean) {
  try {
    sessionStorage.setItem(key, on ? '1' : '0');
  } catch {
    // Remembered for this page only.
  }
}
function savedSort(): Sort {
  try {
    const saved = sessionStorage.getItem(sortKey);
    if (saved && ['added', 'date', 'date-asc', 'merchant', 'amount'].includes(saved)) return saved as Sort;
  } catch {
    // Storage unavailable: newest first.
  }
  return 'added';
}
/** A narrower view opened from a report: one month, one merchant or one kind of expense (within a financial year). */
interface Focus { year?: number; month?: string; merchant?: string; category?: string; }
const focusKey = 'kutax.review.focus';
function readFocus(query: Record<string, string | undefined>, withinReceipts: boolean): Focus | null {
  const focus: Focus = {};
  if (query['year'] && /^\d{4}$/.test(query['year'])) focus.year = Number(query['year']);
  if (query['month'] && /^\d{4}-\d{2}$/.test(query['month'])) focus.month = query['month'];
  if (query['merchant']) focus.merchant = query['merchant'].slice(0, 200);
  if (query['category'] && /^[A-Za-z]{1,40}$/.test(query['category'])) focus.category = query['category'];
  if (focus.month || focus.merchant || focus.category || focus.year) {
    try { sessionStorage.setItem(focusKey, JSON.stringify(focus)); } catch { /* this visit only */ }
    return focus;
  }
  // Moving between the narrowed list and its receipts keeps the view; arriving from elsewhere (the menu) shows everything.
  try {
    if (!withinReceipts) { sessionStorage.removeItem(focusKey); return null; }
    const saved = sessionStorage.getItem(focusKey);
    return saved ? (JSON.parse(saved) as Focus) : null;
  } catch { return null; }
}

function savedFilter(): 'review' | 'all' | 'skipped' {
  try {
    const saved = sessionStorage.getItem(filterKey);
    if (saved === 'all' || saved === 'skipped' || saved === 'review') return saved;
  } catch {
    // Storage unavailable: start with what needs review.
  }
  return 'review';
}

@Component({
  selector: 'app-review-page',
  imports: [TranslatePipe, RouterLink, Icon, ReceiptList, ReceiptReview, AiStatus, SelectMenu],
  template: `
    <div class="page wide" [class.has-detail]="id()" [class.list-hidden]="id() && !listOpen()">
      <section class="page-heading heading">
        <div>
          <span class="eyebrow">{{ 'Review queue' | t }}</span>
          <h1>{{ waiting() === 0 ? (reading() ? ('Reading your receipts…' | t) : ('All caught up' | t)) : waiting() === 1 ? ('One quick check' | t) : ('{n} quick checks' | t: { n: waiting() ?? '…' }) }}</h1>
          <p>{{ 'We’ve done the reading. Approve the clear ones together, and check the rest against the original.' | t }}</p>
        </div>
        @if (waiting()) { <a class="btn btn--primary quick" routerLink="/review/quick"><app-icon name="spark" [size]="18" />{{ 'Quick review' | t }}</a> }
        @if (session.counts(); as counts) {
          <span class="progress"><strong>{{ counts.confirmed + counts.notClaimed }}</strong> {{ 'of {total} done' | t: { total: counts.total - counts.blocked } }}
            <small>{{ '{n} confirmed' | t: { n: counts.confirmed } }}@if (counts.notClaimed) { · {{ '{n} skipped' | t: { n: counts.notClaimed } }} }</small></span>
        }
      </section>
      <div class="layout">
        @if (id()) {
          <div class="row detail-nav">
              <button class="btn btn--quiet back" type="button" (click)="returnTo.back()"><app-icon name="back" [size]="18" />{{ returnTo.label() | t }}</button>
              <button class="btn btn--quiet btn--small list-toggle" type="button" (click)="listOpen.set(!listOpen())" [attr.aria-pressed]="listOpen()">
                <app-icon [name]="listOpen() ? 'back' : 'more'" [size]="16" />{{ listOpen() ? ('Hide list' | t) : ('Show list' | t) }}</button>
              <span class="spacer"></span>
              @if (next(); as nextId) { <a class="btn btn--small" [routerLink]="['/review', nextId]">{{ 'Next receipt' | t }}<app-icon name="arrow" [size]="16" /></a> }
          </div>
        }
        <aside class="card queue" [attr.aria-label]="'Receipts' | t">
          <div class="panel-cap" aria-hidden="true"><span>{{ '01 / Queue' | t }}</span><small>{{ '{n} waiting' | t: { n: waiting() ?? 0 } }}</small></div>
          @if (focus(); as f) {
            <div class="focus" role="status">
              <span><small>{{ 'Showing' | t }}</small><strong>{{ focusLabel() }}</strong></span>
              <button class="btn btn--small btn--quiet" type="button" (click)="clearFocus()" [attr.aria-label]="'Show all receipts' | t"><app-icon name="close" [size]="16" />{{ 'Show all' | t }}</button>
            </div>
          }
          <div class="segmented" role="group" [attr.aria-label]="'Show' | t">
            <button type="button" [attr.aria-pressed]="filter() === 'review'" (click)="setFilter('review')">{{ 'Needs your eye' | t }}</button>
            <button type="button" [attr.aria-pressed]="filter() === 'all'" (click)="setFilter('all')">{{ 'All' | t }}</button>
            <button type="button" [attr.aria-pressed]="filter() === 'skipped'" (click)="setFilter('skipped')">{{ 'Skipped' | t }}@if (session.counts()?.notClaimed; as n) { <span class="count">{{ n }}</span> }</button>
          </div>
          <div class="tools">
            <label class="sr-only" for="queue-search">{{ 'Search receipts' | t }}</label>
            <input class="input" id="queue-search" type="search" maxlength="100" autocomplete="off" [placeholder]="'Merchant, amount or date' | t" [value]="search()" (input)="typed($any($event.target).value)" />
            <app-select-menu label="Sort" [options]="sortOptions" [(value)]="sort" />
          </div>
          <div class="tools tools--second">
            @if (session.account(); as user) { @if (!focus()) {
              <button class="toggle" type="button" [attr.aria-pressed]="thisYear()" (click)="setThisYear(!thisYear())">
                <span class="box" aria-hidden="true">@if (thisYear()) { <app-icon name="check" [size]="14" /> }</span>{{ 'Only {year}' | t: { year: yearName(user.financialYearStart) } }}</button>
            } }
            <span class="spacer"></span>
            <button class="btn btn--small" type="button" (click)="setSelecting(!selecting())" [attr.aria-pressed]="selecting()">{{ selecting() ? ('Done' | t) : ('Select' | t) }}</button>
          </div>
          @if (message()) { <p class="notice notice--error" role="alert">{{ message() | t }}</p> }
          <app-ai-status [compact]="true" />
          @if (session.aiState() === 'on' && waiting()) {
            <button class="btn btn--block reread" type="button" (click)="readAgain()" [disabled]="busy()"><app-icon name="spark" [size]="18" />{{ 'Read waiting receipts with AI' | t }}</button>
          }
          @if (notice()) { <p class="notice notice--success" role="status">{{ notice() | t }}</p> }
          @if (reading(); as count) {
            <p class="notice reading" role="status"><span class="pulse" aria-hidden="true"></span>{{ (count === 1 ? 'Checking and reading {n} receipt. It appears here in a moment.' : 'Checking and reading {n} receipts. They appear here in a moment.') | t: { n: count } }}</p>
          }
          @if (page(); as result) {
            @if (result.items.length) {
              <app-receipt-list [items]="result.items" [activeId]="id() ?? null" [selecting]="selecting()" [selected]="selected()" (toggled)="toggle($event)" />
              @if (selecting()) {
                <div class="batch" role="group" [attr.aria-label]="'Selected receipts' | t">
                  <div class="batch-head"><strong>{{ '{n} selected' | t: { n: selected().size } }}</strong>
                    <button class="btn btn--small btn--quiet" type="button" (click)="selectAll()">{{ allSelected() ? ('Clear selection' | t) : ('Select all on this page' | t) }}</button></div>
                  @if (filter() === 'skipped') {
                    <button class="btn btn--small btn--primary" type="button" (click)="batch(false)" [disabled]="busy() || !selected().size" [attr.aria-busy]="busy()">{{ 'Restore {n}' | t: { n: selected().size } }}</button>
                  } @else {
                    <span class="small">{{ 'Skip as — not for my tax return:' | t }}</span>
                    <div class="row">
                      @for (reason of skipReasons; track reason.key) {
                        <button class="btn btn--small" type="button" (click)="batch(true, reason.key)" [disabled]="busy() || !selected().size" [attr.aria-busy]="busy() && batchReason() === reason.key">{{ reason.label | t }}</button>
                      }
                    </div>
                  }
                </div>
              }
              @if (result.total > result.items.length) { <p class="muted small more">{{ 'Showing the latest {n} of {total}.' | t: { n: result.items.length, total: result.total } }}</p> }
            } @else if (term()) {
              <p class="empty"><strong>{{ 'No receipts match “{q}”' | t: { q: term() } }}</strong>{{ 'Try a merchant name, an amount such as 71.90, or a date such as 3/7/2026.' | t }}
                <button class="btn btn--small btn--quiet" type="button" (click)="typed(''); term.set('')">{{ 'Clear search' | t }}</button></p>
            } @else {
              <p class="empty"><strong>{{ filter() === 'review' ? (reading() ? ('Almost ready' | t) : ('Nothing waiting' | t)) : filter() === 'skipped' ? ('Nothing skipped' | t) : ('No receipts yet' | t) }}</strong>
                {{ filter() === 'review' ? ('New receipts appear here once KuTax has read them.' | t) : filter() === 'skipped' ? ('Receipts you skip appear here, so you can bring them back.' | t) : ('Add receipts from your inbox.' | t) }}</p>
            }
          }
          <div class="nudge"><app-icon name="spark" /><span><strong>{{ 'You approve, KuTax suggests' | t }}</strong><small>{{ 'Nothing is claimed until you confirm it.' | t }}</small></span></div>
        </aside>
        <section class="detail" aria-live="polite">
          @if (id(); as receiptId) {
            <app-receipt-review [id]="receiptId" [nextId]="next()" (saved)="saved()" (finished)="advance()" (confirmed)="afterConfirm($event)" (skipped)="showUndo($event)" (deleted)="afterDelete($event)" />
          } @else {
            <div class="card placeholder">
              <span class="thumb tone-purple"><app-icon name="file" /></span>
              <h2>{{ 'Pick a receipt to check' | t }}</h2>
              <p class="muted">{{ 'The original appears beside the details KuTax read, so you can compare them at a glance.' | t }}</p>
            </div>
          }
        </section>
      </div>
    </div>
  `,
  styles: `
    .quick { flex: none; }
    .progress { display: inline-block; background: var(--mint-soft); border: 1px solid #a8dfcd; border-radius: 20px; color: var(--mint-ink); flex: none; font-size: 14px; padding: 9px 14px; }
    .layout { align-items: start; display: grid; gap: 22px; grid-template-columns: 360px minmax(0, 1fr); }
    .queue { padding: 10px; position: sticky; top: 96px; }
    .tools { align-items: center; display: flex; gap: 8px; margin-bottom: 8px; }
    .tools .spacer { flex: 1; }
    .toggle { align-items: center; background: none; border: 0; color: var(--ink); cursor: pointer; display: flex; font: inherit; font-size: 13px; font-weight: 700; gap: 8px; min-height: 36px; padding: 0 4px; }
    .toggle .box { align-items: center; background: var(--white); border: 2px solid var(--ink); border-radius: 6px; display: flex; height: 20px; justify-content: center; width: 20px; }
    .toggle[aria-pressed='true'] .box { background: var(--yellow); }
    .batch { background: var(--yellow-soft); border: 2px solid var(--ink); border-radius: 14px; bottom: 84px; box-shadow: 4px 4px 0 var(--ink); display: grid; gap: 8px; margin-top: 10px; padding: 12px; position: sticky; z-index: 5; }
    .batch-head { align-items: center; display: flex; justify-content: space-between; }
    .batch .row { flex-wrap: wrap; }
    .tools input { flex: 1; min-height: 40px; min-width: 0; }
    .focus { align-items: center; background: var(--yellow-soft); border: 2px solid var(--ink); border-radius: 12px; display: flex; gap: 8px; justify-content: space-between; margin-bottom: 8px; padding: 6px 6px 6px 12px; }
    .focus span { display: grid; min-width: 0; }
    .focus small { color: var(--ink-soft); font-size: 11px; font-weight: 700; }
    .focus strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .segmented { background: #f3f1ea; border-radius: 14px; display: grid; gap: 4px; grid-template-columns: 1.3fr .8fr 1fr; margin-bottom: 8px; padding: 4px; }
    /* Its own colours, so it reads on any button state and theme. */
    .count { background: var(--yellow); border: 1px solid var(--ink); border-radius: 8px; color: var(--ink); display: inline-block; font-size: 11px; font-weight: 800; line-height: 1.3; margin-left: 4px; min-width: 18px; padding: 0 5px; text-align: center; }
    .progress small { display: block; font-size: 12px; }
    .segmented button { background: transparent; border: 0; border-radius: 10px; color: var(--ink-soft); font-size: 13px; font-weight: 800; min-height: 40px; }
    .segmented button[aria-pressed='true'] { background: var(--white); box-shadow: var(--shadow-small); color: var(--ink); }
    .more { margin: 10px; }
    .reread { margin: 0 0 10px; }
    .nudge { align-items: center; background: var(--sky-soft); border-radius: 14px; display: flex; gap: 10px; margin: 10px 2px 2px; padding: 12px; }
    .nudge app-icon { color: var(--purple-ink); }
    .nudge strong, .nudge small { display: block; }
    .nudge strong { font-size: 13px; }
    .nudge small { color: var(--ink-soft); font-size: 12px; }
    .detail-nav { grid-column: 1 / -1; }
    .has-detail .layout { row-gap: 14px; }
    .detail-nav .btn--quiet { padding-left: 2px; }
    .page.wide { max-width: 1640px; }
    /* With a receipt open, the document gets the room; the list is one tap away. */
    .list-hidden .layout { grid-template-columns: minmax(0, 1fr); }
    .list-hidden .queue, .list-hidden .heading { display: none; }
    .reading { align-items: center; display: flex; gap: 10px; margin: 4px 2px 10px; }
    .pulse { animation: pulse 1.2s ease-in-out infinite; background: var(--purple); border-radius: 50%; flex: none; height: 10px; width: 10px; }
    @keyframes pulse { 0%, 100% { opacity: .3; } 50% { opacity: 1; } }
    .placeholder { align-content: center; display: grid; justify-items: center; min-height: 420px; text-align: center; }
    .placeholder h2 { margin-top: 14px; }
    @media (max-width: 960px) {
      .layout { grid-template-columns: 1fr; }
      .queue { position: static; }
      .list-toggle { display: none; }
      .placeholder { display: none; }
      .has-detail .queue, .has-detail .heading { display: none; }
    }
  `,
})
export class ReviewPage implements OnDestroy {
  readonly id = input<string>();
  readonly session = inject(Session);
  readonly returnTo = inject(ReturnTo);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18n);
  private readonly route = inject(ActivatedRoute);
  readonly focus = signal<Focus | null>(readFocus(this.route.snapshot.queryParams, this.router.currentNavigation()?.previousNavigation?.finalUrl?.toString().startsWith('/review') ?? false));
  readonly focusLabel = computed(() => {
    const f = this.focus();
    if (!f) return '';
    const zh = this.i18n.language() === 'zh';
    const parts: string[] = [];
    if (f.merchant) parts.push(f.merchant);
    if (f.category) parts.push(this.i18n.t(categoryLabel(f.category)));
    if (f.month) {
      const [y, m] = f.month.split('-').map(Number);
      parts.push(zh ? `${y}年${m}月` : new Date(y, m - 1, 1).toLocaleDateString('en-AU', { month: 'long', year: 'numeric' }));
    } else if (f.year) parts.push(yearLabel(f.year));
    return parts.join(' · ');
  });
  // Arriving from a report shows every receipt that matches, whatever its state.
  readonly filter = signal<'review' | 'all' | 'skipped'>(this.focus() && this.route.snapshot.queryParamMap.keys.length ? 'all' : savedFilter());
  readonly sort = signal<Sort>(savedSort());
  /** Only receipts dated in the financial year chosen at the top. */
  readonly thisYear = signal(savedFlag(yearKey));
  readonly selecting = signal(false);
  readonly selected = signal<ReadonlySet<string>>(new Set());
  readonly batchReason = signal<string | null>(null);
  readonly skipReasons = skipReasons;
  readonly yearName = yearLabel;
  readonly allSelected = computed(() => { const items = this.selectableItems(); return items.length > 0 && items.every((i) => this.selected().has(i)); });
  private readonly selectableItems = computed(() => (this.page()?.items ?? []).filter((i) => i.reviewState !== 'Blocked' && i.reviewState !== 'Processing').map((i) => i.id));
  /** What's typed, and the term actually searched (after a short pause in typing). */
  readonly search = signal('');
  readonly term = signal('');
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  readonly sortOptions = [
    { value: 'added', label: 'Newest added' },
    { value: 'date', label: 'Receipt date, newest' },
    { value: 'date-asc', label: 'Receipt date, oldest' },
    { value: 'merchant', label: 'Merchant A–Z' },
    { value: 'amount', label: 'Amount, highest' },
  ];
  private readonly toasts = inject(Toasts);
  readonly page = signal<ReceiptPage | null>(null);
  readonly message = signal('');
  readonly notice = signal('');
  /** Desktop only: whether the queue stays visible beside an open receipt. */
  readonly listOpen = signal(false);
  readonly busy = signal(false);
  readonly waiting = computed(() => this.session.counts()?.needsReview ?? null);
  readonly reading = computed(() => this.session.counts()?.processing ?? 0);
  private timer: ReturnType<typeof setTimeout> | undefined;
  readonly next = computed(() => {
    const items = (this.page()?.items ?? []).filter((item) => item.reviewState !== 'Blocked');
    const index = items.findIndex((item) => item.id === this.id());
    const candidate = items[index + 1] ?? (index === -1 ? items[0] : undefined);
    return candidate && candidate.id !== this.id() ? candidate.id : null;
  });

  constructor() {
    effect(() => {
      this.filter();
      this.term();
      this.thisYear();
      this.focus();
      this.session.account()?.financialYearStart;
      try {
        sessionStorage.setItem(sortKey, this.sort());
      } catch {
        // Remembered for this page only.
      }
      void this.load();
    });
  }

  setThisYear(on: boolean) {
    this.thisYear.set(on);
    saveFlag(yearKey, on);
  }

  setSelecting(on: boolean) {
    this.selecting.set(on);
    this.selected.set(new Set());
  }

  toggle(id: string) {
    const next = new Set(this.selected());
    if (next.has(id)) next.delete(id); else next.add(id);
    this.selected.set(next);
  }

  selectAll() {
    this.selected.set(this.allSelected() ? new Set() : new Set(this.selectableItems()));
  }

  /** Skips (with a reason) or restores everything ticked; the ones that can't change are explained in a notice. */
  async batch(skip: boolean, reason: string | null = null) {
    const ids = [...this.selected()];
    if (!ids.length || this.busy()) return;
    this.busy.set(true);
    this.batchReason.set(reason);
    try {
      const result = await firstValueFrom(this.http.post<{ changed: number; notChanged: { documentId: string; reason: string }[] }>('/api/documents/skip-batch', { documentIds: ids, skip, reason }));
      this.toasts.show({ text: skip ? `${result.changed} skipped.` : `${result.changed} restored.` });
      this.notice.set(result.notChanged.length ? `${result.notChanged.length} couldn’t change: ${[...new Set(result.notChanged.map((n) => n.reason))].join(' ')}` : '');
      this.setSelecting(false);
      await this.load();
      void this.session.refreshCounts();
    } catch (error) {
      this.message.set(error instanceof HttpErrorResponse ? error.error?.detail ?? 'We couldn’t update these receipts. Try again.' : 'We couldn’t update these receipts. Try again.');
    } finally {
      this.busy.set(false);
      this.batchReason.set(null);
    }
  }

  typed(value: string) {
    this.search.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.term.set(value.trim()), 300);
  }

  setFilter(value: 'review' | 'all' | 'skipped') {
    this.filter.set(value);
    try {
      sessionStorage.setItem(filterKey, value);
    } catch {
      // Remembered for this page only.
    }
  }

  clearFocus() {
    this.focus.set(null);
    try { sessionStorage.removeItem(focusKey); } catch { /* nothing kept */ }
    if (Object.keys(this.route.snapshot.queryParams).length) void this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  ngOnDestroy() {
    clearTimeout(this.timer);
    clearTimeout(this.searchTimer);
  }

  /** Skipping can be undone for 10 seconds from the message at the bottom. */
  showUndo(event: { transactionId: string; version: string; merchant: string }) {
    this.toasts.show({ text: `Skipped ${event.merchant} — not for your tax return.`, action: { label: 'Undo', run: () => this.undo(event.transactionId, event.version) } });
  }

  async undo(transactionId: string, version: string) {
    try {
      await firstValueFrom(this.http.put(`/api/transactions/${transactionId}/dismiss`, { version, dismiss: false }));
      this.toasts.show({ text: 'Brought back to your review queue.' });
      await this.load();
    } catch {
      this.toasts.show({ text: 'Couldn’t undo. Find it under Skipped to bring it back.' });
    }
  }

  async afterDelete(name: string) {
    this.toasts.show({ text: `Deleted ${name}.` });
    await this.advance();
    await this.load();
  }

  /**
   * After confirming, open the next receipt that still needs confirming: the next one down the list, then any other
   * waiting in the queue. When none are left, stay here so the person sees it was the last one.
   */
  async afterConfirm(merchant: string) {
    const current = this.id();
    const waitingIn = (items: { id: string; reviewState: string }[]) => items.filter((item) => item.reviewState === 'NeedsReview' && item.id !== current);
    const listed = this.page()?.items ?? [];
    const index = listed.findIndex((item) => item.id === current);
    let nextId = waitingIn(listed.slice(index + 1))[0]?.id ?? waitingIn(listed.slice(0, Math.max(index, 0)))[0]?.id;
    if (!nextId) {
      try {
        nextId = waitingIn((await firstValueFrom(this.http.get<ReceiptPage>('/api/documents/?state=review'))).items)[0]?.id;
      } catch {
        // Stay on this receipt; the queue's own refresh will show what's left.
      }
    }
    if (!nextId || this.id() !== current) return;
    this.toasts.show({ text: merchant ? `Confirmed ${merchant}. Here’s the next one.` : 'Confirmed. Here’s the next one.' });
    await this.router.navigateByUrl(`/review/${nextId}`);
  }

  /** After skipping, go straight to the next receipt, or back to the queue when none are left. */
  async advance() {
    const nextId = this.next();
    await this.router.navigateByUrl(nextId ? `/review/${nextId}` : '/review');
  }

  async readAgain() {
    if (this.busy()) return;
    this.busy.set(true);
    this.message.set('');
    try {
      const result = await firstValueFrom(this.http.post<{ queued: number }>('/api/documents/read-again', {}));
      this.notice.set(result.queued
        ? `Reading ${result.queued} ${result.queued === 1 ? 'receipt' : 'receipts'} with AI. Anything you’ve already typed is kept.`
        : 'Every waiting receipt already has details you entered, so there’s nothing to re-read.');
      await this.load();
    } catch (error) {
      this.message.set(error instanceof HttpErrorResponse ? error.error?.detail ?? 'We couldn’t start reading. Try again.' : 'We couldn’t start reading. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  saved() {
    void this.load();
  }

  async load() {
    try {
      const params = new URLSearchParams();
      if (this.filter() !== 'all') params.set('state', this.filter());
      if (this.term()) params.set('search', this.term());
      if (this.sort() !== 'added') params.set('sort', this.sort());
      const year = this.session.account()?.financialYearStart;
      const focus = this.focus();
      if (focus) {
        if (focus.year) params.set('year', String(focus.year));
        if (focus.month) params.set('month', focus.month);
        if (focus.merchant) params.set('merchant', focus.merchant);
        if (focus.category) params.set('category', focus.category);
      } else if (this.thisYear() && year) params.set('year', String(year));
      const query = params.toString();
      const result = await firstValueFrom(this.http.get<ReceiptPage>(`/api/documents/${query ? '?' + query : ''}`));
      this.page.set(result);
      this.session.counts.set(result.counts);
      this.message.set('');
    } catch {
      this.message.set('We couldn’t load the review queue. Refresh to try again.');
    }
    // Keep the queue current while new receipts are still being checked and read.
    clearTimeout(this.timer);
    if (this.reading()) this.timer = setTimeout(() => void this.load(), 3000);
  }
}
