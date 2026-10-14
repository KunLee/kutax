import { Component, computed, effect, inject, input, OnDestroy, output, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { Icon } from '../core/icon';
import { AllocationEditor } from './allocation-editor';
import { DocumentViewer } from './document-viewer';
import { AiStatus } from '../core/ai-status';
import { Session, yearLabel } from '../core/session';
import { skipReasonLabel, skipReasons } from '../core/skip';
import { clearAttention, showAttention } from '../core/attention';
import { TranslatePipe } from '../core/i18n';
import { DateField } from '../core/date-field';
import { SelectMenu } from '../core/select-menu';
import { Toasts } from '../core/toast';
import { categoryLabel, claimTypes as claimTypeLabels } from '../core/categories';

/** A tax rule as the one-page claim needs it. */
interface RuleOption { category:string;label:string;financialYearStart:number;claimTypes:string[];wholeAmountOnly:boolean;declaration:string;isActive:boolean; }
interface Transaction { id:string;version:string;merchant:string|null;date:string|null;amount:number|null;gst:number|null;notes:string;status:string;originalCurrency?:string|null;originalAmount?:number|null;conversionSource?:'Statement'|'RBA'|null;suggestedCategory?:string|null;skipReason?:string|null; }
interface Quote { rateDate:string;unitsPerAud:number;aud:number|null; }
// Currencies in the RBA F11.1 daily table (matches the server).
const rbaCurrencies=['USD','EUR','GBP','NZD','JPY','CNY','SGD','HKD','CAD','CHF','INR','KRW','THB','TWD','MYR','IDR','VND','PHP','AED','PGK','ZAR'];
interface Field { value:string|number|null; confidence:number; evidence:string|null; }
interface Candidate { merchant:Field;date:Field;amount:Field;gst:Field;originalCurrency?:Field|null;originalAmount?:Field|null; }
interface Review { document:{id:string;originalFileName:string;contentType:string;status:string};transaction:Transaction|null;candidates:{candidateJson:string;provider:string;model:string;createdAt:string}[];job:{status:string;errorCode:string|null}|null;history:{action:string;actorId:string;createdAt:string}[];possibleDuplicate:boolean; }
@Component({
 selector:'app-receipt-review', imports:[TranslatePipe, DatePipe,FormsModule,RouterLink,Icon,AllocationEditor,DocumentViewer,AiStatus,DateField,SelectMenu],
 host:{'(document:keydown.escape)':'setExpanded(false)'},
 template:`
 @if(message()){<p class="notice" role="status">{{message() | t}}</p>}
 @if(data();as review){
 <article class="card review">
  <div class="preview" [class.expanded]="expanded()" [attr.role]="expanded()?'dialog':null" [attr.aria-label]="expanded()?'Original receipt, full screen':null">
   <div class="panel-cap preview-cap" aria-hidden="true"><span>{{ '02 / Original' | t }}</span></div>
   @if(latest()){<span class="tag"><app-icon name="spark" [size]="14" />{{ 'Read by KuTax' | t }}</span>}
   <app-document-viewer class="viewer" [documentId]="id()" [contentType]="review.document.contentType" />
   <div class="row preview-actions">
    <button class="btn btn--small" type="button" (click)="setExpanded(!expanded())" [attr.aria-pressed]="expanded()">{{expanded()?('Close full screen' | t):('Full screen' | t)}}</button>
    <a class="btn btn--small" [href]="'/api/documents/'+id()+'/content?download=1'" download><app-icon name="download" [size]="16" />{{ 'Download original' | t }}</a>
   </div>
  </div>
  <div class="form">
   <div class="panel-cap" aria-hidden="true"><span>{{ '03 / Extracted' | t }}</span><small>{{ (found()===1?'{n} field found':'{n} fields found') | t: { n: found() } }}</small></div>
   <div class="form-heading">
    <span class="kicker">{{ 'Check the details' | t }}</span>
    <h2>{{review.transaction?.merchant || review.document.originalFileName}}</h2>
    <p class="muted small">{{review.document.originalFileName}}</p>
    <div class="row">
     @if(review.transaction;as t){<span class="chip" [class.chip--done]="t.status==='Confirmed'" [class.chip--review]="t.status==='Draft'">{{t.status==='Confirmed'?('Confirmed' | t):t.status==='Dismissed'?('Skipped' | t):('Draft' | t)}}</span>}
     @if(reading()){<span class="chip chip--info"><span class="spinner" aria-hidden="true"></span>{{ 'Reading…' | t }}</span>}
    </div>
   </div>
   @if(!reading()&&review.job?.status==='Failed'&&review.job?.errorCode!=='MalwareDetected'){<p class="notice notice--warning" role="status"><strong>{{ 'AI couldn’t read this receipt reliably.' | t }}</strong> {{ (review.job?.errorCode==='RateLimited'?'Type the details from the original — the AI service was busy, so you can also try Read again later.':'Type the details from the original.') | t }}</p>}
   @if(reading()){<p class="notice reading-now" role="status"><span class="spinner" aria-hidden="true"></span><span><strong>{{ 'Reading this receipt…' | t }}</strong> {{ 'The details fill in here by themselves. You can check and confirm it as soon as it’s done.' | t }}</span>
     @if(review.job?.status==='Queued'){<button class="btn btn--small" type="button" (click)="takeOver()" [disabled]="busy()">{{ 'Don’t wait — I’ll enter it myself' | t }}</button>}</p>}
   @if(review.possibleDuplicate&&review.transaction?.status!=='Dismissed'){<div class="notice notice--warning dismissed" role="alert"><span>{{ 'Another receipt has the same merchant, date and amount. If this is the same purchase, skip this copy.' | t }}</span>
    <button class="btn btn--small" type="button" (click)="dismiss(true,'Duplicate')" [disabled]="busy()||reading()">{{ 'Skip as a duplicate' | t }}</button></div>}
   @if(review.transaction?.status==='Dismissed'){
    <div class="notice dismissed" role="status"><span><strong>{{ 'Skipped' | t }}@if(skipLabel(review.transaction?.skipReason);as why){ · {{why | t}}} — {{ 'not for your tax return.' | t }}</strong> {{ 'It stays in your records but isn’t reviewed or claimed.' | t }}</span>
     <button class="btn btn--small" type="button" (click)="dismiss(false)" [disabled]="busy()||reading()">{{ 'Restore' | t }}</button></div>
   }
   @if(done()==='confirmed'){
    <div class="notice notice--success" role="status"><strong>{{ 'Confirmed.' | t }}</strong>@if(!nextId()){ {{ 'That was the last one waiting.' | t }}}</div>
   }
   @if(review.transaction;as transaction){
    <form (ngSubmit)="save(false)"><fieldset [disabled]="loading() || busy() || reading()">
     @if(otherYear();as fy){<div class="notice notice--warning guard" role="status">
      <span><strong>{{ 'Dated {date} — that’s {fy}, not the selected {selected}.' | t: { date: (date|date:('d MMM yyyy' | t)), fy: fy.label, selected: selectedLabel() } }}</strong> {{ 'Receipts count in the financial year of their date, so this one won’t be in the {selected} tax pack.' | t: { selected: selectedLabel() } }}</span>
      <div class="row"><button class="btn btn--small" type="button" (click)="switchYear(fy.start)">{{ 'Switch to {fy}' | t: { fy: fy.label } }}</button>
       <button class="btn btn--small btn--quiet" type="button" (click)="dismiss(true,'OtherYear')">{{ 'Skip this receipt' | t }}</button></div></div>}
     <label class="check fx-toggle"><input type="checkbox" name="foreign" [(ngModel)]="foreign" (ngModelChange)="fxChanged()"><span>{{ 'This receipt isn’t in Australian dollars' | t }}</span></label>
     @if(foreign){<div class="fx">
      <div class="fx-row">
       <label class="field">{{ 'Currency' | t }}<input id="r-fx-currency" name="fxCurrency" list="fx-currencies" maxlength="3" autocomplete="off" required [ngModel]="fxCurrency" (ngModelChange)="fxCurrency=($event||'').toUpperCase();fxChanged()"></label>
       <label class="field">{{ 'Amount on receipt' | t }}<input id="r-fx-amount" name="fxAmount" type="number" inputmode="decimal" min="0.01" step="0.01" required [(ngModel)]="fxAmount" (ngModelChange)="fxChanged()"></label>
      </div>
      <datalist id="fx-currencies">@for(code of currencies;track code){<option [value]="code"></option>}</datalist>
      <label class="check"><input type="radio" name="fxSource" value="Statement" [(ngModel)]="fxSource" (ngModelChange)="fxChanged()"><span><strong>{{ 'AUD from my bank or card statement' | t }}</strong> {{ '(recommended) — what you actually paid, fees included.' | t }}</span></label>
      <label class="check"><input type="radio" name="fxSource" value="RBA" [(ngModel)]="fxSource" (ngModelChange)="fxChanged()"><span><strong>{{ 'Estimate with the RBA rate' | t }}</strong> {{ 'for the receipt date.' | t }}</span></label>
      @if(fxSource==='RBA'){<p class="small fx-status" role="status">{{fxStatus() | t}}</p>}
     </div>}
     <label class="field">{{ 'Total paid (AUD)' | t }}@if(foreign&&fxSource==='Statement'){ — {{ 'from your statement' | t }}} @if(hint('amount');as h){<span class="hint" [class.hint--check]="h.check">{{h.label | t}}</span>}
      <input id="r-amount" name="amount" type="number" inputmode="decimal" min="0.01" step="0.01" [(ngModel)]="amount" [readonly]="foreign&&fxSource==='RBA'" required>@if(note('amount');as n){<small class="ai-why">AI: {{n | t}}</small>}</label>
     <label class="field">{{ 'Receipt date' | t }} @if(hint('date');as h){<span class="hint" [class.hint--check]="h.check">{{h.label | t}}</span>}
      <app-date-field inputId="r-date" name="date" [(ngModel)]="date" (ngModelChange)="fxChanged()" [required]="true" />@if(note('date');as n){<small class="ai-why">AI: {{n | t}}</small>}</label>
     <label class="field">{{ 'Merchant' | t }} @if(hint('merchant');as h){<span class="hint" [class.hint--check]="h.check">{{h.label | t}}</span>}
      <input id="r-merchant" name="merchant" [(ngModel)]="merchant" maxlength="200" autocomplete="off" required>@if(note('merchant');as n){<small class="ai-why">AI: {{n | t}}</small>}</label>
     <label class="field">{{ 'GST shown (optional)' | t }} @if(hint('gst');as h){<span class="hint" [class.hint--check]="h.check">{{h.label | t}}</span>}
      <input name="gst" type="number" inputmode="decimal" min="0" step="0.01" [(ngModel)]="gst"></label>
     <label class="field">{{ 'Review notes' | t }}<textarea name="notes" [(ngModel)]="notes" maxlength="2000"></textarea></label>
     <div class="ai-note"><app-icon name="spark" /><span>
      @if(latest();as attempt){<strong>{{ 'Suggested by {provider}' | t: { provider: attempt.provider } }}</strong><small>{{ 'Compare every field with the original. Confirming records the facts; claim approval is a separate step.' | t }}</small>}
      @else{<strong>{{ 'Manual entry' | t }}</strong><small>{{ 'Nothing was read automatically. Enter the details exactly as they appear on the original.' | t }}</small>}
     </span></div>
     @if(!latest()){<app-ai-status [compact]="true" />}
     @if(transaction.status!=='Dismissed'&&!splitOpen()){<div class="claim-q" role="group" aria-labelledby="claim-q-title">
      <strong id="claim-q-title">{{ 'What kind of expense is this?' | t }}</strong>
      <app-select-menu label="Expense" [options]="categoryOptions()" [(value)]="category" />
      @if(chosenRule();as rule){
       @if(rule.claimTypes.length){<div class="claim-types" role="radiogroup" [attr.aria-label]="'Claim type' | t">@for(code of rule.claimTypes;track code){<button type="button" role="radio" class="chip" [attr.aria-checked]="claimType()===code" (click)="claimType.set(code)">{{ claimTypeLabel(code) | t }}</button>}</div>}
       @if(!rule.wholeAmountOnly){<label class="field use">{{ 'How much is for work?' | t }}<span class="row"><input id="r-use" class="input" type="number" min="0" max="100" step="1" inputmode="numeric" [(ngModel)]="workUse" name="workUse" /><span>%</span></span></label>}
       <label class="check small"><input id="r-declared" type="checkbox" [(ngModel)]="declared" name="declared" /><span>{{ 'I confirm:' | t }} {{ rule.declaration | t }}</span></label>
      }
      @if(category()==='Unsupported'){<small class="muted">{{ 'KuTax doesn’t calculate this kind of expense. It’s kept with your records for you or a tax agent to decide.' | t }}</small>}
      @if(category()===''){<small class="muted">{{ 'You can choose later. One receipt with several kinds of expense, or partly reimbursed? Choose “Later” and split it after confirming.' | t }}</small>}
     </div>}
     <div class="actions">
      @if(transaction.status!=='Dismissed'){<button class="btn btn--quiet" type="button" (click)="skipping.set(!skipping())" [attr.aria-expanded]="skipping()" [disabled]="busy()" [attr.aria-label]="'Skip — not for my tax return' | t">{{ 'Skip' | t }}<span class="long"> {{ '— not for my tax return' | t }}</span></button>}
      <button class="btn btn--primary" type="button" (click)="save(true)" [disabled]="busy()" [attr.aria-busy]="working()==='confirm'"><app-icon name="check" [size]="18" />{{ (willClaim()?'Confirm and claim':category()==='Personal'?'Confirm and skip':'Confirm receipt') | t }}</button>
     </div>
     @if(skipping()&&transaction.status!=='Dismissed'){<div class="skip" role="group" [attr.aria-label]="'Why skip it?' | t"><span class="small"><strong>{{ 'Why?' | t }}</strong> {{ '(optional)' | t }}</span>
      @for(reason of reasons;track reason.key){<button class="btn btn--small" type="button" (click)="dismiss(true,reason.key)" [disabled]="busy()">{{reason.label | t}}</button>}
      <button class="btn btn--small btn--quiet" type="button" (click)="dismiss(true)" [disabled]="busy()">{{ 'Just skip' | t }}</button></div>}
     <button class="btn btn--quiet btn--small draft" type="submit" [disabled]="busy()" [attr.aria-busy]="working()==='draft'">{{ 'Save as draft' | t }}</button>
     @if(changed()){<button class="btn btn--quiet btn--small draft" type="button" (click)="revert()" [disabled]="busy()"><app-icon name="back" [size]="16" />{{ 'Undo changes' | t }}</button>}
    </fieldset></form>
   }@else{
    @if(!reading()){<p class="notice">{{ 'Reading is pending or needs attention. You can enter the receipt details yourself.' | t }}</p>
    <button class="btn btn--primary" type="button" (click)="manual()" [disabled]="busy()">{{ 'Enter manually' | t }}</button>}
   }
   <div class="extras">
    <button class="btn btn--quiet btn--small" type="button" (click)="retry()" [disabled]="busy()||reading()">{{ 'Read again' | t }}</button>
    @for(attempt of suggestions();track attempt.createdAt){<details><summary>{{ '{provider} suggestions · {date}' | t: { provider: attempt.provider, date: (attempt.createdAt|date:('d MMM, h:mm a' | t)) } }}</summary>
     <dl>@for(field of fields(attempt.candidateJson);track field.name){<div><dt>{{field.name | t}}</dt><dd>{{field.value??('Unknown' | t)}}<small>{{ '{pct}% sure' | t: { pct: (field.confidence*100).toFixed(0) } }}@if(field.evidence){ · “{{field.evidence}}”}</small></dd></div>}</dl></details>}
    <button class="btn btn--quiet btn--small btn--danger" type="button" (click)="deleting.set(!deleting())" [attr.aria-expanded]="deleting()" [disabled]="busy()||reading()">{{ 'Delete this file' | t }}</button>
    @if(deleting()){<div class="notice notice--warning delete" role="alert">
     <span><strong>{{ 'Only for files uploaded by mistake.' | t }}</strong> {{ 'This removes the original, what KuTax read from it and any expenses split from it. If it’s a real receipt, keep it — the ATO expects you to keep records for 5 years after you lodge. To leave it out of your return, skip it instead.' | t }}</span>
     <div class="row"><button class="btn btn--small btn--danger" type="button" (click)="remove()" [disabled]="busy()">{{ 'Delete permanently' | t }}</button>
      <button class="btn btn--small btn--quiet" type="button" (click)="deleting.set(false)">{{ 'Keep it' | t }}</button></div></div>}
    <details><summary>{{ 'Review history' | t }}</summary><ul class="history">@for(event of review.history;track $index){<li><strong>{{event.action}}</strong> · {{event.createdAt|date:('d MMM yyyy, h:mm a' | t)}}</li>}@empty{<li>{{ 'No changes yet.' | t }}</li>}</ul></details>
   </div>
  </div>
 </article>
 @if(review.transaction;as transaction){@if(transaction.status==='Confirmed' && transaction.amount!==null){
  @if(!splitOpen()){<section class="card section next" aria-labelledby="next-title">
   <span class="kicker">{{ 'More options' | t }}</span><h3 id="next-title">{{ 'Several kinds of expense, or partly reimbursed?' | t }}</h3>
   <p class="small muted">{{ 'Split this receipt into expenses. For a single kind of expense, choose it above and confirm.' | t }}</p>
   <div class="row">
    <button class="btn" type="button" (click)="splitOpen.set(true)">{{ 'Split into expenses' | t }}</button>
    <button class="btn btn--quiet" type="button" (click)="skipConfirmed()" [disabled]="busy()">{{ 'Skip — not for my tax return' | t }}</button>
    @if(nextId()){<a class="btn btn--primary" [routerLink]="['/review',nextId()]">{{ 'Next receipt' | t }}<app-icon name="arrow" [size]="16" /></a>}
    @else{<a class="btn btn--primary" routerLink="/review">{{ 'Back to queue' | t }}</a>}
   </div></section>}
  @else{<section class="card section"><app-allocation-editor [transactionId]="transaction.id" [version]="transaction.version" [amount]="transaction.amount" [merchant]="transaction.merchant ?? ''" [suggested]="transaction.suggestedCategory ?? null" (saved)="load();saved.emit()" /></section>}
 }}
 }@else if(loading()){<div class="card loading" role="status">{{ 'Opening receipt…' | t }}</div>}`,
 styles:[`
  :host{display:block}
  .review{display:grid;grid-template-columns:minmax(0,58%) minmax(0,42%);padding:0}
  .preview{border-radius:var(--radius) var(--radius) 0 0}
  .claim-q{background:var(--yellow-soft);border:2px solid var(--ink);border-radius:14px;display:grid;gap:10px;margin:4px 0 14px;padding:12px 14px}.claim-q app-select-menu{display:block}.claim-q .use .row{align-items:center}.claim-q .use input{max-width:110px}.claim-types{display:flex;flex-wrap:wrap;gap:6px}.claim-types .chip{cursor:pointer}.claim-types [aria-checked=true]{background:var(--ink);color:var(--white)}
  .reading-now{align-items:center;display:flex;gap:10px}.guard{display:grid;gap:10px}.fx-toggle{margin-top:0}.fx{background:#fbfaf6;border:1px solid var(--line);border-radius:14px;margin-bottom:14px;padding:12px 14px 2px}.fx-row{display:grid;gap:0 12px;grid-template-columns:110px 1fr}.fx-status{margin:0 0 12px}.ai-why{color:var(--orange-ink);display:block;font-weight:600;margin-top:4px}
  .preview{align-items:center;background:linear-gradient(rgba(52,42,92,.05) 1px,transparent 1px),linear-gradient(90deg,rgba(52,42,92,.05) 1px,transparent 1px),var(--sky-soft);background-size:20px 20px;display:flex;flex-direction:column;gap:12px;justify-content:center;min-height:420px;padding:56px 20px 20px;position:relative}
  .viewer{background:#fffdf5;box-shadow:7px 8px 0 rgba(52,42,92,.15);width:100%}
  .preview-actions{justify-content:center}.preview-actions .btn{background:rgba(255,255,255,.9)}
  .preview.expanded{align-self:stretch;background:var(--paper);border-radius:0;height:auto;inset:0;justify-content:flex-start;padding:12px 12px max(12px,env(safe-area-inset-bottom));position:fixed;z-index:60;--viewer-height:none}
  .preview.expanded .tag{display:none}
  .preview.expanded .viewer{box-shadow:none;flex:1}
  .tag{align-items:center;background:var(--mint);border:1px solid var(--ink);border-radius:15px;display:flex;font-family:var(--mono);font-size:11px;gap:5px;left:15px;padding:6px 9px;position:absolute;top:15px}
  .open{background:rgba(255,255,255,.85)}
  .form{padding:32px;min-width:0}
  .form-heading{margin-bottom:20px}.form-heading h2{margin:6px 0 2px;overflow-wrap:anywhere}.form-heading p{margin:0 0 10px}
  .hint{background:var(--mint-soft);border-radius:10px;color:var(--mint-ink);float:right;font-size:11px;padding:2px 8px}
  .hint--check{background:var(--orange-soft);color:var(--orange-ink)}
  .ai-note{align-items:center;background:var(--purple-soft);border-radius:12px;display:flex;gap:10px;padding:12px 14px}
  .ai-note app-icon{color:var(--purple-ink)}.ai-note strong,.ai-note small{display:block}.ai-note strong{font-size:13px}.ai-note small{color:var(--ink-soft);font-size:12px;line-height:1.5;margin-top:2px}
  .actions{align-items:center;border-top:1px solid var(--line);display:flex;gap:10px;justify-content:space-between;margin-top:20px;padding-top:18px}.draft{margin-top:6px;padding-left:0}
  .dismissed{align-items:center;display:flex;gap:12px;justify-content:space-between}.skip{align-items:center;display:flex;flex-wrap:wrap;gap:8px;margin-top:12px}.delete{display:grid;gap:10px;margin:10px 0}.next h3{margin:4px 0 6px}
  .extras{border-top:1px solid var(--line);margin-top:18px;padding-top:10px}
  dl{display:grid;gap:8px;margin:0 0 10px}dl div{display:grid;gap:8px;grid-template-columns:90px 1fr}dt{color:var(--ink-soft);font-size:13px;text-transform:capitalize}dd{font-weight:700;margin:0;overflow-wrap:anywhere}dd small{color:var(--ink-soft);display:block;font-weight:400}
  .history{font-size:14px;margin:0;padding-left:18px}.history li{margin-bottom:6px}
  .loading{color:var(--ink-soft);text-align:center}
  @media(min-width:1101px){.preview{border-radius:var(--radius) 0 0 var(--radius);align-self:start;height:calc(100dvh - 104px);justify-content:flex-start;position:sticky;top:88px;--viewer-height:calc(100dvh - 250px)}}
  @media(max-width:1100px){.review{grid-template-columns:minmax(0,1fr)}.preview{min-height:0;padding-top:52px}}
  @media(max-width:700px){.form{padding:20px 18px}
   .actions{background:var(--white);bottom:var(--tabbar);margin:20px -18px 0;padding:12px 18px;position:sticky;z-index:5}
   .actions .btn--primary{flex:1}.actions .long{display:none}}
 `]
})
export class ReceiptReview implements OnDestroy {
 readonly id=input.required<string>();readonly nextId=input<string|null>(null);readonly saved=output<void>();
 /** Fired when the person is finished with this receipt and nothing else needs doing on it. */
 readonly finished=output<void>();
 /** Fired after the details are confirmed, so the page can move on to the next receipt waiting. */
 readonly confirmed=output<string>();
 /** Which save is running, so that button shows it. */
 readonly working=signal<'confirm'|'draft'|null>(null);
 readonly done=signal<'confirmed'|'dismissed'|null>(null);
 /** Fired after skipping, with what's needed to undo it. */
 readonly skipped=output<{transactionId:string;version:string;merchant:string}>();
 /** Fired after a mistaken upload is deleted. */
 readonly deleted=output<string>();
 readonly reasons=skipReasons;readonly skipping=signal(false);readonly deleting=signal(false);
 /** Step 2 is optional: shown straight away only when the receipt already has expenses. */
 readonly splitOpen=signal(false);
 /** The one-page claim: what kind of expense, its claim type and work share, and the conditions confirmed. */
 readonly rules=signal<RuleOption[]>([]);
 readonly category=signal('');readonly claimType=signal<string|null>(null);workUse=100;declared=false;
 /** Rules for the financial year of the receipt's date (1 July to 30 June). */
 yearRules(){const [y,m]=(this.date||'').split('-').map(Number);if(!y||!m)return [];const start=m>=7?y:y-1;return this.rules().filter(r=>r.isActive&&r.financialYearStart===start);}
 categoryOptions(){return [{value:'',label:'Later'},...this.yearRules().map(r=>({value:r.category,label:categoryLabel(r.category)})),{value:'Unsupported',label:'Other work expense (needs advice)'},{value:'Personal',label:'Not for my tax return'}];}
 chosenRule(){return this.yearRules().find(r=>r.category===this.category())??null;}
 willClaim(){return this.category()!==''&&this.category()!=='Personal';}
 claimTypeLabel(code:string){return claimTypeLabels[code]??code;}
 skipLabel(key:string|null|undefined){return skipReasonLabel(key);}
 private readonly http=inject(HttpClient);private readonly toasts=inject(Toasts);
 readonly expanded=signal(false);
 private readonly viewer=viewChild(DocumentViewer);
 /** Leaving full screen returns to fit-to-width so a large zoomed page isn't left in the normal layout. */
 setExpanded(on:boolean){if(!on&&this.expanded())this.viewer()?.reset();this.expanded.set(on);}
 readonly found=computed(()=>{const c=this.latestFields();return c?[c.merchant,c.date,c.amount,c.gst].filter(f=>f?.value!==null&&f?.value!==undefined).length:0;});
 readonly reading=computed(()=>['Queued','Processing'].includes(this.data()?.job?.status??''));private timer:ReturnType<typeof setTimeout>|undefined;
 ngOnDestroy(){clearTimeout(this.timer);}readonly loading=signal(false);readonly data=signal<Review|null>(null);readonly busy=signal(false);readonly message=signal('');
 /** Extraction attempts that actually suggested values; the default manual extractor reads nothing. */
 readonly suggestions=computed(()=>[...(this.data()?.candidates??[])].filter(c=>c.provider!=='Manual').sort((a,b)=>b.createdAt.localeCompare(a.createdAt)));
 readonly latest=computed(()=>this.suggestions()[0]??null);
 private readonly latestFields=computed(()=>{const attempt=this.latest();return attempt?JSON.parse(attempt.candidateJson) as Candidate:null;});
 merchant='';date='';amount:number|null=null;gst:number|null=null;notes='';
 /** The details as last loaded or saved, so edits can be undone in one press. */
 private original='';
 private snapshot(){return JSON.stringify({merchant:this.merchant,date:this.date,amount:this.amount,gst:this.gst,notes:this.notes,foreign:this.foreign,fxCurrency:this.fxCurrency,fxAmount:this.fxAmount,fxSource:this.fxSource});}
 changed(){return !!this.original&&this.snapshot()!==this.original;}
 revert(){const o=JSON.parse(this.original);Object.assign(this,o);document.querySelectorAll<HTMLElement>('.needs-attention').forEach(clearAttention);this.fxQuote.set(null);this.fxError.set('');if(this.foreign&&this.fxSource==='RBA')void this.fxChanged();this.message.set('');}
 // Foreign currency: what the receipt shows and how its AUD amount is arrived at.
 readonly currencies=rbaCurrencies;foreign=false;fxCurrency='';fxAmount:number|null=null;fxSource:'Statement'|'RBA'='Statement';
 readonly fxQuote=signal<Quote|null>(null);readonly fxError=signal('');readonly fxLoading=signal(false);private fxRequest=0;
 private readonly session=inject(Session);
 selectedLabel(){const start=this.session.account()?.financialYearStart;return start===undefined?'':yearLabel(start);}
 /** The financial year of the receipt date, when it isn't the year the person is working on. */
 otherYear(){const [y,m]=(this.date||'').split('-').map(Number);const selected=this.session.account()?.financialYearStart;if(!y||!m||selected===undefined)return null;const start=m>=7?y:y-1;return start===selected?null:{start,label:yearLabel(start)};}
 switchYear(start:number){void this.session.selectYear(start);}
 fxStatus(){if(this.fxError())return this.fxError();const q=this.fxQuote();if(this.fxLoading()||!q)return 'Enter the currency and amount to look up the RBA rate…';return `A$1 = ${q.unitsPerAud} ${this.fxCurrency} on ${new Date(q.rateDate+'T00:00:00').toLocaleDateString('en-AU',{day:'numeric',month:'short',year:'numeric'})} (Reserve Bank of Australia), so A$${q.aud?.toFixed(2)} is an estimate.`;}
 /** Looks up the RBA rate (and the server's AUD figure) whenever the foreign details change. */
 async fxChanged(){this.fxError.set('');this.fxQuote.set(null);if(!this.foreign||this.fxSource!=='RBA'||!/^[A-Z]{3}$/.test(this.fxCurrency)||!this.fxAmount||this.fxAmount<=0||!this.date)return;
  const request=++this.fxRequest;this.fxLoading.set(true);
  try{const q=await firstValueFrom(this.http.get<Quote>(`/api/exchange-rates/${this.fxCurrency}/${this.date}`,{params:{amount:this.fxAmount}}));if(request!==this.fxRequest)return;this.fxQuote.set(q);if(q.aud!==null)this.amount=q.aud;}
  catch(error){if(request===this.fxRequest)this.fxError.set(error instanceof HttpErrorResponse?error.error?.detail??'The RBA rate couldn’t be loaded. Use your statement amount.':'The RBA rate couldn’t be loaded.');}
  finally{if(request===this.fxRequest)this.fxLoading.set(false);}}
 constructor(){effect(()=>{this.id();this.expanded.set(false);this.done.set(null);this.data.set(null);this.skipping.set(false);this.deleting.set(false);this.splitOpen.set(false);this.message.set('');void this.load();});}
 fields(json:string){const candidate=JSON.parse(json) as Candidate;return Object.entries(candidate).filter(([name])=>name!=='currency').map(([name,field])=>({name,...field}));}
 /** Uncertain-value prompt for a field the extractor actually suggested. */
 /** The AI's reason for leaving a field blank, e.g. a receipt in another currency. */
 note(name:keyof Candidate){const field=this.latestFields()?.[name];const why=field&&field.value===null?field.evidence:null;return why&&!why.startsWith('Not extracted')?why:null;}
 hint(name:keyof Candidate){const field=this.latestFields()?.[name];if(!field||field.value===null||field.value===undefined)return null;return field.confidence>=0.9?{label:'Looks clear',check:false}:{label:'Check this',check:true};}
 async load(){clearTimeout(this.timer);const id=this.id();this.loading.set(true);try{const result=await firstValueFrom(this.http.get<Review>(`/api/documents/${id}/review`));if(id!==this.id())return;this.data.set(result);if(['Queued','Processing'].includes(result.job?.status??''))this.timer=setTimeout(()=>void this.load(),3000);if(result.transaction){const t=result.transaction;if(!this.rules().length)void firstValueFrom(this.http.get<{rules:RuleOption[]}>('/api/tax-rules')).then(r=>this.rules.set(r.rules),()=>undefined);if(this.category()===''&&t.status!=='Confirmed'){this.category.set(t.suggestedCategory==='Personal'?'':t.suggestedCategory??'');this.declared=false;this.workUse=100;this.claimType.set(null);}this.merchant=t.merchant??'';this.date=t.date??'';this.amount=t.amount;this.gst=t.gst;this.notes=t.notes;
  const ai=this.latestFields();const code=t.originalCurrency??(ai?.originalCurrency?.value as string|null|undefined)??null;
  this.foreign=!!code;this.fxCurrency=code??'';this.fxAmount=t.originalAmount??(t.originalCurrency?null:(ai?.originalAmount?.value as number|null|undefined)??null);this.fxSource=t.conversionSource??'Statement';this.original=this.snapshot();this.fxQuote.set(null);this.fxError.set('');if(this.foreign&&this.fxSource==='RBA')void this.fxChanged();
  if(t.status==='Confirmed'&&!this.splitOpen())void firstValueFrom(this.http.get<unknown[]>(`/api/transactions/${t.id}/allocations`)).then(rows=>{if(rows.length&&id===this.id())this.splitOpen.set(true);}).catch(()=>undefined);}}catch{this.message.set('We couldn’t open this receipt. It may still be in a security check, or it isn’t yours.');}finally{this.loading.set(false);}}
 /** The first required field that's empty or invalid, so Confirm can point straight at it. */
 missingField(){
  if(this.foreign&&!/^[A-Z]{3}$/.test(this.fxCurrency))return {id:'r-fx-currency',message:'Enter the 3-letter currency code shown on the receipt, such as USD.'};
  if(this.foreign&&!(Number(this.fxAmount)>0))return {id:'r-fx-amount',message:'Enter the amount shown on the receipt.'};
  if(!(Number(this.amount)>0))return {id:'r-amount',message:this.foreign&&this.fxSource==='RBA'?'Waiting for the RBA rate. Check the currency, amount and date.':'Enter the total paid in Australian dollars.'};
  if(!this.date)return {id:'r-date',message:'Enter the date on the receipt.'};
  if(!this.merchant.trim())return {id:'r-merchant',message:'Enter who you paid.'};
  return null;}
 async save(confirm:boolean){const t=this.data()?.transaction;if(!t||this.busy())return;
  const missing=confirm?this.missingField():null;if(missing){showAttention(missing.id,missing.message);return;}this.busy.set(true);this.working.set(confirm?'confirm':'draft');try{await firstValueFrom(this.http.put(`/api/transactions/${t.id}`,{version:t.version,merchant:this.merchant,date:this.date||null,amount:this.amount,gst:this.gst,notes:this.notes,confirm,foreign:this.foreign&&this.fxCurrency&&this.fxAmount?{currency:this.fxCurrency,amount:this.fxAmount,source:this.fxSource}:null}));this.message.set(confirm?'Receipt details confirmed.':'Draft saved.');if(confirm)this.done.set('confirmed');await this.load();if(confirm&&this.category()==='Personal'){await this.skipConfirmedQuietly();return;}if(confirm&&this.willClaim()&&!await this.claim())return;await this.load();this.saved.emit();if(confirm)this.confirmed.emit(this.merchant||'');}catch(error){this.message.set(error instanceof HttpErrorResponse?error.error?.detail??'Could not save. Refresh and try again.':'Could not save.');}finally{this.busy.set(false);this.working.set(null);}}
 /** Claims the confirmed receipt in the chosen category; approved at once when its rule is checked and allows it. */
 private async claim(){const t=this.data()?.transaction;if(!t)return false;
  const rule=this.chosenRule();
  if(rule&&!this.declared){showAttention('r-declared','Tick this to confirm the conditions.');return false;}
  if(rule&&rule.claimTypes.length&&!this.claimType()){this.message.set('Choose the claim type.');return false;}
  try{const r=await firstValueFrom(this.http.post<{approved:boolean;status:string;deductibleAmount:number|null}>(`/api/transactions/${t.id}/claim`,{version:t.version,category:this.category(),workUsePercent:rule?.wholeAmountOnly?100:Number(this.workUse),claimType:rule?.claimTypes.length?this.claimType():null,declared:this.declared}));
   this.toasts.show({text:r.approved?`Claimed ${new Intl.NumberFormat('en-AU',{style:'currency',currency:'AUD'}).format(r.deductibleAmount??0)}.`:r.status==='Unsupported'?'Kept for advice.':'Saved. It’s calculated once this year’s tax rules are ready.'});
   return true;
  }catch(error){this.message.set(error instanceof HttpErrorResponse?error.error?.detail??'Could not claim this receipt.':'Could not claim this receipt.');await this.load();return false;}}
 private async skipConfirmedQuietly(){const t=this.data()?.transaction;if(!t)return;this.busy.set(false);await this.dismiss(true,'Personal');}
 async dismiss(dismiss:boolean,reason:string|null=null){const t=this.data()?.transaction;if(!t||this.busy())return;this.busy.set(true);try{const result=await firstValueFrom(this.http.put<Transaction>(`/api/transactions/${t.id}/dismiss`,{version:t.version,dismiss,reason}));this.skipping.set(false);this.message.set(dismiss?'':'Restored to your review queue.');this.saved.emit();if(dismiss){this.done.set('dismissed');this.skipped.emit({transactionId:t.id,version:result.version,merchant:t.merchant??this.data()?.document.originalFileName??'Receipt'});this.finished.emit();}await this.load();}catch(error){this.message.set(error instanceof HttpErrorResponse?error.error?.detail??'Could not update this receipt. Reload and try again.':'Could not update this receipt.');}finally{this.busy.set(false);}}
 /** Skipping a confirmed receipt: it has no expenses yet, so it can go straight out of the return. */
 skipConfirmed(){return this.dismiss(true,null);}
 async remove(){if(this.busy())return;this.busy.set(true);try{await firstValueFrom(this.http.post(`/api/documents/${this.id()}/delete`,{confirm:true}));this.deleting.set(false);this.saved.emit();this.deleted.emit(this.data()?.document.originalFileName??'File');}catch(error){this.message.set(error instanceof HttpErrorResponse?error.error?.detail??'Could not delete this file.':'Could not delete this file.');}finally{this.busy.set(false);}}
 /** Stops waiting for AI on a receipt still in the queue; AI and the person never edit it at the same time. */
 async takeOver(){if(this.busy())return;this.busy.set(true);try{await firstValueFrom(this.http.post(`/api/documents/${this.id()}/take-over`,{}));await this.load();this.saved.emit();}catch(e){this.message.set(e instanceof HttpErrorResponse?e.error?.detail??'Could not update this receipt.':'Could not update this receipt.');await this.load();}finally{this.busy.set(false);}}
 async retry(){try{await firstValueFrom(this.http.post(`/api/documents/${this.id()}/retry`,{}));this.message.set('');await this.load();this.saved.emit();}catch{this.message.set('Could not queue processing. Try again shortly.');}}
 async manual(){try{await firstValueFrom(this.http.post(`/api/documents/${this.id()}/manual`,{}));await this.load();}catch{this.message.set('Could not start manual entry.');}}
}
