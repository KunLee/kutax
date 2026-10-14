import { Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Session, yearLabel } from '../core/session';
import { Icon } from '../core/icon';
import { I18n, TranslatePipe } from '../core/i18n';
import { categories, categoryLabel } from '../core/categories';
import { box, floor, ring } from '../core/iso';
import { SelectMenu } from '../core/select-menu';

interface Report {
  year: number;
  totals: { receipts: number; spent: number; claimed: number; ready: number; skipped: number; toCheck: number; expenses: number };
  claims: { approved: number; ready: number; waiting: number; advice: number; excluded: number };
  months: { month: string; spent: number; claimed: number; receipts: number }[];
  categories: { category: string; claimed: number; ready: number; spent: number; expenses: number }[];
  merchants: { merchant: string; spent: number; receipts: number }[];
  currencies: { currency: string; receipts: number; spent: number; original: number }[];
  years: { year: number; claimed: number; spent: number }[];
  missing: { merchant: string; months: number; typical: number; category: string | null; missing: string[] }[];
  compare: { previousYear: number; items: { category: string; lastYear: number; thisYear: number }[] };
  rates: { brackets: { from: number; to: number | null; rate: number }[]; medicareLevy: number; source: string } | null;
}
type Chart = 'months' | 'kinds' | 'status' | 'merchants' | 'years';

const monthsEn = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const statusParts = [
  { key: 'approved', label: 'Claimed', color: 'var(--mint-ink)' },
  { key: 'ready', label: 'Ready to approve', color: 'var(--sky)' },
  { key: 'waiting', label: 'Calculated soon', color: 'var(--yellow)' },
  { key: 'advice', label: 'Kept for advice', color: 'var(--orange)' },
  { key: 'excluded', label: 'Excluded', color: 'var(--line)' },
] as const;
const bracketKey = 'kutax.reports.bracket';

/**
 * Reports on the chosen financial year, drawn as outlined building blocks (inline SVG, so they follow the theme):
 * what may still be missing, how the year compares with the last, spending month by month, where deductions come
 * from, how claims stand, the merchants spent with most, and year-by-year totals. Every item shows its figures on
 * hover (tap on phones) and opens the matching receipts when chosen.
 */
@Component({
  selector: 'app-reports-page',
  imports: [TranslatePipe, RouterLink, Icon, SelectMenu],
  template: `
    <div class="page">
      <section class="page-heading">
        <div>
          <span class="eyebrow">{{ 'Reports · {year}' | t: { year: label(session.account()?.financialYearStart ?? 0) } }}</span>
          <h1>{{ 'Your year at a glance.' | t }}</h1>
          <p>{{ 'Where your money went, what you can claim and how this year compares. Figures match your tax summary; change the year at the top.' | t }}</p>
        </div>
      </section>
      @if (message()) { <p class="notice notice--error" role="alert">{{ message() | t }}</p> }
      @if (report(); as r) {
        @if (!r.totals.receipts) {
          <section class="card empty-state">
            <span class="thumb tone-mint"><app-icon name="report" /></span>
            <h2>{{ 'No receipts in this year yet' | t }}</h2>
            <p class="muted">{{ 'Add receipts and your reports appear here, month by month.' | t }}</p>
            <a class="btn btn--primary" routerLink="/">{{ 'Add receipts' | t }}</a>
          </section>
        } @else {
          <div class="kpis">
            <a class="kpi tone-spent" [routerLink]="['/review']" [queryParams]="{ year: r.year }"><small>{{ 'Spent on receipts' | t }}</small><strong>{{ money(r.totals.spent) }}</strong><span>{{ (r.totals.receipts === 1 ? '{n} receipt' : '{n} receipts') | t: { n: r.totals.receipts } }}</span></a>
            <a class="kpi tone-claimed" routerLink="/summary"><small>{{ 'Deductions claimed' | t }}</small><strong>{{ money(r.totals.claimed) }}</strong><span>{{ share(r.totals.claimed, r.totals.spent) }} {{ 'of spending' | t }}</span></a>
            <a class="kpi tone-ready" routerLink="/summary"><small>{{ 'Ready to approve' | t }}</small><strong>{{ money(r.totals.ready) }}</strong><span>{{ 'Approve in Tax' | t }} →</span></a>
            <a class="kpi tone-check" routerLink="/review"><small>{{ 'Receipts to check' | t }}</small><strong>{{ r.totals.toCheck }}</strong><span>{{ '{n} skipped as not claimable' | t: { n: r.totals.skipped } }}</span></a>
          </div>

          @if (r.rates && deductible() > 0) {
            <section class="card estimate" aria-labelledby="estimate-title">
              <span class="estimate-icon" aria-hidden="true">$</span>
              <div class="estimate-copy">
                <span class="kicker">{{ 'Estimate' | t }}</span>
                <h2 id="estimate-title">@if (saving() !== null) { {{ 'About {amount} less tax' | t: { amount: money(saving()!) } }} } @else { {{ 'How much less tax?' | t }} }</h2>
                <p class="small muted">@if (saving() !== null) { {{ 'From {deductions} of deductions at {rate} (your tax rate plus the 2% Medicare levy).' | t: { deductions: money(deductible()), rate: ratePercent() } }} } @else { {{ 'Choose your yearly taxable income to see roughly what your deductions save.' | t }} }
                  {{ 'A rough guide only: offsets, other income and tax already withheld change the result, which myTax works out.' | t }}</p>
              </div>
              <app-select-menu label="Yearly taxable income" [options]="bracketOptions()" [value]="bracket()" (valueChange)="setBracket($event)" />
            </section>
          }

          @if (r.missing.length || r.compare.items.length) {
            <div class="pair">
              @if (r.missing.length) {
                <section class="card section insight" aria-labelledby="missing-title">
                  <span class="kicker">{{ 'Worth a look' | t }}</span>
                  <h2 id="missing-title">{{ 'Receipts that may be missing' | t }}</h2>
                  <p class="small muted">{{ 'These come every month, but some months have no receipt. Find them in your email or account and add them.' | t }}</p>
                  <ul class="insights">
                    @for (m of r.missing; track m.merchant) {
                      <li>
                        <a class="insight-head" [routerLink]="['/review']" [queryParams]="{ year: r.year, merchant: m.merchant }"><strong>{{ m.merchant }}</strong><span>{{ 'about {amount} each' | t: { amount: money(m.typical) } }}</span></a>
                        <span class="months">@for (month of m.missing; track month) { <span class="month-chip">{{ monthName(month) }}</span> }</span>
                        <small>{{ '{n} months found' | t: { n: m.months } }}@if (m.category) { · {{ kindName(m.category) | t }} }</small>
                      </li>
                    }
                  </ul>
                  <a class="btn btn--small" routerLink="/">{{ 'Add receipts' | t }}</a>
                </section>
              }
              @if (r.compare.items.length) {
                <section class="card section insight" aria-labelledby="compare-title">
                  <span class="kicker">{{ 'Compared with {year}' | t: { year: label(r.compare.previousYear) } }}</span>
                  <h2 id="compare-title">{{ 'Claimed last year, not yet this year' | t }}</h2>
                  <p class="small muted">{{ 'If you still had these costs this year, add their receipts so you don’t miss the deduction.' | t }}</p>
                  <ul class="insights">
                    @for (c of r.compare.items; track c.category) {
                      <li class="compare">
                        <a class="insight-head" [routerLink]="['/review']" [queryParams]="{ year: r.compare.previousYear, category: c.category }"><i class="sw" [style.background]="colour(c.category)"></i><strong>{{ kindName(c.category) | t }}</strong></a>
                        <span class="vs"><span>{{ label(r.compare.previousYear) }} <b>{{ money(c.lastYear) }}</b></span><span>{{ label(r.year) }} <b [class.none]="!c.thisYear">{{ c.thisYear ? money(c.thisYear) : ('None yet' | t) }}</b></span></span>
                      </li>
                    }
                  </ul>
                </section>
              }
            </div>
          }

          <section class="card section" aria-labelledby="months-title">
            <div class="section-heading"><div><span class="kicker">{{ 'Month by month' | t }}</span><h2 id="months-title">{{ 'Spending and deductions' | t }}</h2></div>
              <span class="legend"><i class="sw spent"></i>{{ 'Spent' | t }} <i class="sw claimed"></i>{{ 'Claimed' | t }}</span></div>
            <div class="plot">
              <svg class="iso" [attr.viewBox]="'0 0 ' + IW() + ' ' + IH" role="img" [attr.aria-label]="'Spending and deductions by month' | t">
                <polygon class="floor" [attr.points]="iso().floor" />
                @for (line of iso().grid; track line.y) {
                  <line class="grid" [attr.x1]="line.x1" [attr.x2]="line.x2" [attr.y1]="line.y" [attr.y2]="line.y" />
                  <text class="tick" [attr.x]="line.x1 + 2" [attr.y]="line.y - 4">{{ money(line.value, true) }}</text>
                }
                @for (m of iso().bars; track m.month; let i = $index) {
                  <g class="col" [class.on]="hover() === i" [style.animation-delay.ms]="i * 45" (mouseenter)="month(i)" (mouseleave)="clear()" (click)="month(i, true)">
                    <rect class="hit" [attr.x]="m.x - 6" y="0" [attr.width]="m.w + 20" [attr.height]="IH" />
                    @if (m.empty) { <polygon class="tile" [attr.points]="m.empty" /> }
                    @if (m.low; as b) {
                      <rect class="face claimed" [attr.x]="b.front.x" [attr.y]="b.front.y" [attr.width]="b.front.w" [attr.height]="b.front.h" />
                      <polygon class="face claimed" [attr.points]="b.side" /><polygon class="shade" [attr.points]="b.side" />
                      @if (!m.high) { <polygon class="face claimed" [attr.points]="b.top" /><polygon class="light" [attr.points]="b.top" /> }
                    }
                    @if (m.high; as b) {
                      <rect class="face spent" [attr.x]="b.front.x" [attr.y]="b.front.y" [attr.width]="b.front.w" [attr.height]="b.front.h" />
                      <polygon class="face spent" [attr.points]="b.side" /><polygon class="shade" [attr.points]="b.side" />
                      <polygon class="face spent" [attr.points]="b.top" /><polygon class="light" [attr.points]="b.top" />
                    }
                  </g>
                }
              </svg>
              @if (tip()?.chart === 'months' && bars()[tip()!.index]; as m) {
                <div class="tip" role="status" [style.left.%]="tip()!.left" [style.top.%]="tip()!.top">
                  <strong>{{ m.label }}</strong>
                  <span><i class="sw spent"></i>{{ 'Spent' | t }}<b>{{ money(m.spent) }}</b></span>
                  <span><i class="sw claimed"></i>{{ 'Claimed' | t }}<b>{{ money(m.claimed) }}</b></span>
                  <small>{{ (m.receipts === 1 ? '{n} receipt' : '{n} receipts') | t: { n: m.receipts } }}@if (m.receipts) { · {{ openHint() | t }} }</small>
                </div>
              }
            </div>
            <div class="axis">@for (m of bars(); track m.month; let i = $index) { <span [class.on]="hover() === i">{{ m.label }}</span> }</div>
            <p class="readout">{{ 'Busiest month: {month}, {amount}' | t: { month: busiest().label, amount: money(busiest().spent) } }}</p>
          </section>

          <div class="pair">
            <section class="card section" aria-labelledby="kinds-title">
              <span class="kicker">{{ 'Where deductions come from' | t }}</span>
              <h2 id="kinds-title">{{ 'By kind of expense' | t }}</h2>
              @if (kinds().total > 0) {
                <div class="donut-wrap">
                  <div class="plot plot--ring">
                    <svg class="ring" viewBox="0 0 240 172" role="img" [attr.aria-label]="'Deductions by kind of expense' | t">
                      <ellipse class="ring-shadow" cx="126" cy="104" rx="104" ry="58" />
                      @for (slice of ringSlices(); track slice.key) {
                        <g class="slice-3d" [class.lift]="activeKind() === slice.key" (mouseenter)="kind(slice.key)" (mouseleave)="clear()" (click)="kind(slice.key, true)">
                          @for (d of slice.inner; track $index) { <path class="face" [attr.d]="d" [attr.fill]="slice.color" /><path class="shade deep" [attr.d]="d" /> }
                          @for (d of slice.outer; track $index) { <path class="face" [attr.d]="d" [attr.fill]="slice.color" /><path class="shade" [attr.d]="d" /> }
                          <path class="face" [attr.d]="slice.top" [attr.fill]="slice.color" /><path class="light" [attr.d]="slice.top" />
                        </g>
                      }
                    </svg>
                    @if (tip()?.chart === 'kinds' && part(tip()!.key); as k) {
                      <div class="tip" role="status" [style.left.%]="tip()!.left" [style.top.%]="tip()!.top">
                        <strong><i class="sw" [style.background]="k.color"></i>{{ k.label | t }}</strong>
                        <span>{{ 'Claimed' | t }}<b>{{ money(k.claimed) }}</b></span>
                        @if (k.ready) { <span>{{ 'Ready to approve' | t }}<b>{{ money(k.ready) }}</b></span> }
                        <small>{{ share(k.value, kinds().total) }} · {{ (k.expenses === 1 ? '{n} expense' : '{n} expenses') | t: { n: k.expenses } }} · {{ openHint() | t }}</small>
                      </div>
                    }
                  </div>
                  <ul class="kind-list">
                    <li class="kind-total"><span>{{ 'claimed + ready' | t }}</span><strong>{{ money(kinds().total) }}</strong></li>
                    @for (part of kinds().parts; track part.category) {
                      <li [class.on]="activeKind() === part.category" (mouseenter)="kind(part.category)" (mouseleave)="clear()" (click)="kind(part.category, true)"><i class="sw" [style.background]="part.color"></i><span>{{ part.label | t }}</span><strong>{{ money(part.value) }}</strong><small>{{ share(part.value, kinds().total) }}</small></li>
                    }
                  </ul>
                </div>
              } @else {
                <p class="muted small">{{ 'Nothing calculated yet. Choose the kind of expense on your receipts and it shows here.' | t }}</p>
              }
            </section>

            <section class="card section" aria-labelledby="claims-title">
              <span class="kicker">{{ 'Progress' | t }}</span>
              <h2 id="claims-title">{{ 'How your expenses stand' | t }}</h2>
              @if (r.totals.expenses) {
                <div class="plot">
                  <svg class="iso slab" [attr.viewBox]="'0 0 ' + SW + ' ' + SH" role="img" [attr.aria-label]="'How your expenses stand' | t">
                    <polygon class="floor" [attr.points]="slab().floor" />
                    @for (seg of slab().parts; track seg.key; let last = $last) {
                      <g class="seg" [class.on]="tip()?.key === seg.key" (mouseenter)="stand(seg.key)" (mouseleave)="clear()" (click)="stand(seg.key, true)">
                        <rect class="face" [attr.fill]="seg.color" [attr.x]="seg.box.front.x" [attr.y]="seg.box.front.y" [attr.width]="seg.box.front.w" [attr.height]="seg.box.front.h" />
                        <polygon class="face" [attr.fill]="seg.color" [attr.points]="seg.box.top" /><polygon class="light" [attr.points]="seg.box.top" />
                        @if (last) { <polygon class="face" [attr.fill]="seg.color" [attr.points]="seg.box.side" /><polygon class="shade" [attr.points]="seg.box.side" /> }
                      </g>
                    }
                  </svg>
                  @if (tip()?.chart === 'status' && statusPart(tip()!.key); as st) {
                    <div class="tip" role="status" [style.left.%]="tip()!.left" [style.top.%]="tip()!.top">
                      <strong><i class="sw" [style.background]="st.color"></i>{{ st.label | t }}</strong>
                      <span>{{ (st.count === 1 ? '{n} expense' : '{n} expenses') | t: { n: st.count } }}<b>{{ share(st.count, r.totals.expenses) }}</b></span>
                      <small>{{ (touch ? 'Tap again to open the tax summary' : 'Click to open the tax summary') | t }}</small>
                    </div>
                  }
                </div>
                <ul class="status-list">
                  @for (part of status(); track part.key) {
                    <li [class.zero]="!part.count" [class.on]="tip()?.key === part.key" (mouseenter)="stand(part.key)" (mouseleave)="clear()"><i class="sw" [style.background]="part.color"></i><span>{{ part.label | t }}</span><strong>{{ part.count }}</strong></li>
                  }
                </ul>
              } @else {
                <p class="muted small">{{ 'No expenses yet. Confirm a receipt and choose what it’s for.' | t }}</p>
              }
              @if (r.currencies.length > 1) {
                <h3 class="sub">{{ 'Currencies' | t }}</h3>
                <p class="chips">@for (c of r.currencies; track c.currency) { <span class="chip">{{ c.currency }} · {{ (c.receipts === 1 ? '{n} receipt' : '{n} receipts') | t: { n: c.receipts } }}</span> }</p>
              }
            </section>
          </div>

          <div class="pair">
            <section class="card section" aria-labelledby="merchants-title">
              <span class="kicker">{{ 'Merchants' | t }}</span>
              <h2 id="merchants-title">{{ 'Where you spent most' | t }}</h2>
              <ol class="hbars">
                @for (m of r.merchants; track m.merchant; let i = $index) {
                  <li [class.on]="tip()?.chart === 'merchants' && tip()!.index === i" (mouseenter)="merchant(i)" (mouseleave)="clear()" (click)="merchant(i, true)">
                    <span class="name"><b>{{ i + 1 }}</b>{{ m.merchant || ('Unknown merchant' | t) }}</span>
                    <svg class="iso bar3d" viewBox="0 0 300 26" preserveAspectRatio="xMinYMid meet" aria-hidden="true">
                      <polygon class="floor" [attr.points]="laneFloor" />
                      @if (lane(m.spent, r.merchants[0].spent); as b) {
                        <g class="grow-x" [style.animation-delay.ms]="i * 70">
                          <rect class="face merchant" [class.first]="i === 0" [attr.x]="b.front.x" [attr.y]="b.front.y" [attr.width]="b.front.w" [attr.height]="b.front.h" />
                          <polygon class="face merchant" [class.first]="i === 0" [attr.points]="b.top" /><polygon class="light" [attr.points]="b.top" />
                          <polygon class="face merchant" [class.first]="i === 0" [attr.points]="b.side" /><polygon class="shade" [attr.points]="b.side" />
                        </g>
                      }
                    </svg>
                    <span class="value">{{ money(m.spent) }}<small>{{ m.receipts }}×</small></span>
                    @if (tip()?.chart === 'merchants' && tip()!.index === i) {
                      <span class="row-tip" role="status">{{ share(m.spent, r.totals.spent) }} {{ 'of spending' | t }} · {{ 'average {amount}' | t: { amount: money(m.spent / m.receipts) } }} · {{ openHint() | t }}</span>
                    }
                  </li>
                }
              </ol>
            </section>

            <section class="card section" aria-labelledby="years-title">
              <span class="kicker">{{ 'Year by year' | t }}</span>
              <h2 id="years-title">{{ 'Deductions claimed each year' | t }}</h2>
              <div class="plot">
                <svg class="iso iso--years" [attr.viewBox]="'0 0 ' + YW + ' ' + YH" role="img" [attr.aria-label]="'Deductions claimed each year' | t">
                  <polygon class="floor" [attr.points]="yearsIso().floor" />
                  @for (y of yearsIso().bars; track y.year; let i = $index) {
                    <g class="col" [class.on]="tip()?.chart === 'years' && tip()!.index === i" [style.animation-delay.ms]="i * 80" (mouseenter)="yearTip(i)" (mouseleave)="clear()" (click)="yearTip(i, true)">
                      <rect class="face" [class.claimed]="y.current" [class.past]="!y.current" [attr.x]="y.box.front.x" [attr.y]="y.box.front.y" [attr.width]="y.box.front.w" [attr.height]="y.box.front.h" />
                      <polygon class="face" [class.claimed]="y.current" [class.past]="!y.current" [attr.points]="y.box.side" /><polygon class="shade" [attr.points]="y.box.side" />
                      <polygon class="face" [class.claimed]="y.current" [class.past]="!y.current" [attr.points]="y.box.top" /><polygon class="light" [attr.points]="y.box.top" />
                    </g>
                    <text class="col-value-3d" [attr.x]="y.labelX" [attr.y]="y.valueY">{{ money(y.claimed, true) }}</text>
                    <text class="col-label-3d" [attr.x]="y.labelX - 6" [attr.y]="YH - 6">{{ label(y.year) }}</text>
                  }
                </svg>
                @if (tip()?.chart === 'years' && r.years[tip()!.index]; as y) {
                  <div class="tip" role="status" [style.left.%]="tip()!.left" [style.top.%]="tip()!.top">
                    <strong>{{ label(y.year) }}</strong>
                    <span><i class="sw claimed"></i>{{ 'Claimed' | t }}<b>{{ money(y.claimed) }}</b></span>
                    <span><i class="sw spent"></i>{{ 'Expenses' | t }}<b>{{ money(y.spent) }}</b></span>
                    @if (change(tip()!.index); as c) { <small>{{ c > 0 ? '▲' : '▼' }} {{ (c > 0 ? '{n}% more than the year before' : '{n}% less than the year before') | t: { n: abs(c) } }}</small> }
                    <small>{{ (touch ? 'Tap again to open its tax packs' : 'Click to open its tax packs') | t }}</small>
                  </div>
                }
              </div>
              @if (r.years.length === 1) { <p class="muted small">{{ 'Earlier years appear here once they have receipts.' | t }}</p> }
            </section>
          </div>
        }
      } @else if (!message()) {
        <p class="muted">{{ 'Loading reports…' | t }}</p>
      }
    </div>
  `,
  styles: `
    .kpis { display: grid; gap: 12px; grid-template-columns: repeat(4, minmax(0, 1fr)); margin-bottom: 20px; }
    .kpi { border: 2px solid var(--ink); border-radius: 18px; box-shadow: 4px 4px 0 var(--ink); color: var(--ink); display: grid; gap: 2px; padding: 14px 16px; text-decoration: none; transition: transform .15s, box-shadow .15s; }
    .kpi:hover { box-shadow: 6px 6px 0 var(--ink); transform: translate(-2px, -2px); }
    .kpi small { color: var(--ink-soft); font-size: 12px; font-weight: 700; }
    .kpi strong { font-size: 26px; font-variant-numeric: tabular-nums; letter-spacing: -.02em; }
    .kpi span { font-size: 12px; font-weight: 700; }
    .tone-spent { background: var(--sky-soft); }
    .tone-claimed { background: var(--mint-soft); }
    .tone-ready { background: var(--purple-soft); }
    .tone-check { background: var(--yellow-soft); }
    .estimate { align-items: center; display: flex; gap: 16px; margin-bottom: 20px; }
    .estimate-icon { align-items: center; background: var(--mint); border: 2px solid var(--ink); border-radius: 14px; box-shadow: 3px 3px 0 var(--ink); display: flex; flex: none; font-size: 24px; font-weight: 800; height: 52px; justify-content: center; width: 52px; }
    .estimate-copy { flex: 1; min-width: 0; }
    .estimate h2 { font-size: 22px; margin: 2px 0 4px; }
    .estimate p { margin: 0; }
    .insight .insights { display: grid; gap: 10px; list-style: none; margin: 10px 0 12px; padding: 0; }
    .insights li { background: var(--white); border: 1px solid var(--line); border-radius: 12px; display: grid; gap: 6px; padding: 10px 12px; }
    .insight-head { align-items: center; color: var(--ink); display: flex; gap: 8px; justify-content: space-between; text-decoration: none; }
    .insight-head span { color: var(--ink-soft); font-size: 12px; font-weight: 700; }
    .insight-head:hover strong { text-decoration: underline; }
    .insights small { color: var(--ink-soft); font-size: 12px; }
    .months { display: flex; flex-wrap: wrap; gap: 6px; }
    .month-chip { background: var(--orange-soft); border: 1.5px dashed var(--orange-ink); border-radius: 8px; color: var(--orange-ink); font-size: 12px; font-weight: 800; padding: 2px 8px; }
    .compare .insight-head { justify-content: flex-start; }
    .vs { display: flex; flex-wrap: wrap; font-size: 13px; gap: 6px 18px; }
    .vs b { font-variant-numeric: tabular-nums; margin-left: 4px; }
    .vs b.none { color: var(--orange-ink); }
    .legend { align-items: center; display: flex; font-size: 12px; font-weight: 700; gap: 6px; }
    .sw { border-radius: 3px; display: inline-block; flex: none; height: 12px; width: 12px; }
    .sw.spent { background: var(--sky); }
    .sw.claimed { background: var(--mint-ink); }
    .legend .sw { margin-left: 8px; }
    .grid { stroke: var(--line); stroke-dasharray: 3 4; stroke-width: 1; vector-effect: non-scaling-stroke; }
    .hit { cursor: pointer; fill: transparent; }
    .axis { display: grid; font-size: 11px; font-weight: 700; grid-template-columns: repeat(12, 1fr); margin-top: 6px; padding-right: 2.3%; text-align: center; }
    .axis span { color: var(--ink-soft); }
    .axis span.on { color: var(--ink); }
    .readout { font-size: 13px; margin: 10px 0 0; }
    .pair { display: grid; gap: 20px; grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .pair > .card { margin-top: 20px; }
    .pair:first-of-type > .card { margin-top: 0; }
    .donut-wrap { align-items: center; display: grid; gap: 16px; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); margin-top: 8px; }
    .kind-list, .status-list, .hbars { display: grid; gap: 8px; list-style: none; margin: 0; padding: 0; }
    .kind-list li, .status-list li { align-items: center; border-radius: 8px; cursor: pointer; display: grid; font-size: 13px; gap: 8px; grid-template-columns: 12px minmax(0, 1fr) auto auto; padding: 2px 4px; transition: background .15s; }
    .kind-list li.on, .status-list li.on { background: var(--yellow-soft); }
    .kind-list strong, .status-list strong { font-variant-numeric: tabular-nums; }
    .kind-list small { color: var(--ink-soft); font-size: 11px; min-width: 34px; text-align: right; }
    .kind-list .kind-total { border-bottom: 1px dashed var(--line); cursor: default; grid-template-columns: minmax(0, 1fr) auto; padding-bottom: 6px; }
    .kind-total span { color: var(--ink-soft); font-size: 12px; font-weight: 700; }
    .kind-total strong { font-size: 18px; }
    .status-list li { grid-template-columns: 12px minmax(0, 1fr) auto; }
    .status-list .zero { opacity: .45; }
    .sub { font-size: 14px; margin: 16px 0 6px; }
    .chips { display: flex; flex-wrap: wrap; gap: 6px; margin: 0; }
    .hbars { margin-top: 10px; }
    .hbars li { align-items: center; border-radius: 10px; cursor: pointer; display: grid; font-size: 13px; gap: 10px; grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr) auto; padding: 4px 6px; transition: background .15s; }
    .hbars li.on { background: var(--yellow-soft); }
    .hbars .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .hbars b { color: var(--ink-soft); display: inline-block; font-size: 11px; width: 18px; }
    .hbars .value { font-variant-numeric: tabular-nums; font-weight: 800; text-align: right; }
    .hbars .value small { color: var(--ink-soft); font-size: 11px; font-weight: 700; margin-left: 6px; }
    .row-tip { color: var(--ink-soft); font-size: 12px; font-weight: 700; grid-column: 1 / -1; }
    .empty-state { display: grid; justify-items: center; padding: 40px 24px; text-align: center; }
    @media (max-width: 860px) {
      .kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .pair { gap: 0; grid-template-columns: minmax(0, 1fr); }
      .pair > .card { margin-top: 20px; }
      .estimate { align-items: flex-start; flex-wrap: wrap; }
    }
    @media (max-width: 480px) {
      .kpi strong { font-size: 21px; }
      .donut-wrap { grid-template-columns: minmax(0, 1fr); justify-items: center; }
      .kind-list { width: 100%; }
      .axis { font-size: 9px; }
      .hbars li { grid-template-columns: minmax(0, 1fr) auto; }
      .hbars .bar3d { grid-column: 1 / -1; grid-row: 2; }
      .estimate-icon { display: none; }
    }
  `,
})
export class ReportsPage {
  readonly session = inject(Session);
  private readonly http = inject(HttpClient);
  private readonly i18n = inject(I18n);
  private readonly router = inject(Router);
  readonly label = yearLabel;
  readonly report = signal<Report | null>(null);
  readonly message = signal('');
  /** Phones and tablets: a tap shows an item's figures, a second tap opens it. With a mouse, hover shows and click opens. */
  readonly touch = typeof matchMedia === 'function' && !matchMedia('(hover: hover)').matches;
  readonly tip = signal<{ chart: Chart; index: number; key: string; left: number; top: number; pinned: boolean } | null>(null);
  readonly activeKind = computed(() => (this.tip()?.chart === 'kinds' ? this.tip()!.key : null));
  readonly hover = computed(() => (this.tip()?.chart === 'months' ? this.tip()!.index : null));
  readonly openHint = computed(() => (this.touch ? 'tap again to see the receipts' : 'click to see the receipts'));

  readonly IH = 250;
  readonly SW = 600;
  readonly SH = 64;
  readonly YW = 360;
  readonly YH = 230;
  private readonly DX = 14;
  private readonly DY = -9;
  readonly laneFloor = floor(0, 22, 288, 9, -6);
  /** Phones get a narrower, relatively taller monthly drawing so the blocks and labels stay readable. */
  private readonly narrow = signal(typeof matchMedia === 'function' && matchMedia('(max-width: 600px)').matches);
  readonly IW = computed(() => (this.narrow() ? 360 : 600));

  /** The chosen yearly-income band for the tax estimate (remembered on this device). */
  readonly bracket = signal(savedBracket());

  /** Bars scaled to the busiest month, rounded up to a tidy number. */
  private readonly top = computed(() => tidy(Math.max(1, ...(this.report()?.months ?? []).map((m) => m.spent))));
  readonly bars = computed(() => (this.report()?.months ?? []).map((m) => ({ ...m, label: this.monthName(m.month) })));
  readonly busiest = computed(() => this.bars().reduce((a, b) => (b.spent > a.spent ? b : a), this.bars()[0]));
  readonly kinds = computed(() => {
    const parts = (this.report()?.categories ?? []).map((c) => ({ category: c.category, value: c.claimed + c.ready }))
      .filter((c) => c.value > 0).map((c) => ({ ...c, label: categoryLabel(c.category), color: this.colour(c.category) }));
    return { total: parts.reduce((sum, c) => sum + c.value, 0), parts };
  });
  /** Each month as a block: the claimed part at the bottom, the rest of the spending above it. */
  readonly iso = computed(() => {
    const base = this.IH - 12;
    const max = base - 18;
    const left = 4;
    const width = this.IW() - this.DX - left * 2;
    const slot = width / 12;
    const w = slot * 0.56;
    const scale = (v: number) => (v / this.top()) * max;
    const bars = this.bars().map((m, i) => {
      const x = left + i * slot + (slot - w) / 2;
      const spent = scale(m.spent);
      const claimed = Math.min(spent, scale(m.claimed));
      return {
        ...m, x, w,
        low: claimed > 0.5 ? box(x, base, w, claimed, this.DX, this.DY) : null,
        high: spent - claimed > 0.5 ? box(x, base - claimed, w, spent - claimed, this.DX, this.DY) : null,
        empty: spent <= 0.5 ? box(x, base, w, 0, this.DX, this.DY).top : null,
      };
    });
    const grid = [0.25, 0.5, 0.75, 1].map((f) => ({ y: base - f * max + this.DY, x1: left + this.DX, x2: left + width + this.DX, value: this.top() * f }));
    return { bars, grid, floor: floor(left, base, width, this.DX, this.DY) };
  });
  readonly ringSlices = computed(() =>
    ring(this.kinds().parts.map((p) => ({ key: p.category, value: p.value, color: p.color })), { cx: 120, cy: 78, rx: 104, ry: 58, irx: 50, iry: 28, depth: 24 }));
  readonly maxYear = computed(() => Math.max(1, ...(this.report()?.years ?? []).map((y) => y.claimed)));
  readonly yearsIso = computed(() => {
    const years = this.report()?.years ?? [];
    const base = this.YH - 30;
    const max = base - 34;
    const slots = Math.max(years.length, 4);
    const slot = (this.YW - this.DX - 8) / slots;
    const w = Math.min(70, slot * 0.6);
    const offset = 4 + ((slots - years.length) * slot) / 2;
    const bars = years.map((y, i) => {
      const x = offset + i * slot + (slot - w) / 2;
      const h = Math.max(3, (y.claimed / this.maxYear()) * max);
      return { ...y, current: y.year === this.report()?.year, box: box(x, base, w, h, this.DX, this.DY), labelX: x + w / 2 + this.DX / 2, valueY: base - h + this.DY - 8 };
    });
    return { bars, floor: floor(4, base, this.YW - this.DX - 8, this.DX, this.DY) };
  });
  readonly status = computed(() => statusParts.map((p) => ({ ...p, count: this.report()?.claims[p.key] ?? 0 })));
  /** The progress slab: one block per state, as wide as its share of the expenses. */
  readonly slab = computed(() => {
    const parts = this.status().filter((p) => p.count > 0);
    const total = parts.reduce((sum, p) => sum + p.count, 0);
    const width = this.SW - this.DX - 4;
    const base = this.SH - 8;
    let x = 2;
    return {
      floor: floor(2, base + 4, width, this.DX, this.DY),
      parts: parts.map((p) => {
        const w = total ? (p.count / total) * width : 0;
        const part = { ...p, box: box(x, base, w, 26, this.DX, this.DY) };
        x += w;
        return part;
      }),
    };
  });

  /** Deductions that count (claimed and ready to approve), and roughly what they save at the chosen rate. */
  readonly deductible = computed(() => (this.report()?.totals.claimed ?? 0) + (this.report()?.totals.ready ?? 0));
  readonly bracketOptions = computed(() => {
    const rates = this.report()?.rates;
    const options = [{ value: '', label: this.i18n.t('Choose…') }];
    for (const [i, b] of (rates?.brackets ?? []).entries()) {
      const range = b.to === null ? this.i18n.t('Over {from}', { from: whole(b.from) }) : b.from === 0 ? this.i18n.t('Up to {to}', { to: whole(b.to) }) : `${whole(b.from + 1)} – ${whole(b.to)}`;
      options.push({ value: String(i), label: `${range} · ${Math.round(b.rate * 100)}%` });
    }
    return options;
  });
  private readonly chosen = computed(() => {
    const rates = this.report()?.rates;
    const bracket = rates?.brackets[Number(this.bracket())];
    return this.bracket() !== '' && rates && bracket ? { bracket, levy: rates.medicareLevy } : null;
  });
  readonly saving = computed(() => {
    const chosen = this.chosen();
    if (!chosen) return null;
    return chosen.bracket.rate === 0 ? 0 : Math.round(this.deductible() * (chosen.bracket.rate + chosen.levy) * 100) / 100;
  });
  readonly ratePercent = computed(() => {
    const chosen = this.chosen();
    return chosen ? `${Math.round((chosen.bracket.rate + (chosen.bracket.rate ? chosen.levy : 0)) * 100)}%` : '';
  });

  constructor() {
    if (typeof matchMedia === 'function') {
      const query = matchMedia('(max-width: 600px)');
      const changed = (e: MediaQueryListEvent) => this.narrow.set(e.matches);
      query.addEventListener?.('change', changed);
      inject(DestroyRef).onDestroy(() => query.removeEventListener?.('change', changed));
    }
    effect(() => {
      const year = this.session.account()?.financialYearStart;
      if (year) void this.load(year);
    });
  }

  async load(year: number) {
    this.message.set('');
    this.tip.set(null);
    try {
      const report = await firstValueFrom(this.http.get<Report>(`/api/reports/${year}`));
      if (year === this.session.account()?.financialYearStart) this.report.set(report);
    } catch {
      this.message.set('We couldn’t load your reports. Refresh to try again.');
    }
  }

  /**
   * Hover shows an item's tooltip. Choosing it opens what it stands for: straight away with a mouse; on a touch screen
   * the first tap shows the figures and a second tap on the same item opens it.
   */
  private show(chart: Chart, index: number, key: string, x: number, y: number, chosen: boolean) {
    const current = this.tip();
    const same = current?.chart === chart && current.key === key;
    if (chosen && (!this.touch || (same && current?.pinned))) { this.open(chart, index, key); return; }
    if (!chosen && current?.pinned) return;
    this.tip.set({ chart, index, key, left: Math.min(88, Math.max(12, x)), top: Math.max(4, y), pinned: chosen });
  }
  clear() {
    if (!this.tip()?.pinned) this.tip.set(null);
  }
  private open(chart: Chart, index: number, key: string) {
    const r = this.report();
    if (!r) return;
    this.tip.set(null);
    switch (chart) {
      case 'months': if (r.months[index]?.receipts) void this.router.navigate(['/review'], { queryParams: { year: r.year, month: r.months[index].month } }); break;
      case 'kinds': void this.router.navigate(['/review'], { queryParams: { year: r.year, category: key } }); break;
      case 'merchants': void this.router.navigate(['/review'], { queryParams: { year: r.year, merchant: r.merchants[index].merchant } }); break;
      case 'status': void this.router.navigate(['/summary']); break;
      case 'years': void this.router.navigate(['/history'], { queryParams: { year: r.years[index].year } }); break;
    }
  }

  month(i: number, chosen = false) {
    const m = this.iso().bars[i];
    const top = (m.high ?? m.low)?.front.y ?? this.IH - 12;
    this.show('months', i, String(i), ((m.x + m.w / 2 + this.DX / 2) / this.IW()) * 100, ((top + this.DY) / this.IH) * 100, chosen);
  }
  kind(key: string, chosen = false) {
    const slice = this.ringSlices().find((s) => s.key === key);
    if (slice) this.show('kinds', 0, key, (slice.mid.x / 240) * 100, ((slice.mid.y - 8) / 172) * 100, chosen);
  }
  part(key: string) {
    const part = this.kinds().parts.find((p) => p.category === key);
    const source = this.report()?.categories.find((c) => c.category === key);
    return part && source ? { ...part, claimed: source.claimed, ready: source.ready, expenses: source.expenses } : null;
  }
  stand(key: string, chosen = false) {
    const seg = this.slab().parts.find((p) => p.key === key);
    if (seg) this.show('status', 0, key, ((seg.box.front.x + seg.box.front.w / 2 + this.DX / 2) / this.SW) * 100, ((seg.box.front.y + this.DY) / this.SH) * 100, chosen);
  }
  statusPart(key: string) {
    return this.status().find((p) => p.key === key) ?? null;
  }
  merchant(i: number, chosen = false) {
    this.show('merchants', i, String(i), 50, 0, chosen);
  }
  yearTip(i: number, chosen = false) {
    const y = this.yearsIso().bars[i];
    this.show('years', i, String(i), (y.labelX / this.YW) * 100, ((y.valueY - 10) / this.YH) * 100, chosen);
  }
  /** Change in claimed deductions from the year before, in whole per cent. */
  change(i: number) {
    const years = this.report()?.years ?? [];
    if (i < 1 || !years[i - 1]?.claimed) return null;
    return Math.round(((years[i].claimed - years[i - 1].claimed) / years[i - 1].claimed) * 100) || null;
  }
  abs(n: number) { return Math.abs(n); }
  /** A merchant's bar lying down, as long as its share of the top merchant. */
  lane(value: number, max: number) {
    return box(0, 22, max > 0 ? Math.max(6, (value / max) * 276) : 6, 13, 9, -6);
  }
  setBracket(value: string) {
    this.bracket.set(value);
    try { localStorage.setItem(bracketKey, value); } catch { /* remembered for this visit only */ }
  }

  monthName(month: string) {
    const m = Number(month.slice(5));
    return this.i18n.language() === 'zh' ? `${m}月` : monthsEn[m - 1];
  }
  kindName(category: string) { return categoryLabel(category); }
  colour(category: string) { return categories[category]?.color ?? 'var(--ink-soft)'; }
  money(value: number, compact = false) {
    if (compact && Math.abs(value) >= 10000) return '$' + (value / 1000).toFixed(value >= 100000 ? 0 : 1) + 'k';
    return '$' + value.toLocaleString('en-AU', { minimumFractionDigits: compact ? 0 : 2, maximumFractionDigits: compact ? 0 : 2 });
  }
  share(part: number, whole: number) {
    return whole > 0 ? Math.round((part / whole) * 100) + '%' : '0%';
  }
}

function whole(n: number) {
  return '$' + n.toLocaleString('en-AU', { maximumFractionDigits: 0 });
}
function savedBracket() {
  try { return localStorage.getItem(bracketKey) ?? ''; } catch { return ''; }
}
function tidy(value: number) {
  const step = 10 ** Math.floor(Math.log10(value));
  return Math.ceil(value / step) * step;
}
