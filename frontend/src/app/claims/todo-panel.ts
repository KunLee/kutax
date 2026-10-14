import { Component, computed, input, output } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Icon } from '../core/icon';
import { showAttention } from '../core/attention';
import { TranslatePipe } from '../core/i18n';

export interface TodoRow { allocationId: string; documentId: string; description: string; status: string; reason: string; }
export interface TodoIssue { documentId: string; description: string; amount: number | null; }
/** One thing waiting on the person: approving the calculated claims, a claim to fix, or a receipt exception. */
export type Todo = { kind: 'approve'; key: string } | { kind: 'row'; key: string; row: TodoRow } | { kind: 'issue'; key: string; issue: TodoIssue };

const statusLabels: Record<string, string> = {
  Approved: 'Approved', Candidate: 'Ready to approve', UnverifiedRule: 'Rule not checked yet', Unsupported: 'Needs advice',
  NeedsReview: 'Needs details', NeedsEvidence: 'Needs evidence', Excluded: 'Excluded',
};
export function claimStatusLabel(status: string) {
  return statusLabels[status] ?? status;
}

/**
 * The to-do list on the tax summary. As a "tile" it sits in the top row beside the totals and shows the three most
 * urgent items on one line each; "all" is the full list, full width under that row, so no single column grows.
 * Class names are prefixed because the global stylesheet already styles `.item`.
 */
@Component({
  selector: 'app-todo-panel',
  imports: [TranslatePipe, CurrencyPipe, FormsModule, RouterLink, Icon],
  host: { '[class.tile]': "mode() === 'tile'" },
  template: `
    <div class="todo-head">
      @if (mode() === 'tile') { <span class="todo-badge" aria-hidden="true">{{ todos().length }}</span> }
      <div><span class="kicker">{{ 'Waiting for you' | t }}</span>
        <h2 [id]="mode() + '-todo-title'">{{ mode() === 'tile' ? ((todos().length === 1 ? '{n} thing to do' : '{n} things to do') | t: { n: todos().length }) : ('All {n} things to do' | t: { n: todos().length }) }}</h2></div>
    </div>
    <ul [class]="mode() === 'tile' ? 'todo-list' : 'todo-grid'" [attr.aria-labelledby]="mode() + '-todo-title'">
      @for (item of shown(); track item.key) {
        @switch (item.kind) {
          @case ('approve') {
            <li class="todo-approve">
              <div class="todo-item"><app-icon name="check" [size]="18" /><span><strong>{{ (pending() === 1 ? 'Approve {n} claim' : 'Approve {n} claims at once') | t: { n: pending() } }}</strong><small>{{ total() | currency: ('AUD' | t) : 'symbol-narrow' }} · <button class="link" type="button" (click)="show.emit('Candidate')">{{ 'See them' | t }}</button></small></span>
                <button class="btn btn--primary btn--small" type="button" (click)="approveClicked()" [disabled]="busy()" [attr.aria-busy]="busy()">{{ (pending() === 1 ? 'Approve it' : 'Approve all {n}') | t: { n: pending() } }}</button></div>
              <label class="check small"><input [id]="checkboxId()" type="checkbox" [(ngModel)]="confirmed"><span>{{ (mode() === 'tile' ? 'I checked these claims.' : 'I checked their evidence, calculations, conditions, duplicates and myTax prefill.') | t }}</span></label>
            </li>
          }
          @case ('row') {
            <li class="todo-item"><app-icon name="warning" [size]="18" /><span [title]="item.row.description + ' · ' + (item.row.reason | t)"><strong>{{ item.row.description }}</strong><small>{{ statusLabel(item.row.status) | t }} · {{ item.row.reason | t }}</small></span>
              <a class="btn btn--small" [routerLink]="['/review', item.row.documentId]">{{ 'Fix' | t }}<span class="sr-only"> {{ item.row.description }}</span></a></li>
          }
          @case ('issue') {
            <li class="todo-item"><app-icon name="warning" [size]="18" /><span [title]="item.issue.description | t"><strong>{{ item.issue.description | t }}</strong>@if (item.issue.amount !== null) {<small>{{ item.issue.amount | currency: ('AUD' | t) : 'symbol-narrow' }}</small>}</span>
              <a class="btn btn--small" [routerLink]="['/review', item.issue.documentId]">{{ 'Fix' | t }}</a></li>
          }
        }
      }
    </ul>
    @if (mode() === 'tile') {
      <!-- One foot line: the way to the rest when there's more, otherwise what comes next. -->
      @if (todos().length > 3) {
        <button class="todo-foot todo-more" type="button" (click)="more.emit()">{{ 'And {n} more · see all' | t: { n: todos().length - 3 } }}<app-icon name="arrow" [size]="16" /></button>
      } @else {
        <p class="todo-foot"><app-icon name="report" [size]="18" />{{ 'Clear these and your tax pack is ready to prepare.' | t }}</p>
      }
    }
  `,
  styles: `
    :host { display: block; }
    :host(.tile) { background: var(--yellow); border: 2px solid var(--ink); border-radius: var(--radius); box-shadow: 5px 5px 0 var(--ink); display: flex; flex-direction: column; min-height: 0; padding: 18px; }
    :host(.flash) { box-shadow: 0 0 0 4px var(--orange), 5px 5px 0 var(--ink); }
    :host(:not(.tile)) { border-color: var(--orange-ink); scroll-margin-top: 96px; transition: box-shadow .3s; }
    .todo-head { align-items: center; display: flex; gap: 12px; margin-bottom: 10px; }
    .todo-head h2 { margin: 2px 0 0; }
    :host(.tile) .todo-head h2 { font-size: 20px; }
    .todo-badge { align-items: center; background: var(--orange); border: 2px solid var(--ink); border-radius: 14px; box-shadow: 3px 3px 0 var(--ink); color: var(--ink); display: flex; flex: none; font-size: 22px; font-weight: 800; height: 46px; justify-content: center; width: 46px; }
    ul { display: grid; gap: 8px; list-style: none; margin: 0; padding: 0; }
    .todo-list { margin-bottom: 12px; }
    .todo-grid { align-items: start; gap: 10px; grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .todo-grid .todo-approve { grid-column: 1 / -1; }
    .todo-grid .todo-item { background: var(--yellow-soft); }
    .todo-item { align-items: center; background: var(--white); border: 1.5px solid var(--ink); border-radius: 12px; display: grid; gap: 10px; grid-template-columns: 18px minmax(0, 1fr) auto; padding: 9px 10px; }
    .todo-item strong { display: block; font-size: 14px; overflow-wrap: anywhere; }
    .todo-item small { color: var(--ink-soft); display: block; font-size: 12px; }
    .todo-approve { display: grid; gap: 6px; }
    .todo-approve .todo-item { border: 2px solid var(--orange-ink); }
    .todo-approve .check { font-size: 12px; padding: 0 4px; }
    /* In the tile: smaller type and one line each (full text on hover), so three fit beside the totals. */
    :host(.tile) .todo-item { padding: 6px 8px; }
    :host(.tile) .todo-item :is(strong, small) { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    :host(.tile) .todo-item strong { font-size: 13px; }
    :host(.tile) .todo-item small { font-size: 11.5px; }
    :host(.tile) .todo-item .btn { font-size: 12px; min-height: 30px; padding: 0 10px; }
    .todo-foot { align-items: center; border: 0; border-top: 1.5px dashed var(--ink); color: var(--ink); display: flex; font-size: 13px; font-weight: 700; gap: 8px; margin: auto 0 0; padding: 12px 0 0; }
    .todo-more { background: none; cursor: pointer; font-family: inherit; font-weight: 800; text-align: left; width: 100%; }
    .todo-more:hover { text-decoration: underline; }
    .link { background: none; border: 0; color: var(--ink); cursor: pointer; font: inherit; font-weight: 700; padding: 0; text-decoration: underline; }
    @media (max-width: 700px) { .todo-grid { grid-template-columns: 1fr; } }
  `,
})
export class TodoPanel {
  readonly mode = input<'tile' | 'all'>('tile');
  readonly todos = input.required<Todo[]>();
  readonly pending = input(0);
  readonly total = input(0);
  readonly busy = input(false);
  /** Fired only after the person ticked the confirmation. */
  readonly approve = output<void>();
  readonly more = output<void>();
  /** Show the breakdown filtered to one status group. */
  readonly show = output<string>();
  confirmed = false;
  readonly shown = computed(() => (this.mode() === 'tile' ? this.todos().slice(0, 3) : this.todos()));
  readonly checkboxId = computed(() => 'approve-all-' + this.mode());
  readonly statusLabel = claimStatusLabel;

  approveClicked() {
    if (!this.confirmed) {
      showAttention(this.checkboxId(), 'Tick this to confirm you checked these claims.');
      return;
    }
    this.approve.emit();
    this.confirmed = false;
  }
}
