import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { yearLabel } from '../core/session';
import { Icon } from '../core/icon';
import { TranslatePipe } from '../core/i18n';

export interface TaxPackSummary {
  id: string; financialYearStart: number; approvedAt: string; status: string; approvedTotal: number; entryAmount: number;
  approvedClaims: number; evidence: number; entryProgress: 'NotStarted' | 'Entered' | 'Checked';
}
export const progressLabels = { NotStarted: 'Not entered in myTax yet', Entered: 'Entered in myTax', Checked: 'Entered and checked' } as const;

/** Every tax pack ever prepared, year by year and version by version, with quick filters. */
@Component({
  selector: 'app-history-page',
  imports: [TranslatePipe, CurrencyPipe, DatePipe, RouterLink, Icon],
  template: `
    <div class="page">
      <section class="page-heading">
        <div>
          <span class="eyebrow">{{ 'Tax return history' | t }}</span>
          <h1>{{ 'Every year, kept safe.' | t }}</h1>
          <p>{{ 'Every tax pack you’ve prepared, year by year. Open one to see what you claimed and download it again.' | t }}</p>
        </div>
        <a class="btn btn--primary" routerLink="/summary" fragment="tax-pack"><app-icon name="plus" [size]="18" />{{ 'New tax pack' | t }}</a>
      </section>
      @if (message()) { <p class="notice notice--error" role="alert">{{ message() | t }}</p> }
      @if (packs()?.length) {
        <div class="filters">
          <div class="years" role="group" [attr.aria-label]="'Financial year' | t">
            <button type="button" class="pill" [attr.aria-pressed]="year() === null" (click)="year.set(null)">{{ 'All years' | t }} <span class="count">{{ packs()!.length }}</span></button>
            @for (group of allYears(); track group.year) {
              <button type="button" class="pill" [attr.aria-pressed]="year() === group.year" (click)="year.set(group.year)">{{ label(group.year) }} <span class="count">{{ group.count }}</span></button>
            }
          </div>
          <div class="segmented" role="group" [attr.aria-label]="'Show' | t">
            <button type="button" [attr.aria-pressed]="show() === 'all'" (click)="show.set('all')">{{ 'All versions' | t }}</button>
            <button type="button" [attr.aria-pressed]="show() === 'latest'" (click)="show.set('latest')">{{ 'Latest only' | t }}</button>
            <button type="button" [attr.aria-pressed]="show() === 'todo'" (click)="show.set('todo')">{{ 'Not in myTax yet' | t }}</button>
          </div>
        </div>
      }
      @if (years(); as groups) {
        @for (group of groups; track group.year) {
          <section class="card section year" [attr.aria-labelledby]="'year-' + group.year">
            <div class="section-heading">
              <div><span class="kicker">{{ 'Financial year' | t }}</span><h2 [id]="'year-' + group.year">{{ label(group.year) }}</h2></div>
              <span class="muted small">{{ (group.total === 1 ? '{n} tax pack' : '{n} tax packs') | t: { n: group.total } }}</span>
            </div>
            <ul class="packs">
              @for (pack of group.items; track pack.id) {
                <li><a class="pack" [class.pack--latest]="pack.id === group.latestId" [routerLink]="['/history', pack.id]">
                  <span class="when"><strong>{{ pack.approvedAt | date: ('d MMM yyyy, h:mm a' | t) }}</strong>
                    @if (pack.id === group.latestId) { <span class="chip chip--done">{{ 'Latest' | t }}</span> } @else { <small>{{ 'Replaced by a later pack' | t }}</small> }
                    @if (pack.status !== 'Complete') { <span class="chip chip--info">{{ pack.status | t }}</span> }</span>
                  <span class="figure"><small>{{ 'Approved deductions' | t }}</small><strong>{{ pack.approvedTotal | currency: ('AUD' | t) : 'symbol-narrow' }}</strong></span>
                  <span class="figure"><small>{{ 'Claims · attachments' | t }}</small><strong>{{ pack.approvedClaims }} · {{ pack.evidence }}</strong></span>
                  <span class="figure"><small>myTax</small><strong class="state">{{ progress(pack) | t }}</strong></span>
                  <app-icon class="open" name="arrow" [size]="18" />
                </a></li>
              }
            </ul>
          </section>
        } @empty {
          @if (packs()?.length) {
            <p class="empty-filter muted">{{ 'Nothing matches this filter.' | t }} <button class="btn btn--small" type="button" (click)="year.set(null); show.set('all')">{{ 'Show all' | t }}</button></p>
          } @else {
            <section class="card empty-state">
              <span class="thumb tone-mint"><app-icon name="archive" /></span>
              <h2>{{ 'No tax packs yet' | t }}</h2>
              <p class="muted">{{ 'When you approve your figures in the tax summary, KuTax freezes them into a tax pack: a summary PDF, a myTax entry guide, a detailed workbook and your original receipts. They’ll all appear here, year by year.' | t }}</p>
              <a class="btn btn--primary" routerLink="/summary">{{ 'Go to tax summary' | t }}</a>
            </section>
          }
        }
      }
    </div>
  `,
  styles: `
    .filters { display: grid; gap: 10px; margin-bottom: 16px; }
    .years { display: flex; gap: 8px; overflow-x: auto; padding: 2px 2px 4px; scrollbar-width: none; }
    .pill { background: var(--white); border: 2px solid var(--ink); border-radius: 999px; color: var(--ink); cursor: pointer; flex: none; font: inherit; font-size: 13px; font-weight: 800; min-height: 38px; padding: 0 14px; white-space: nowrap; }
    .pill[aria-pressed='true'] { background: var(--yellow); }
    .count { font-size: 11px; opacity: .7; }
    .segmented { background: #f3f1ea; border-radius: 14px; display: grid; gap: 4px; grid-template-columns: repeat(3, 1fr); max-width: 520px; padding: 4px; }
    .segmented button { background: transparent; border: 0; border-radius: 10px; color: var(--ink-soft); cursor: pointer; font: inherit; font-size: 13px; font-weight: 800; min-height: 40px; }
    .segmented button[aria-pressed='true'] { background: var(--white); box-shadow: var(--shadow-small); color: var(--ink); }
    .packs { display: grid; gap: 8px; list-style: none; margin: 0; padding: 0; }
    .pack { align-items: center; background: #fbfaf6; border: 1px solid var(--line); border-radius: 16px; color: var(--ink); display: grid; font-weight: 400;
      gap: 12px; grid-template-columns: 1.4fr 1fr 1fr 1fr auto; padding: 12px 16px; text-decoration: none; }
    .pack:hover { border-color: var(--ink); }
    .pack--latest { background: var(--purple-soft); border-color: var(--ink); }
    .when { align-items: center; display: flex; flex-wrap: wrap; gap: 4px 8px; }
    .when strong { width: 100%; }
    .when small, .figure small { color: var(--ink-soft); font-size: 12px; }
    .figure small, .figure strong { display: block; }
    .figure strong { font-size: 16px; margin-top: 2px; }
    .figure .state { font-size: 13px; }
    .open { color: var(--purple-ink); }
    .empty-filter { align-items: center; display: flex; flex-wrap: wrap; gap: 10px; }
    .empty-state { display: grid; justify-items: center; padding: 40px 24px; text-align: center; }
    .empty-state h2 { margin-top: 14px; }
    .empty-state p { max-width: 560px; }
    @media (max-width: 700px) {
      .pack { grid-template-columns: 1fr 1fr; }
      .when { grid-column: 1 / -1; }
      .open { display: none; }
      .segmented { max-width: none; }
    }
  `,
})
export class HistoryPage {
  private readonly http = inject(HttpClient);
  readonly packs = signal<TaxPackSummary[] | null>(null);
  readonly message = signal('');
  readonly label = yearLabel;
  /** Quick filters: one year or all of them; every version, each year's latest, or latest packs not yet entered in myTax. */
  readonly year = signal<number | null>(Number(inject(ActivatedRoute).snapshot.queryParamMap.get('year')) || null);
  readonly show = signal<'all' | 'latest' | 'todo'>('all');
  readonly allYears = computed(() => {
    const counts = new Map<number, number>();
    for (const pack of this.packs() ?? []) counts.set(pack.financialYearStart, (counts.get(pack.financialYearStart) ?? 0) + 1);
    return [...counts].sort((a, b) => b[0] - a[0]).map(([year, count]) => ({ year, count }));
  });
  readonly years = computed(() => {
    const packs = this.packs();
    if (!packs) return null;
    const byYear = new Map<number, TaxPackSummary[]>();
    for (const pack of packs) byYear.set(pack.financialYearStart, [...(byYear.get(pack.financialYearStart) ?? []), pack]);
    const show = this.show();
    const year = this.year();
    return [...byYear].sort((a, b) => b[0] - a[0]).filter(([y]) => year === null || y === year).map(([y, all]) => {
      const sorted = [...all].sort((a, b) => b.approvedAt.localeCompare(a.approvedAt));
      const latest = sorted.slice(0, 1);
      const items = show === 'latest' ? latest : show === 'todo' ? latest.filter((p) => p.entryProgress === 'NotStarted') : sorted;
      return { year: y, latestId: sorted[0].id, total: sorted.length, items };
    }).filter((group) => group.items.length);
  });

  constructor() {
    void this.load();
  }

  progress(pack: TaxPackSummary) {
    return progressLabels[pack.entryProgress] ?? pack.entryProgress;
  }

  async load() {
    try {
      this.packs.set(await firstValueFrom(this.http.get<TaxPackSummary[]>('/api/tax-exports')));
    } catch {
      this.message.set('We couldn’t load your tax pack history. Refresh to try again.');
    }
  }
}
