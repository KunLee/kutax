import { Component, computed, effect, inject, input, OnDestroy, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { yearLabel } from '../core/session';
import { Icon } from '../core/icon';
import { categoryLabel } from '../core/categories';
import { links, myTaxOtherWorkExpenses } from '../core/links';
import { MyTaxSection, progressFor, sectionsOf } from '../core/mytax';
import { PackFiles } from '../claims/pack-files';
import { TranslatePipe } from '../core/i18n';

interface Evidence { id: string; name: string; }
interface Row { allocationId: string; documentId: string; merchant: string; date: string; description: string; category: string; expenseAmount: number;
  workUsePercent: number; workReimbursement: number; status: string; deductibleAmount: number | null; reason: string; ruleVersion: string | null; evidence: Evidence[];
  originalCurrency?: string | null; originalAmount?: number | null; conversionSource?: string | null; conversionRateDate?: string | null; }
interface Pack {
  id: string; sha256: string; approvedAt: string; status: string; errorCode: string | null;
  model: { financialYearStart: number; approvalNote: string; evidence: Evidence[];
    assessment: { rows: Row[]; issues: { description: string; amount: number | null }[]; approvedTotal: number; candidateTotal: number; excludedExpenseTotal: number; unassessedExpenseTotal: number; entryAmount: number; sections?: MyTaxSection[] };
    rules: { rule: { version: string; category: string; section: string; paperReference: string; sourceUrl: string; mappingUrl: string }; reviewedAt: string }[]; };
  progress: { section: string; status: string; updatedAt: string }[];
  files: { kind: string; fileName: string; size: number; sha256: string }[];
}

@Component({
  selector: 'app-history-detail-page',
  imports: [TranslatePipe, CurrencyPipe, DatePipe, DecimalPipe, FormsModule, NgTemplateOutlet, RouterLink, Icon, PackFiles],
  template: `
    <div class="page">
      <a class="btn btn--quiet back" routerLink="/history"><app-icon name="back" [size]="18" />{{ 'All years' | t }}</a>
      @if (message()) { <p class="notice" role="status">{{ message() | t }}</p> }
      @if (pack(); as item) {
        <section class="page-heading">
          <div>
            <span class="eyebrow">{{ 'Tax pack · approved {date}' | t: { date: (item.approvedAt | date: ('d MMM yyyy' | t)) } }}</span>
            <h1>{{ label(item.model.financialYearStart) }}</h1>
            <p>{{ 'A frozen record of what you approved. Later changes to receipts don’t alter it.' | t }}</p>
          </div>
          <span class="chip" [class.chip--done]="item.status === 'Complete'" [class.chip--info]="item.status === 'Queued' || item.status === 'Processing'" [class.chip--review]="item.status === 'Failed'">{{ 'Package ' + item.status.toLowerCase() | t }}</span>
        </section>

        <section class="figures">
          <div class="total" [class.total--done]="item.status === 'Complete'">
            @if (item.status === 'Complete') {
              <div class="confetti" aria-hidden="true">@for (bit of confetti; track $index) { <i [style.left.%]="bit.x" [style.top.%]="bit.y" [style.background]="bit.color" [style.--turn]="bit.turn + 'deg'" [style.animation-delay.ms]="bit.delay" [class.round]="bit.round"></i> }</div>
            }
            <span class="kicker">{{ 'Approved deductions' | t }}</span><strong class="sum">{{ item.model.assessment.approvedTotal | currency: ('AUD' | t) : 'symbol-narrow' }}</strong>
            <p>{{ (approved().length === 1 ? '{n} approved claim' : '{n} approved claims') | t: { n: approved().length } }} · {{ (item.model.evidence.length === 1 ? '{n} evidence file' : '{n} evidence files') | t: { n: item.model.evidence.length } }}</p>
            @if (item.status === 'Complete') {
              <div class="done">
                <span class="badge" aria-hidden="true"><app-icon name="check" [size]="30" /></span>
                <span>@if (allChecked()) { <strong>{{ 'All entered and checked — nicely done!' | t }}</strong><small>{{ 'This year’s deductions are in myTax. Keep this pack for your records.' | t }}</small> }
                  @else { <strong>{{ 'Your tax pack is ready' | t }}</strong><small>{{ 'Enter the amounts on the right in myTax, then mark each one as you go.' | t }}</small> }</span>
              </div>
            }
          </div>
          <div class="card entry">
            <span class="kicker">{{ 'Enter in myTax' | t }}</span>
            @for (part of sections(); track part.paperReference) {
              <div class="part">
                <p class="small muted">{{ 'Deductions' | t }} → {{ part.section }} ({{ part.paperReference }})@if (part.claimType) { · {{ 'claim type' | t }} <strong>{{ part.claimType }}</strong> }</p>
                <div class="row"><strong class="amount">{{ '$' + part.entryAmount.toFixed(0) }}</strong>
                  <button class="btn btn--primary btn--small" type="button" (click)="copy(part)">{{ 'Copy' | t }}<span class="sr-only"> {{ part.section }}</span></button></div>
                <label class="field">{{ 'Your progress' | t }}<span class="sr-only"> {{ 'for {name}' | t: { name: part.section } }}</span>
                  <select [ngModel]="progressOf(part)" (ngModelChange)="saveProgress($event, part)" [disabled]="busy()">
                    <option value="NotStarted">{{ 'Not entered yet' | t }}</option><option value="Entered">{{ 'Entered in myTax' | t }}</option><option value="Checked">{{ 'Entered and checked' | t }}</option>
                  </select>
                </label>
              </div>
            }
          </div>
        </section>

        <section class="card section" aria-labelledby="steps-title">
          <span class="kicker">{{ 'How to use it' | t }}</span><h2 id="steps-title">{{ 'Entering this in myTax' | t }}</h2>
          <ol class="steps">
            <li>{{ 'Sign in to' | t }} <a [href]="links.myGov" target="_blank" rel="noopener">{{ 'myGov' | t }}<span class="sr-only"> {{ '(opens in a new tab)' | t }}</span></a>{{ ', open the ATO and choose' | t }} <strong>{{ 'Prepare tax return' | t }}</strong>.</li>
            <li>{{ 'Under' | t }} <strong>{{ 'Deductions' | t }}</strong>{{ ', open each section listed above. Some amounts, such as union fees from your income statement, may already be there — don’t add them twice.' | t }}</li>
            <li>{{ 'Add anything not prefilled, using the amounts in your entry guide. Where a section asks for a claim type, choose the code shown. The whole-dollar figures above are what KuTax approved.' | t }}</li>
            <li>{{ 'Mark your progress here so you know where you’re up to next time.' | t }}</li>
          </ol>
          <p class="small"><a [href]="myTaxGuide()" target="_blank" rel="noopener">{{ 'ATO: myTax instructions for this section' | t }}</a> · <a [href]="links.lodgeWithMyTax" target="_blank" rel="noopener">{{ 'ATO: lodging with myTax' | t }}</a></p>
        </section>

        <section class="card section" aria-labelledby="files-title">
          <span class="kicker">{{ 'Downloads' | t }}</span><h2 id="files-title">{{ 'Your files' | t }}</h2>
          @if (item.status === 'Complete') {
            <app-pack-files [packId]="item.id" [files]="item.files" />
          } @else if (item.status === 'Failed') {
            <p class="notice notice--error" role="alert">{{ 'This package couldn’t be generated ({code}).' | t: { code: item.errorCode } }}</p>
            <button class="btn" type="button" (click)="retry()" [disabled]="busy()">{{ 'Try again' | t }}</button>
          } @else {
            <p class="notice" role="status">{{ 'Your files are being prepared. This page updates by itself.' | t }}</p>
          }
        </section>

        <section class="card section" aria-labelledby="claims-title">
          <span class="kicker">{{ 'What you claimed' | t }}</span><h2 id="claims-title">{{ 'Approved claims' | t }}</h2>
          @if (!approved().length) { <p class="muted">{{ 'No claims were approved in this pack.' | t }}</p> }
          <ul class="claims">
            @for (row of approved(); track row.allocationId) {
              <li>
                <div class="claim-head"><strong>{{ row.description }}</strong><strong>{{ row.deductibleAmount | currency: ('AUD' | t) : 'symbol-narrow' }}</strong></div>
                <p class="small muted">{{ row.merchant }} · {{ row.date | date: ('d MMM yyyy' | t) }} · {{ category(row.category) | t }}</p>
                <p class="small">{{ row.expenseAmount | currency: ('AUD' | t) : 'symbol-narrow' }} × {{ '{pct}% work use' | t: { pct: row.workUsePercent } }} − {{ '{amount} reimbursed' | t: { amount: (row.workReimbursement | currency: 'AUD' : 'symbol-narrow') } }}</p>
                @if (row.originalCurrency) { <p class="small muted">{{ 'Receipt in {currency} {amount}' | t: { currency: row.originalCurrency, amount: (row.originalAmount | number: '1.2-2') } }} · {{ row.conversionSource === 'RBA' ? ('AUD estimated at the RBA rate for {date}' | t: { date: (row.conversionRateDate | date: ('d MMM yyyy' | t)) }) : ('AUD from your statement' | t) }}</p> }
                <p class="small">@for (doc of row.evidence; track doc.id) { <a [href]="'/api/documents/' + doc.id + '/content?download=1'" download><app-icon name="file" [size]="14" /> {{ doc.name }}</a> }</p>
              </li>
            }
          </ul>
          @if (others().length) {
            <details>
              <summary class="small">{{ (others().length === 1 ? '{n} expense was not claimed' : '{n} expenses were not claimed') | t: { n: others().length } }}</summary>
              <ul class="claims">
                @for (row of others(); track row.allocationId) {
                  <li><div class="claim-head"><span>{{ row.description }}</span><span class="chip">{{ row.status | t }}</span></div><p class="small muted">{{ row.merchant }} · {{ row.reason | t }}</p></li>
                }
              </ul>
            </details>
          }
          @if (item.model.assessment.issues.length) {
            <details><summary class="small">{{ (item.model.assessment.issues.length === 1 ? '{n} exception was acknowledged' : '{n} exceptions were acknowledged') | t: { n: item.model.assessment.issues.length } }}</summary>
              <ul class="small">@for (issue of item.model.assessment.issues; track $index) { <li>{{ issue.description | t }}@if (issue.amount !== null) { · {{ issue.amount | currency: ('AUD' | t) : 'symbol-narrow' }} }</li> }</ul>
            </details>
          }
        </section>

        <section class="card section" aria-labelledby="rules-title">
          <span class="kicker">{{ 'Why these amounts' | t }}</span><h2 id="rules-title">{{ 'Rules applied' | t }}</h2>
          <ng-template #ruleLine let-signoff>
            <p class="small"><strong>{{ category(signoff.rule.category) | t }}</strong> · {{ 'rule {version}, reviewed {date}' | t: { version: signoff.rule.version, date: (signoff.reviewedAt | date: ('d MMM yyyy' | t)) } }} ·
              <a [href]="signoff.rule.sourceUrl" target="_blank" rel="noopener">{{ signoff.rule.sourceUrl.includes('/law/') ? ('ATO ruling' | t) : ('ATO guidance' | t) }}</a> ·
              <a [href]="signoff.rule.mappingUrl" target="_blank" rel="noopener">{{ 'tax return instructions (PDF, ~17 MB)' | t }}</a></p>
          </ng-template>
          @for (signoff of usedRules(); track signoff.rule.version) { <ng-container *ngTemplateOutlet="ruleLine; context: { $implicit: signoff }" /> }
          @empty { <p class="muted small">{{ 'No rules were needed for this pack.' | t }}</p> }
          @if (otherRules().length) {
            <details><summary class="small">{{ (otherRules().length === 1 ? '{n} other rule was in force but not used' : '{n} other rules were in force but not used') | t: { n: otherRules().length } }}</summary>
              @for (signoff of otherRules(); track signoff.rule.version) { <ng-container *ngTemplateOutlet="ruleLine; context: { $implicit: signoff }" /> }</details>
          }
          @if (item.model.approvalNote) { <p class="small"><strong>{{ 'Your approval note:' | t }}</strong> “{{ item.model.approvalNote | t }}”</p> }
        </section>
      }
    </div>
  `,
  styles: `
    .back { margin: 0 0 8px -8px; }
    .figures { display: grid; gap: 16px; grid-template-columns: 1.2fr 1fr; }
    .total { background: var(--purple-dark); border-radius: var(--radius); color: var(--white); overflow: hidden; padding: 24px; position: relative; }
    .total > :not(.confetti) { position: relative; z-index: 1; }
    .total--done { background: radial-gradient(circle at 85% 20%, rgba(255, 255, 255, .12), transparent 45%), var(--purple-dark); }
    .confetti { inset: 0; pointer-events: none; position: absolute; }
    .confetti i { animation: fall 1.6s cubic-bezier(.2, .7, .3, 1) both; border: 1.5px solid rgba(0, 0, 0, .25); border-radius: 2px; height: 14px; position: absolute; transform: rotate(var(--turn)); width: 8px; }
    .confetti i.round { border-radius: 50%; height: 10px; width: 10px; }
    @keyframes fall { from { opacity: 0; transform: translateY(-120px) rotate(0); } to { opacity: .9; transform: translateY(0) rotate(var(--turn)); } }
    @media (prefers-reduced-motion: reduce) { .confetti i { animation: none; opacity: .9; } }
    .sum { text-shadow: 3px 3px 0 rgba(0, 0, 0, .25); }
    .done { align-items: center; background: rgba(255, 255, 255, .12); border: 2px solid rgba(255, 255, 255, .35); border-radius: 16px; display: flex; gap: 14px; margin-top: 22px; padding: 14px; }
    .done strong, .done small { display: block; }
    .done strong { font-size: 17px; }
    .done small { color: #d4cfea; margin-top: 2px; }
    .badge { align-items: center; animation: pop .45s .3s cubic-bezier(.3, 1.6, .5, 1) both; background: var(--mint); border: 3px solid var(--ink); border-radius: 50%;
      box-shadow: 3px 3px 0 var(--ink); color: var(--ink); display: flex; flex: none; height: 54px; justify-content: center; width: 54px; }
    @keyframes pop { from { transform: scale(0) rotate(-20deg); } to { transform: scale(1) rotate(0); } }
    @media (prefers-reduced-motion: reduce) { .badge { animation: none; } }
    .total .kicker { color: #cbbdfa; }
    .total .sum { display: block; font-size: clamp(36px, 6vw, 48px); letter-spacing: -.04em; margin-top: 12px; }
    .total p { color: #d4cfea; margin: 4px 0 0; }
    .entry p { margin: 2px 0 6px; }
    .amount { font-family: var(--mono); font-size: 36px; }
    .entry .field { margin: 12px 0 0; }
    .part + .part { border-top: 1px solid var(--line); margin-top: 14px; padding-top: 12px; }
    .steps { display: grid; gap: 10px; line-height: 1.55; margin: 8px 0 12px; padding-left: 20px; }
    .claims { display: grid; list-style: none; margin: 0; padding: 0; }
    .claims li { border-top: 1px solid var(--line); padding: 12px 0; }
    .claims p { margin: 4px 0 0; }
    .claim-head { align-items: center; display: flex; gap: 12px; justify-content: space-between; }
    .claims a { display: inline-flex; gap: 4px; margin-right: 10px; }
    @media (max-width: 700px) { .figures { grid-template-columns: 1fr; } }
  `,
})
export class HistoryDetailPage implements OnDestroy {
  readonly id = input.required<string>();
  private readonly http = inject(HttpClient);
  readonly pack = signal<Pack | null>(null);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly links = links;
  readonly label = yearLabel;
  readonly category = categoryLabel;
  /** Decorative confetti for a finished pack, in the theme's colours; fixed positions so it looks the same every time. */
  readonly confetti = Array.from({ length: 22 }, (_, i) => ({
    x: 48 + ((i * 37 + 7) % 48), y: 3 + ((i * 53) % 60), turn: (i * 47) % 180 - 90, delay: (i % 7) * 70, round: i % 4 === 0,
    color: ['var(--yellow)', 'var(--mint)', 'var(--orange)', 'var(--sky)', 'var(--purple-soft)'][i % 5],
  }));
  /** Every myTax section marked "Entered and checked". */
  readonly allChecked = computed(() => { const pack = this.pack(); return !!pack && this.sections().length > 0 && this.sections().every((part) => progressFor(pack.progress, part.section) === 'Checked'); });
  /** Rules behind the approved claims first; the rest of the year's rules are folded away. */
  readonly usedRules = computed(() => { const used = new Set(this.approved().map((row) => row.ruleVersion)); return (this.pack()?.model.rules ?? []).filter((r) => used.has(r.rule.version)); });
  readonly otherRules = computed(() => { const used = new Set(this.approved().map((row) => row.ruleVersion)); return (this.pack()?.model.rules ?? []).filter((r) => !used.has(r.rule.version)); });
  readonly approved = computed(() => this.pack()?.model.assessment.rows.filter((row) => row.status === 'Approved') ?? []);
  readonly others = computed(() => this.pack()?.model.assessment.rows.filter((row) => row.status !== 'Approved') ?? []);
  readonly sections = computed(() => (this.pack() ? sectionsOf(this.pack()!.model.assessment) : []));
  readonly myTaxGuide = computed(() => myTaxOtherWorkExpenses(this.pack()?.model.financialYearStart ?? 0));
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      this.id();
      void this.load();
    });
  }

  ngOnDestroy() {
    clearTimeout(this.timer);
  }


  async load() {
    clearTimeout(this.timer);
    const id = this.id();
    try {
      const item = await firstValueFrom(this.http.get<Pack>(`/api/tax-exports/${encodeURIComponent(id)}`));
      if (id !== this.id()) return;
      this.pack.set(item);
      if (item.status === 'Queued' || item.status === 'Processing') this.timer = setTimeout(() => void this.load(), 2500);
    } catch {
      this.message.set('We couldn’t open this tax pack. It may belong to another account.');
    }
  }

  progressOf(part: MyTaxSection) {
    return progressFor(this.pack()?.progress ?? [], part.section);
  }

  async copy(part: MyTaxSection) {
    const amount = part.entryAmount.toFixed(0);
    try {
      await navigator.clipboard.writeText(amount);
      this.message.set(`Copied ${amount}. Check prefilled amounts in myTax before entering it.`);
    } catch {
      this.message.set('Copying isn’t available here. Type the amount shown instead.');
    }
  }

  async saveProgress(status: string, part: MyTaxSection) {
    const item = this.pack();
    if (!item || this.busy()) return;
    this.busy.set(true);
    try {
      await firstValueFrom(this.http.put(`/api/tax-exports/${item.id}/entry-progress`, { status, section: item.model.assessment.sections ? part.section : null }));
      await this.load();
      this.message.set('Progress saved.');
    } catch {
      this.message.set('We couldn’t save your progress. Try again.');
    } finally {
      this.busy.set(false);
    }
  }

  async retry() {
    const item = this.pack();
    if (!item || this.busy()) return;
    this.busy.set(true);
    try {
      await firstValueFrom(this.http.post(`/api/tax-exports/${item.id}/retry`, {}));
      await this.load();
    } catch {
      this.message.set('We couldn’t restart the package. Try again shortly.');
    } finally {
      this.busy.set(false);
    }
  }
}
