import { Component, inject, input, output } from '@angular/core';
import { CurrencyPipe, DatePipe, NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Icon } from '../core/icon';
import { Session, yearLabel } from '../core/session';
import { skipReasonLabel } from '../core/skip';
import { TranslatePipe } from '../core/i18n';

export interface ReceiptSummary {
  id: string;
  originalFileName: string;
  relativePath: string;
  contentType: string;
  size: number;
  status: string;
  createdAt: string;
  reviewState: 'NeedsReview' | 'Confirmed' | 'Processing' | 'Blocked' | 'NotClaimed';
  merchant: string | null;
  date: string | null;
  amount: number | null;
  aiRead: boolean;
  skipReason?: string | null;
}
export interface ReceiptPage {
  items: ReceiptSummary[];
  total: number;
  page: number;
  pageSize: number;
  counts: { total: number; needsReview: number; processing: number; confirmed: number; blocked: number; notClaimed: number };
}

const states = {
  NeedsReview: { label: 'Check details', chip: 'chip--review', tone: 'tone-orange' },
  Confirmed: { label: 'Confirmed', chip: 'chip--done', tone: 'tone-mint' },
  Processing: { label: 'Reading…', chip: 'chip--info', tone: 'tone-blue' },
  Blocked: { label: 'Blocked', chip: '', tone: 'tone-yellow' },
  NotClaimed: { label: 'Skipped', chip: '', tone: 'tone-yellow' },
} as const;

@Component({
  selector: 'app-receipt-list',
  imports: [TranslatePipe, CurrencyPipe, DatePipe, NgTemplateOutlet, RouterLink, Icon],
  styles: `.selectable { cursor: pointer; grid-template-columns: 22px 44px minmax(0, 1fr) auto; }
    .selectable input { accent-color: var(--ink); height: 20px; width: 20px; }
    .is-selected { box-shadow: 0 0 0 3px var(--orange) inset; }
    .other-year { background: var(--yellow-soft); border-radius: 4px; color: var(--ink); font-weight: 700; margin-right: 4px; padding: 0 5px; }`,
  template: `
    <ul class="item-list">
      @for (receipt of items(); track receipt.id) {
        <li>
          @if (selecting()) {
            <!-- Selecting: a press ticks the receipt instead of opening it. -->
            <label class="item selectable" [class.is-selected]="selected().has(receipt.id)">
              <input type="checkbox" [checked]="selected().has(receipt.id)" (change)="toggled.emit(receipt.id)" [attr.aria-label]="'Select {name}' | t: { name: receipt.merchant || receipt.originalFileName }" />
              <ng-container *ngTemplateOutlet="row; context: { $implicit: receipt }" />
            </label>
          } @else if (receipt.reviewState === 'Blocked') {
            <div class="item" [title]="'Malware scanning blocked this file. It cannot be opened.' | t">
              <ng-container *ngTemplateOutlet="row; context: { $implicit: receipt }" />
            </div>
          } @else {
            <a class="item" [class.is-active]="receipt.id === activeId()" [routerLink]="['/review', receipt.id]"
               [attr.aria-current]="receipt.id === activeId() ? 'true' : null">
              <ng-container *ngTemplateOutlet="row; context: { $implicit: receipt }" />
            </a>
          }
        </li>
      }
    </ul>
    <ng-template #row let-receipt>
      <span class="thumb" [class]="state(receipt).tone">
        <app-icon [name]="receipt.contentType === 'application/pdf' ? 'file' : 'image'" />
      </span>
      <span class="item__copy">
        <strong>{{ receipt.merchant || receipt.originalFileName }}</strong>
        <small>
          @if (otherYear(receipt); as fy) { <span class="other-year">{{ fy }}</span> }
          @if (receipt.date) { {{ receipt.date | date: ('d MMM yyyy' | t) }} · }
          {{ receipt.merchant ? receipt.originalFileName : ('Added {date}' | t: { date: (receipt.createdAt | date: ('d MMM' | t)) }) }}
        </small>
      </span>
      <span class="item__end">
        @if (receipt.amount !== null) { {{ receipt.amount | currency: ('AUD' | t) : 'symbol-narrow' }} }
        <span class="chip" [class]="chip(receipt).css">
          @if (receipt.reviewState === 'Processing') { <span class="spinner" aria-hidden="true"></span> }
          @else if (receipt.reviewState === 'NeedsReview' && receipt.aiRead) { <app-icon name="spark" [size]="12" /> }
          {{ chip(receipt).label | t }}</span>
      </span>
    </ng-template>
  `,
})
export class ReceiptList {
  private readonly session = inject(Session);
  /** Receipts count in the financial year of their date; flag ones outside the year being worked on. */
  otherYear(receipt: ReceiptSummary) {
    const [y, m] = (receipt.date ?? '').split('-').map(Number);
    const selected = this.session.account()?.financialYearStart;
    if (!y || !m || selected === undefined) return null;
    const start = m >= 7 ? y : y - 1;
    return start === selected ? null : yearLabel(start);
  }
  readonly items = input.required<ReceiptSummary[]>();
  readonly activeId = input<string | null>(null);
  /** Selection mode for acting on several receipts at once. */
  readonly selecting = input(false);
  readonly selected = input<ReadonlySet<string>>(new Set());
  readonly toggled = output<string>();
  /** What the person needs to do next, at a glance: check AI's reading, type details, or wait. */
  chip(receipt: ReceiptSummary) {
    if (receipt.reviewState === 'NeedsReview') return receipt.aiRead ? { label: 'AI read · check', css: 'chip--purple' } : { label: 'Type details', css: 'chip--review' };
    if (receipt.reviewState === 'Processing') return { label: 'Reading…', css: 'chip--info' };
    if (receipt.reviewState === 'NotClaimed') return { label: 'Skipped' + (skipReasonLabel(receipt.skipReason) ? ' · ' + skipReasonLabel(receipt.skipReason) : ''), css: '' };
    return { label: this.state(receipt).label, css: this.state(receipt).chip };
  }
  state(receipt: ReceiptSummary) {
    return states[receipt.reviewState] ?? states.Processing;
  }
}
