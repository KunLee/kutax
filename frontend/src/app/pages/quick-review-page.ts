import { Component, computed, effect, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Session, yearLabel } from '../core/session';
import { Icon } from '../core/icon';
import { claimTypes } from '../core/categories';
import { showAttention } from '../core/attention';
import { TranslatePipe } from '../core/i18n';

interface QuickItem { id: string; version: string; documentId: string; fileName: string; merchant: string; date: string; amount: number; status: string; suggestedCategory: string | null; note: string | null; }
interface QuickRule { category: string; label: string; section: string; paperReference: string; conditions: string; declaration: string; claimTypes: string[];
  maxItemCost: number | null; workUseAbove: number | null; wholeAmountOnly: boolean; sourceUrl: string; version: string; reviewed: boolean; defaultWorkUse: number; defaultClaimType: string | null; }
interface QuickCheck { documentId: string; merchant: string | null; amount: number | null; reason: string; }
interface QuickList { year: number; items: QuickItem[]; needsCheck: number; checks?: QuickCheck[]; rules: QuickRule[]; }
interface Row extends QuickItem { selected: boolean; category: string; }
interface Group { key: string; label: string; rule: QuickRule | null; rows: Row[]; selected: Row[]; total: number; }
interface Result { approved: number; approvedTotal: number; skipped: number; keptForAdvice?: number; notApproved: { transactionId: string; merchant: string; status: string; reason: string; documentId?: string }[]; }

/** Categories that are never calculated: skipped (not for the return) or kept for advice. */
const PERSONAL = 'Personal';
const ADVICE = 'Unsupported';
const UNSET = '';

@Component({
  selector: 'app-quick-review-page',
  imports: [TranslatePipe, CurrencyPipe, DatePipe, FormsModule, RouterLink, Icon],
  template: `
    <div class="page">
      <section class="page-heading">
        <div>
          <span class="eyebrow">{{ 'Quick review · {year}' | t: { year: yearName() } }}</span>
          <h1>{{ rows().length ? ('Approve in one go' | t) : loading() ? ('Getting your receipts…' | t) : ('Nothing for quick review' | t) }}</h1>
          <p>{{ 'Receipts AI read clearly, grouped by what they look like. Fix any category, confirm each group once, then approve them together.' | t }}</p>
        </div>
        <a class="btn btn--quiet" routerLink="/review"><app-icon name="back" [size]="18" />{{ 'One by one' | t }}</a>
      </section>

      @if (message()) { <p class="notice notice--error" role="alert">{{ message() | t }}</p> }
      @if (suggesting()) { <p class="notice reading" role="status"><span class="spinner" aria-hidden="true"></span>{{ 'Suggesting categories from merchant names…' | t }}</p> }

      @if (result(); as done) {
        <section class="card section done" aria-labelledby="done-title" role="status">
          <span class="thumb tone-mint"><app-icon name="check" /></span>
          <div>
            <h2 id="done-title">{{ done.approved ? (done.approved === 1 ? ('One claim approved' | t) : done.approved + (' claims approved' | t)) : ('Saved' | t) }}
              @if (done.approved) { · {{ done.approvedTotal | currency: ('AUD' | t) : 'symbol-narrow' }} }</h2>
            <p class="muted small">
              @if (done.skipped) { {{ '{n} skipped as not for your tax return.' | t: { n: done.skipped } }} }
              @if (done.keptForAdvice) { {{ '{n} kept for advice.' | t: { n: done.keptForAdvice } }} }
              {{ 'Everything is in your tax summary, where you can open any claim and change it.' | t }}
            </p>
            @if (done.notApproved.length) {
              <ul class="waiting">
                @for (item of done.notApproved; track item.transactionId) {
                  <li><a [routerLink]="['/review', item.documentId]">{{ item.merchant }}</a> — {{ item.reason | t }}</li>
                }
              </ul>
            }
            <div class="row"><a class="btn btn--primary btn--small" routerLink="/summary">{{ 'Open tax summary' | t }}</a>
              <button class="btn btn--small" type="button" (click)="result.set(null)">{{ 'Keep reviewing' | t }}</button></div>
          </div>
        </section>
      }

      @if (data(); as list) {
        @if (list.needsCheck) {
          <section class="card section checks" aria-labelledby="checks-title">
            <span class="kicker">{{ 'One by one' | t }}</span>
            <h2 id="checks-title">{{ (list.needsCheck === 1 ? '{n} receipt needs a closer look' : '{n} receipts need a closer look') | t: { n: list.needsCheck } }}</h2>
            <ul>
              @for (check of list.checks ?? []; track check.documentId) {
                <li><a [routerLink]="['/review', check.documentId]">
                  <span><strong>{{ check.merchant }}</strong><small>{{ check.reason | t }}</small></span>
                  @if (check.amount !== null) { <span class="amount">{{ check.amount | currency: ('AUD' | t) : 'symbol-narrow' }}</span> }
                  <app-icon name="arrow" [size]="16" />
                </a></li>
              }
            </ul>
            @if ((list.checks?.length ?? 0) < list.needsCheck) { <a class="small" routerLink="/review">{{ 'See all in Review' | t }}</a> }
          </section>
        }
        <!-- Administrators only: everyone else just sees, per group, that those claims are calculated later. -->
        @if (unreviewed().length && session.account()?.isAdministrator) {
          <section class="card section rules" aria-labelledby="rules-title">
            <span class="kicker">{{ 'One-time setup' | t }}</span>
            <h2 id="rules-title">{{ (unreviewed().length === 1 ? '{n} tax rule needs an administrator’s check' : '{n} tax rules need an administrator’s check') | t: { n: unreviewed().length } }}</h2>
            <p class="small">{{ unreviewedNames() }}. {{ 'Until then, receipts in those categories are confirmed but their claims wait in your tax summary.' | t }}</p>
            @if (session.account()?.isAdministrator) {
              <details>
                <summary>{{ 'Check the rules now' | t }}</summary>
                <ul class="small">@for (rule of unreviewed(); track rule.category) { <li><strong>{{ rule.label | t }}</strong> ({{ rule.paperReference }}): {{ rule.conditions | t }} <a [href]="rule.sourceUrl" target="_blank" rel="noopener">{{ 'ATO source' | t }}</a></li> }</ul>
                <label class="field">{{ 'Review note' | t }}<textarea [(ngModel)]="reviewNote" maxlength="2000" [placeholder]="'For example: checked against the ATO pages and this year’s instructions' | t"></textarea></label>
                <label class="check"><input type="checkbox" [(ngModel)]="reviewConfirmed"><span>{{ 'I checked each rule’s conditions and myTax section against the official ATO sources.' | t }}</span></label>
                <button class="btn btn--primary" type="button" (click)="reviewRules()" [disabled]="busy() || !reviewConfirmed || !reviewNote.trim()">{{ (unreviewed().length === 1 ? 'Record review of {n} rule' : 'Record review of {n} rules') | t: { n: unreviewed().length } }}</button>
              </details>
            }
          </section>
        }
        @if (!rows().length && !loading()) {
          <div class="card empty-state">
            <span class="thumb tone-purple"><app-icon name="spark" /></span>
            <h2>{{ 'All clear' | t }}</h2>
            <p class="muted">{{ 'Receipts appear here once AI has read them clearly. Add more from your inbox.' | t }}</p>
            <a class="btn" routerLink="/">{{ 'Go to inbox' | t }}</a>
          </div>
        }
        @for (group of groups(); track group.key) {
          <section class="card section group" [class.group--muted]="!group.rule" [attr.aria-labelledby]="'g-' + group.key">
            <div class="section-heading">
              <div>
                <span class="kicker">{{ group.rule ? group.rule.section + ' · ' + group.rule.paperReference : group.key === personal ? ('Skipped' | t) : group.key === advice ? ('Not calculated' | t) : ('Needs a category' | t) }}</span>
                <h2 [id]="'g-' + group.key">{{ group.label | t }}</h2>
              </div>
              <span class="chip" [class.chip--purple]="group.selected.length">{{ '{n} of {total}' | t: { n: group.selected.length, total: group.rows.length } }} · {{ group.total | currency: ('AUD' | t) : 'symbol-narrow' }}</span>
            </div>
            @if (group.key === personal) { <p class="muted small">{{ 'Ticked receipts are skipped: kept in your records but left out of your tax return. You can bring them back from Review.' | t }}</p> }
            @if (group.key === advice) { <p class="muted small">{{ 'Possibly work-related, but KuTax doesn’t calculate these (for example travel, car or working from home). Ticked receipts are kept in your tax pack as “needs advice” without a deduction.' | t }}</p> }
            @if (group.key === unset) { <p class="muted small">{{ 'Pick a category for each receipt you want to include.' | t }}</p> }

            <ul class="rows">
              @for (row of group.rows; track row.id) {
                <li [class.off]="!row.selected">
                  <label class="pick"><input type="checkbox" [(ngModel)]="row.selected" (ngModelChange)="touch()" [attr.aria-label]="'Include ' + row.merchant" [disabled]="row.category === unset">
                    <span class="what"><strong>{{ row.merchant }}</strong><small>{{ row.date | date: ('d MMM yyyy' | t) }} · <a [routerLink]="['/review', row.documentId]">{{ 'view receipt' | t }}</a></small>
                      @if (row.note && (row.category === 'ToolsEquipment' || row.category === 'SoftwareSubscriptions')) { <small class="warn">{{ row.note | t }}</small> }</span>
                    <strong class="amount">{{ row.amount | currency: ('AUD' | t) : 'symbol-narrow' }}</strong></label>
                  <label class="sr-only" [for]="'c-' + row.id">{{ 'Category for {name}' | t: { name: row.merchant } }}</label>
                  <select class="category" [id]="'c-' + row.id" [ngModel]="row.category" (ngModelChange)="setCategory(row, $event)">
                    <option [value]="unset" disabled>{{ 'Choose a category…' | t }}</option>
                    @for (rule of data()!.rules; track rule.category) { <option [value]="rule.category">{{ rule.label | t }}</option> }
                    <option [value]="advice">{{ 'Other work expense (needs advice)' | t }}</option>
                    <option [value]="personal">{{ 'Not for my tax return' | t }}</option>
                  </select>
                </li>
              }
            </ul>

            @if (group.rule; as rule) {
              @if (group.selected.length) {
                <div class="terms">
                  @if (!rule.wholeAmountOnly || rule.claimTypes.length) {
                    <div class="settings">
                      @if (!rule.wholeAmountOnly) {
                        <label class="field">{{ 'Work use %' | t }}<input type="number" inputmode="decimal" min="0" max="100" step="1" [ngModel]="workUse()[rule.category] ?? rule.defaultWorkUse" (ngModelChange)="setWorkUse(rule.category, $event)"></label>
                      }
                      @if (rule.claimTypes.length) {
                        <label class="field">{{ 'Claim type' | t }}<select [id]="'q-claim-' + rule.category" [ngModel]="claimType()[rule.category] ?? rule.defaultClaimType ?? ''" (ngModelChange)="setClaimType(rule.category, $event)">
                          <option value="" disabled>{{ 'Choose…' | t }}</option>
                          @for (code of rule.claimTypes; track code) { <option [value]="code">{{ claimLabel(code) | t }}</option> }
                        </select></label>
                      }
                    </div>
                    @if (rule.workUseAbove !== null && (workUse()[rule.category] ?? rule.defaultWorkUse) <= rule.workUseAbove) {
                      <p class="small warn">{{ 'Items used {pct}% or less for work decline in value instead, which KuTax doesn’t calculate.' | t: { pct: rule.workUseAbove } }}</p>
                    }
                  }
                  <label class="check"><input type="checkbox" [id]="'q-declare-' + rule.category" [ngModel]="!!declared()[rule.category]" (ngModelChange)="declare(rule.category, $event)">
                    <span><strong>{{ 'For each ticked receipt:' | t }}</strong> {{ rule.declaration | t }}</span></label>
                  <details class="small"><summary>{{ 'What counts, from the ATO' | t }}</summary><p>{{ rule.conditions | t }}</p><a [href]="rule.sourceUrl" target="_blank" rel="noopener">{{ 'Read the ATO guidance' | t }}</a></details>
                  @if (!rule.reviewed) { <p class="small warn">{{ 'These are calculated once this year’s tax rules are ready. Approve them now; nothing else to do.' | t }}</p> }
                </div>
              }
            }
          </section>
        }
      }

      @if (selectedCount()) {
        <div class="bar" role="region" [attr.aria-label]="'Approve selection' | t">
          <div class="bar-inner">
            <span class="summary"><strong>{{ (selectedCount() === 1 ? '{n} receipt' : '{n} receipts') | t: { n: selectedCount() } }}</strong>
              <small>{{ (missing()?.message | t) || ('Ready to approve. Partly reimbursed? Use the one-by-one review for those.' | t) }}</small></span>
            <button class="btn btn--primary" type="button" (click)="approve()" [disabled]="busy()">
              @if (busy()) { <span class="spinner" aria-hidden="true"></span> } @else { <app-icon name="check" [size]="18" /> }{{ 'Approve {n}' | t: { n: selectedCount() } }}</button>
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    .reading { align-items: center; display: flex; gap: 10px; }
    .checks { margin-bottom: 18px; }
    .checks h2 { margin: 2px 0 8px; }
    .checks ul { list-style: none; margin: 0; padding: 0; }
    .checks li a { align-items: center; border-top: 1px solid var(--line); color: inherit; display: flex; gap: 12px; min-height: 52px; padding: 8px 0; text-decoration: none; }
    .checks li a > span:first-child { flex: 1; min-width: 0; }
    .checks strong, .checks small { display: block; overflow: hidden; text-overflow: ellipsis; }
    .checks small { color: var(--orange-ink); font-weight: 600; }
    .checks li a:hover strong { text-decoration: underline; }
    .nudge { align-items: center; color: inherit; display: flex; gap: 12px; margin-bottom: 18px; padding: 14px 16px; text-decoration: none; }
    .nudge app-icon:first-child { color: var(--orange-ink); }
    .nudge span { flex: 1; }
    .nudge strong, .nudge small { display: block; }
    .nudge small { color: var(--ink-soft); }
    .empty-state { display: grid; gap: 8px; justify-items: center; padding: 40px 20px; text-align: center; }
    .group, .rules { margin-bottom: 18px; }
    .rules { background: var(--yellow-soft); }
    .rules h2 { margin: 2px 0 6px; }
    .rules ul { padding-left: 18px; }
    .group--muted { background: #fbfaf6; }
    .rows { list-style: none; margin: 0 0 4px; padding: 0; }
    .rows li { align-items: center; border-top: 1px solid var(--line); display: grid; gap: 6px 12px; grid-template-columns: minmax(0, 1fr) 230px; padding: 10px 0; }
    .rows li.off .what, .rows li.off .amount { opacity: .55; }
    .pick { align-items: center; cursor: pointer; display: grid; gap: 12px; grid-template-columns: 22px minmax(0, 1fr) auto; min-height: 44px; }
    .pick input { height: 20px; width: 20px; }
    .what { min-width: 0; }
    .what strong, .what small { display: block; overflow: hidden; text-overflow: ellipsis; }
    .what small { color: var(--ink-soft); font-size: 12px; }
    .amount { font-variant-numeric: tabular-nums; }
    .category { background: var(--white); border: 1px solid var(--line); border-radius: 10px; color: var(--ink); font: inherit; font-size: 14px; min-height: 40px; padding: 0 10px; width: 100%; }
    .warn { color: var(--orange-ink); }
    .terms { border-top: 1px solid var(--line); display: grid; gap: 10px; padding-top: 12px; }
    .settings { display: grid; gap: 0 12px; grid-template-columns: 140px minmax(0, 1fr); }
    .settings .field { margin-bottom: 0; }
    details p { margin: 6px 0; }
    .done { align-items: start; display: flex; gap: 14px; margin-bottom: 18px; }
    .done h2 { margin: 0 0 4px; }
    .waiting { font-size: 14px; margin: 8px 0 12px; padding-left: 18px; }
    .bar { bottom: 0; left: 0; padding: 0 16px 12px; position: sticky; z-index: 5; }
    .bar-inner { align-items: center; background: var(--ink); border-radius: 18px; box-shadow: var(--shadow); color: var(--white); display: flex; gap: 14px; margin: 0 auto; max-width: 760px; padding: 12px 12px 12px 18px; }
    .summary { flex: 1; min-width: 0; }
    .summary strong, .summary small { display: block; }
    .summary small { color: rgba(255, 255, 255, .78); font-size: 12px; }
    @media (max-width: 720px) {
      .rows li { grid-template-columns: 1fr; }
      .category { margin-left: 34px; width: calc(100% - 34px); }
      .settings { grid-template-columns: 1fr 1fr; }
      .bar { bottom: var(--tabbar, 0px); padding: 0 0 10px; }
    }
  `,
})
export class QuickReviewPage {
  readonly session = inject(Session);
  private readonly http = inject(HttpClient);
  readonly personal = PERSONAL;
  readonly advice = ADVICE;
  readonly unset = UNSET;
  readonly data = signal<QuickList | null>(null);
  readonly rows = signal<Row[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly suggesting = signal(false);
  readonly message = signal('');
  readonly result = signal<Result | null>(null);
  readonly workUse = signal<Record<string, number>>({});
  readonly claimType = signal<Record<string, string>>({});
  readonly declared = signal<Record<string, boolean>>({});
  private readonly version = signal(0);
  private suggested = false;
  readonly year = computed(() => this.session.account()?.financialYearStart ?? null);
  readonly yearName = computed(() => (this.year() === null ? '' : yearLabel(this.year()!)));

  /** Rows grouped by chosen category: calculated categories first, then advice, skipped and unset. */
  readonly groups = computed<Group[]>(() => {
    this.version();
    const rules = this.data()?.rules ?? [];
    const order = [...rules.map((r) => r.category), ADVICE, PERSONAL, UNSET];
    const label = (key: string) => rules.find((r) => r.category === key)?.label ?? (key === ADVICE ? 'Other work expenses' : key === PERSONAL ? 'Not for my tax return' : 'Choose a category');
    return order
      .map((key) => {
        const rows = this.rows().filter((row) => row.category === key);
        const selected = rows.filter((row) => row.selected);
        return { key, label: label(key), rule: rules.find((r) => r.category === key) ?? null, rows, selected, total: selected.reduce((sum, row) => sum + row.amount, 0) };
      })
      .filter((group) => group.rows.length);
  });
  /** Unreviewed rules for categories that have receipts here. */
  readonly unreviewed = computed(() => {
    this.version();
    const used = new Set(this.rows().map((row) => row.category));
    return (this.data()?.rules ?? []).filter((rule) => !rule.reviewed && used.has(rule.category));
  });
  reviewNote = '';
  reviewConfirmed = false;
  readonly selectedCount = computed(() => this.groups().reduce((sum, group) => sum + group.selected.length, 0));
  /** What still stops approval, in plain words; empty when ready. */
  readonly missing = computed(() => {
    for (const group of this.groups()) {
      const rule = group.rule;
      if (!rule || !group.selected.length) continue;
      if (rule.claimTypes.length && !(this.claimType()[rule.category] ?? rule.defaultClaimType)) return { id: 'q-claim-' + rule.category, message: `Choose the claim type for ${rule.label}.` };
      if (!this.declared()[rule.category]) return { id: 'q-declare-' + rule.category, message: `Tick the conditions for ${rule.label} if they’re true.` };
    }
    return null;
  });

  constructor() {
    effect(() => {
      if (this.year() !== null) void this.load();
    });
  }

  unreviewedNames() {
    return this.unreviewed().map((rule) => rule.label).join(', ');
  }

  async reviewRules() {
    const year = this.year();
    if (year === null || this.busy()) return;
    this.busy.set(true);
    try {
      const versions = this.unreviewed().map((rule) => rule.version);
      await firstValueFrom(this.http.post('/api/tax-rules/review-batch', { versions, confirm: this.reviewConfirmed, note: this.reviewNote.trim() }));
      this.reviewNote = '';
      this.reviewConfirmed = false;
      await this.load();
    } catch (error) {
      this.message.set(error instanceof HttpErrorResponse ? error.error?.detail ?? 'We couldn’t record the review.' : 'We couldn’t record the review.');
    } finally {
      this.busy.set(false);
    }
  }

  claimLabel(code: string) {
    return claimTypes[code] ?? code;
  }

  docFor(transactionId: string) {
    return this.data()?.items.find((item) => item.id === transactionId)?.documentId ?? '';
  }

  touch() {
    this.version.update((v) => v + 1);
  }

  setCategory(row: Row, category: string) {
    row.category = category;
    row.selected = category !== UNSET;
    this.touch();
  }

  setWorkUse(category: string, value: number) {
    this.workUse.update((all) => ({ ...all, [category]: Number(value) }));
  }

  setClaimType(category: string, value: string) {
    this.claimType.update((all) => ({ ...all, [category]: value }));
  }

  declare(category: string, value: boolean) {
    this.declared.update((all) => ({ ...all, [category]: value }));
  }

  async load() {
    const year = this.year();
    if (year === null) return;
    this.loading.set(true);
    try {
      const list = await firstValueFrom(this.http.get<QuickList>(`/api/quick-review/${year}`));
      const known = new Set([...list.rules.map((r) => r.category), ADVICE, PERSONAL]);
      this.data.set(list);
      this.rows.set(list.items.map((item) => {
        let category = item.suggestedCategory && known.has(item.suggestedCategory) ? item.suggestedCategory : UNSET;
        // Items over a category's immediate-deduction limit can't be calculated; start them as "needs advice".
        const limit = list.rules.find((r) => r.category === category)?.maxItemCost;
        if (limit != null && item.amount > limit) category = ADVICE;
        return { ...item, category, selected: category !== UNSET };
      }));
      this.message.set('');
      // Receipts read before KuTax suggested categories: ask once, from merchant names only.
      if (!this.suggested && this.session.aiState() === 'on' && list.items.some((item) => !item.suggestedCategory)) {
        this.suggested = true;
        void this.suggest();
      }
    } catch {
      this.message.set('We couldn’t load your receipts. Refresh to try again.');
    } finally {
      this.loading.set(false);
    }
  }

  async suggest() {
    this.suggesting.set(true);
    try {
      const result = await firstValueFrom(this.http.post<{ suggested: number }>(`/api/quick-review/${this.year()}/suggest`, {}));
      if (result.suggested) await this.load();
    } catch {
      // Suggestions are optional; the person can still choose categories.
    } finally {
      this.suggesting.set(false);
    }
  }

  async approve() {
    const missing = this.missing();
    if (missing) { showAttention(missing.id, missing.message); return; }
    if (this.busy()) return;
    this.busy.set(true);
    this.message.set('');
    const items = this.groups().flatMap((group) => group.selected.map((row) => ({
      transactionId: row.id,
      version: row.version,
      category: row.category,
      workUsePercent: group.rule ? (group.rule.wholeAmountOnly ? 100 : this.workUse()[group.rule.category] ?? group.rule.defaultWorkUse) : 100,
      claimType: group.rule?.claimTypes.length ? this.claimType()[group.rule.category] ?? group.rule.defaultClaimType : null,
    })));
    const declarations = this.groups().filter((group) => group.rule && group.selected.length && this.declared()[group.key]).map((group) => group.key);
    try {
      const result = await firstValueFrom(this.http.post<Result>(`/api/quick-review/${this.year()}/approve`, { items, declarations, confirm: true }));
      this.result.set({ ...result, notApproved: result.notApproved.map((item) => ({ ...item, documentId: this.docFor(item.transactionId) })) });
      this.declared.set({});
      await Promise.all([this.load(), this.session.refreshCounts()]);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } catch (error) {
      this.message.set(error instanceof HttpErrorResponse ? error.error?.detail ?? 'We couldn’t approve these. Refresh and try again.' : 'We couldn’t approve these.');
    } finally {
      this.busy.set(false);
    }
  }
}
