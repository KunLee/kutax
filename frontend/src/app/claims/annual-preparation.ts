import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Icon } from '../core/icon';
import { categories, categoryLabel } from '../core/categories';
import { Session } from '../core/session';
import { showAttention } from '../core/attention';
import { TranslatePipe } from '../core/i18n';
import { claimStatusLabel, Todo, TodoPanel } from './todo-panel';

interface Rule { version:string;financialYearStart:number;category:string;label:string;section:string;paperReference:string;sourceUrl:string;mappingUrl:string;conditions:string; }
interface Evidence { id:string;name:string;sha256:string; }
interface Row { allocationId:string;allocationVersion:string;transactionVersion:string;documentId:string;merchant:string;date:string;description:string;category:string;expenseAmount:number;workUsePercent:number;workReimbursement:number;basis:string;status:string;deductibleAmount:number|null;reason:string;ruleVersion:string|null;approvedAt:string|null;evidence:Evidence[];originalCurrency?:string|null;originalAmount?:number|null;conversionSource?:string|null;conversionRateDate?:string|null;claimType?:string|null; }
interface Section { section:string;paperReference:string;claimType:string|null;approvedTotal:number;entryAmount:number;claims:number;categories:string[]; }
interface Assessment { financialYearStart:number;rows:Row[];issues:{documentId:string;description:string;amount:number|null}[];approvedTotal:number;candidateTotal:number;excludedExpenseTotal:number;unassessedExpenseTotal:number;entryAmount:number;sections?:Section[]; }
interface Receipt {id:string;originalFileName:string;}
@Component({selector:'app-annual-preparation',imports:[TranslatePipe, CurrencyPipe,DatePipe,DecimalPipe,FormsModule,NgTemplateOutlet,RouterLink,Icon,TodoPanel],template:`
@if(message()){<p class="notice" role="status">{{message() | t}}</p>}
@if(data();as report){
@if(admin()&&pendingUpdate();as update){<a class="card section rules-banner update" [routerLink]="['/rules',update.id]"><app-icon name="spark" /><div><h2>{{ 'Tax rules for {year} are ready for your check' | t: { year: yearLabel(update.financialYearStart) } }}</h2><p class="small">{{ 'KuTax drafted them from the new ATO instructions and checked every figure against the official text. Nothing changes until you put them in force.' | t }}</p></div><app-icon name="arrow" /></a>}
@if(waitingOnRules().length&&!(admin()&&unreviewed().length)){<section class="card section rules-banner" role="status" aria-labelledby="rules-banner-title">
 <app-icon name="shield" /><div>@if(admin()){<h2 id="rules-banner-title">{{ (waitingOnRules().length===1?'{n} claim is waiting for a one-time rule check':'{n} claims are waiting for a one-time rule check') | t: { n: waitingOnRules().length } }}</h2>
 <p class="small">{{ 'KuTax calculates a category only after an administrator confirms its rule matches the ATO sources: {names}.' | t: { names: waitingRuleNames() } }}</p>}
 @else{<h2 id="rules-banner-title">{{ (waitingOnRules().length===1?'{n} claim will be calculated soon':'{n} claims will be calculated soon') | t: { n: waitingOnRules().length } }}</h2>
 <p class="small">{{ 'KuTax is still checking this year’s tax rules for {names}. Nothing for you to do: they update by themselves.' | t: { names: waitingRuleNames() } }}</p>}
 @if(admin()){<button class="btn btn--primary btn--small" type="button" (click)="openRuleReview()">{{ 'Check the rules' | t }}</button>}</div></section>}
@if(admin()&&unreviewed().length){<ng-container *ngTemplateOutlet="ruleReview" />}

<section class="hero">
 <div class="total">
  <span class="kicker">{{ 'Approved deductions' | t }}</span>
  <strong class="amount">{{dollars(report.approvedTotal)}}<span>.{{cents(report.approvedTotal)}}</span></strong>
  <p>{{ (approvedCount()===1?'Across {n} approved claim':'Across {n} approved claims') | t: { n: approvedCount() } }}</p>
  <div class="status" [class.status--wait]="pending()>0">
   <app-icon [name]="pending()?'clock':'check'" [size]="16" />{{pending()?('{n} waiting for your approval' | t: { n: pending() }):approvedCount()?('Everything assessed is approved' | t):('Nothing assessed yet' | t)}}</div>
 </div>
 <div class="card mix">
  <div class="mix-head"><strong>{{ 'Deduction mix' | t }}</strong><small>{{ 'Assessed claims' | t }}</small></div>
  <div class="donut" [style.background]="donut()" aria-hidden="true"><span><strong>{{mix().length}}</strong><small>{{(mix().length===1?'category':'categories') | t}}</small></span></div>
  <ul class="legend">
   @for(item of mix();track item.key){<li><i [style.background]="item.color"></i>{{item.label | t}}<strong>{{item.amount|currency:('AUD' | t):'symbol-narrow':'1.0-0'}}</strong></li>}
   @empty{<li class="muted">{{ 'No assessed deductions yet. Confirm receipts and split them into expenses first.' | t }}</li>}
  </ul>
 </div>
 @if(todoCount()){<app-todo-panel class="todo" [todos]="todos()" [pending]="pending()" [total]="report.candidateTotal" [busy]="busy()" (approve)="approveAll()" (more)="showAllTodos()" (show)="showStatus($event)" />}@else{
 <div class="mascot">
  <div class="mini" aria-hidden="true"><span></span><app-icon [name]="ready()?'check':'clock'" [size]="30" /></div>
  <strong>{{ready()?('Nicely done!' | t):('Almost there' | t)}}</strong>
  <p>{{ready()?('Every claim is reviewed. Prepare your tax pack below.' | t):('Approve each claim and clear the exceptions before you prepare your tax pack.' | t)}}</p>
 </div>}
</section>
@if(todos().length>3){<app-todo-panel id="all-todos" class="card section" [class.flash]="flashTodos()" mode="all" [todos]="todos()" [pending]="pending()" [total]="report.candidateTotal" [busy]="busy()" (approve)="approveAll()" (show)="showStatus($event)" />}
<div class="totals">
 <div><small>{{ 'Awaiting approval' | t }}</small><strong>{{report.candidateTotal|currency:('AUD' | t):'symbol-narrow'}}</strong></div>
 <div><small>{{ 'Unassessed expenses' | t }}</small><strong>{{report.unassessedExpenseTotal|currency:('AUD' | t):'symbol-narrow'}}</strong></div>
 <div><small>{{ 'Excluded expenses' | t }}</small><strong>{{report.excludedExpenseTotal|currency:('AUD' | t):'symbol-narrow'}}</strong></div>
</div>
<p class="muted small">{{ 'Expense amounts and deductible amounts differ. These are deductions, not a refund: Reports gives a rough idea of the tax they save.' | t }}</p>

@if(report.sections?.length){<section class="card section" aria-labelledby="mytax-title">
 <div class="section-heading"><div><span class="kicker">{{ 'For myTax' | t }}</span><h2 id="mytax-title">{{ 'What goes where' | t }}</h2></div></div>
 <p class="muted small">{{ 'Approved claims by myTax deduction section. Each section adds up the cents first, then drops them once. Prepare your tax pack to freeze these figures.' | t }}</p>
 <ul class="sections">@for(item of report.sections;track item.paperReference){<li>
  <span><strong>{{item.section}}</strong><small>{{item.paperReference}}@if(item.claimType){ · {{ 'claim type' | t }} {{item.claimType}}} · {{ (item.claims===1?'{n} claim':'{n} claims') | t: { n: item.claims } }}</small></span>
  <strong class="entry">{{'$'+item.entryAmount.toLocaleString('en-AU')}}</strong>
  <button class="btn btn--small" type="button" (click)="copy(item)">{{ 'Copy' | t }}<span class="sr-only"> {{item.section}}</span></button></li>}</ul>
</section>}



<section id="claims" class="card section" aria-labelledby="claims-title">
 <div class="section-heading"><div><span class="kicker">{{ 'Breakdown' | t }}</span><h2 id="claims-title">{{ 'Claims by expense' | t }}</h2></div>
  <button class="btn btn--small" type="button" (click)="load()" [disabled]="busy()">{{ 'Refresh' | t }}</button></div>
 @if(statusFilters().length>2){<div class="filters" role="group" [attr.aria-label]="'Show claims' | t">@for(f of statusFilters();track f.key){<button type="button" class="chip" [attr.aria-pressed]="status()===f.key" (click)="status.set(f.key)">{{f.label | t}} {{f.count}}</button>}</div>}
 @if(!report.rows.length){<p class="empty"><strong>{{ 'No expenses for this year yet' | t }}</strong>{{ 'Receipts dated in this financial year appear here once they’re confirmed and split into expenses.' | t }}</p>}
 <ul class="claims">
 @for(row of shownRows();track row.allocationId){<li class="claim">
  <div class="claim-top"><span class="thumb" [class]="tone(row.status)"><app-icon name="archive" /></span>
   <span class="claim-copy"><strong>{{row.description}}</strong><small>{{row.merchant}} · {{row.date|date:('d MMM yyyy' | t)}} · {{label(row.category) | t}}</small></span>
   <span class="claim-end"><strong>{{row.deductibleAmount===null?(amountNote(row.status) | t):(row.deductibleAmount|currency:('AUD' | t):'symbol-narrow')}}</strong><span class="chip" [class]="chip(row.status)">{{statusLabel(row.status) | t}}</span></span></div>
  <p class="small calc">{{row.expenseAmount|currency:('AUD' | t):'symbol-narrow'}} × {{ '{pct}% work use' | t: { pct: row.workUsePercent } }} − {{ '{amount} reimbursed' | t: { amount: (row.workReimbursement|currency:'AUD':'symbol-narrow') } }}</p>
  @if(row.originalCurrency){<p class="small muted">{{ 'Receipt in {currency} {amount}' | t: { currency: row.originalCurrency, amount: (row.originalAmount|number:'1.2-2') } }} · {{row.conversionSource==='RBA'?('AUD estimated at the RBA rate for {date}' | t: { date: (row.conversionRateDate|date:('d MMM yyyy' | t)) }):('AUD from your statement' | t)}}</p>}
  <p class="small muted">{{row.reason | t}}</p>
  <details><summary>{{ 'Why this amount, and attachments' | t }}</summary>
   @if(row.basis){<p class="small">{{row.basis}}</p>}
   @if(ruleFor(row);as rule){<p class="small"><strong>{{ 'Rule {version}.' | t: { version: rule.version } }}</strong>  {{rule.conditions | t}}</p><p class="small"><a [href]="rule.sourceUrl" target="_blank" rel="noopener">{{ 'ATO eligibility guidance' | t }}</a> · <a [href]="rule.mappingUrl" target="_blank" rel="noopener">{{ 'FY instructions' | t }}</a> · {{rule.section}}, {{ 'paper reference' | t }} {{rule.paperReference}}</p>}
   <p class="small"><strong>{{ 'Attachments:' | t }}</strong> @for(doc of row.evidence;track doc.id){<a [href]="'/api/documents/'+doc.id+'/content?download=1'" download>{{doc.name}}</a>@if(doc.id===row.documentId){ <span class="muted">{{ '(the receipt, attached automatically)' | t }}</span>} }</p>
   @if(evidenceRow()?.allocationId!==row.allocationId){
    <p class="small muted">{{ 'The receipt is all most claims need.' | t }} {{evidenceHint(row.category) | t}}</p>
    <button class="btn btn--small btn--quiet" type="button" (click)="editEvidence(row)" [disabled]="busy()">{{ 'Add attachments' | t }}</button>
   }@else{
    <div class="evidence" role="group" [attr.aria-label]="'Supporting documents for '+row.description">
     <p class="small">{{ 'Upload a document that backs up this claim, or tick one you’ve already uploaded. They’re checked for malware but not read as receipts.' | t }}@if(row.status==='Approved'){ <strong>{{ 'Changing them means approving this claim again.' | t }}</strong>}</p>
     <label class="btn btn--small upload"><app-icon name="upload" [size]="16" />{{uploading()?('Uploading…' | t):('Upload a document' | t)}}<input type="file" accept="application/pdf,image/jpeg,image/png" (change)="uploadSupporting(row,$event)" [disabled]="busy()||uploading()"></label>
     <label class="field">{{ 'Find a document' | t }}<input type="search" [(ngModel)]="search" (ngModelChange)="findDocuments()" [placeholder]="'File name' | t"></label>
     @for(doc of documentOptions();track doc.id){@if(doc.id!==row.documentId){<label class="check"><input type="checkbox" [ngModel]="selectedDocuments.has(doc.id)" (ngModelChange)="toggleDocument(doc.id,$event)"><span>{{doc.originalFileName}}</span></label>}}
     @empty{<p class="small muted">{{ 'No other documents match.' | t }}</p>}
     <label class="field">{{ 'Note (optional)' | t }}<textarea [(ngModel)]="evidenceNote" maxlength="500" [placeholder]="'For example: 4-week phone diary, March 2026' | t"></textarea></label>
     <div class="row"><button class="btn btn--primary btn--small" type="button" (click)="saveEvidence()" [disabled]="busy()">{{ 'Save documents' | t }}</button><button class="btn btn--quiet btn--small" type="button" (click)="evidenceRow.set(null)">{{ 'Cancel' | t }}</button></div>
    </div>
   }
  </details>
  <div class="row"><a class="btn btn--small" [routerLink]="['/review',row.documentId]">{{ 'Review receipt' | t }}<span class="sr-only"> {{ 'for {name}' | t: { name: row.description } }}</span></a></div>
  @if(row.status==='Candidate'){<div class="approve"><label class="check"><input type="checkbox" [id]="'approve-'+row.allocationId" [ngModel]="confirmations[row.allocationId]||false" (ngModelChange)="confirmations[row.allocationId]=$event"><span>{{ 'I reviewed the evidence, calculation and rule conditions and approve this deduction. I checked for duplicate claims and payroll/myTax prefill.' | t }}</span></label>
   <button class="btn btn--primary btn--block" type="button" (click)="approve(row)" [disabled]="busy()" [attr.aria-busy]="busy()&&approving()===row.allocationId"><app-icon name="check" [size]="18" />{{ 'Approve {name}' | t: { name: row.description } }}</button></div>}
  @if(row.status==='Approved'){<p class="small muted">{{ 'Approved {date} · edits require fresh approval.' | t: { date: (row.approvedAt|date:('d MMM yyyy, h:mm a' | t)) } }}</p>}
 </li>}
 </ul>
</section>


}

@if(admin()&&!unreviewed().length){<ng-container *ngTemplateOutlet="ruleReview" />}
<ng-template #ruleReview><details id="rule-review" class="card section admin" [open]="unreviewed().length>0"><summary>@if(unreviewed().length){<strong>{{ (unreviewed().length===1?'One-time check: {n} tax rule for {year}':'One-time check: {n} tax rules for {year}') | t: { n: unreviewed().length, year: yearName() } }}</strong> <span class="chip chip--review">{{waitingOnRules().length}} {{waitingOnRules().length===1?('claim waits' | t):('claims wait' | t)}}</span>}@else{ {{ 'Administrator: tax rules for this year' | t }} }</summary><p class="small">{{ 'Each rule is a short calculation card: which myTax section it goes to, the conditions, any limits, and the formula (amount × work use − reimbursement). KuTax drafted them from the ATO pages linked on each card. Confirm they match, and KuTax starts calculating those categories for everyone. This doesn’t approve any claim.' | t }}</p>
 @if(unreviewed().length>1){<div class="batch"><p class="small"><strong>{{ 'Check all {n} at once:' | t: { n: unreviewed().length } }}</strong></p>
  <ul class="small cards">@for(rule of unreviewed();track rule.version){<li><strong>{{(rule.label||rule.category) | t}}</strong> → {{rule.section}} ({{rule.paperReference}}) · <a [href]="rule.sourceUrl" target="_blank" rel="noopener">{{ 'ATO page' | t }}</a></li>}</ul>
  <label class="field">{{ 'Review note' | t }}<textarea id="batch-note" [(ngModel)]="batchNote" maxlength="2000" [placeholder]="'For example: checked conditions and labels against the ATO pages and the year’s instructions' | t"></textarea></label>
  <label class="check"><input id="batch-confirm" type="checkbox" [(ngModel)]="batchConfirmed"><span>{{ 'I checked each rule’s eligibility, evidence conditions and this financial year’s mapping against the official sources linked below.' | t }}</span></label>
  <button class="btn btn--primary" type="button" (click)="reviewAll()" [disabled]="busy()">{{ 'Record review of {n} rules' | t: { n: unreviewed().length } }}</button></div>}
 @if(yearRules().length){<details class="each" [open]="unreviewed().length<=1"><summary class="small">{{ 'Read each rule’s conditions ({n})' | t: { n: yearRules().length } }}</summary>
 @for(rule of yearRules();track rule.version){<article class="rule"><h3>{{(rule.label||rule.category) | t}} · {{rule.version}}</h3><p class="small muted">{{rule.section}} · {{ 'paper reference' | t }} {{rule.paperReference}}</p><p class="small">{{rule.conditions | t}}</p><p class="small"><a [href]="rule.sourceUrl" target="_blank" rel="noopener">{{ 'ATO eligibility source' | t }}</a> · <a [href]="rule.mappingUrl" target="_blank" rel="noopener">{{ 'ATO year instructions' | t }}</a></p>
  @if(reviewFor(rule);as review){
   <p class="notice notice--success small">{{ 'Reviewed {date} by {who}.' | t: { date: (review.reviewedAt|date:('d MMM yyyy' | t)), who: (review.actorId===userId()?'you':'another administrator') | t } }} <em>“{{review.note}}”</em></p>
   @if(review.actorId!==userId()){
    @if(superseding[rule.version]){<p class="small muted">{{ 'Your review replaces this sign-off. The earlier review stays in the audit history and in tax packs already prepared.' | t }}</p><label class="field">{{ 'Review note' | t }}<textarea [(ngModel)]="reviewNotes[rule.version]" maxlength="2000"></textarea></label><label class="check"><input type="checkbox" [(ngModel)]="ruleConfirmed[rule.version]"><span>{{ 'I checked eligibility, evidence conditions and this financial year’s mapping against the official sources.' | t }}</span></label><button class="btn btn--primary" type="button" (click)="reviewRule(rule)" [disabled]="busy()||!ruleConfirmed[rule.version]||!reviewNotes[rule.version]">{{ 'Record my review' | t }}</button>}
    @else{<button class="btn btn--small" type="button" (click)="superseding[rule.version]=true">{{ 'Record my own review' | t }}</button>}
   }
  }@else{<label class="field">{{ 'Review note' | t }}<textarea [(ngModel)]="reviewNotes[rule.version]" maxlength="2000"></textarea></label><label class="check"><input type="checkbox" [(ngModel)]="ruleConfirmed[rule.version]"><span>{{ 'I checked eligibility, evidence conditions and this financial year’s mapping against the official sources.' | t }}</span></label><button class="btn btn--primary" type="button" (click)="reviewRule(rule)" [disabled]="busy()||!ruleConfirmed[rule.version]||!reviewNotes[rule.version]">{{ 'Record rule review' | t }}</button>}</article>}
 </details>}
 @if(!yearRules().length){<p class="notice notice--warning">{{ 'There are no tax rules for this financial year yet, so KuTax can’t calculate or approve claims for it. For the year in progress, the ATO publishes its instructions around June, and KuTax prepares the rules then.' | t }}</p>}</details></ng-template>
`,styles:[`
:host{display:block}
.hero{display:grid;gap:18px;grid-template-columns:repeat(3,minmax(0,1fr))}
.filters{display:flex;flex-wrap:wrap;gap:6px;margin-bottom:12px}.filters .chip{cursor:pointer}.filters [aria-pressed=true]{background:var(--ink);color:var(--white)}#claims{scroll-margin-top:96px}.total{background:var(--purple-dark);border-radius:var(--radius);color:var(--white);min-height:270px;padding:26px}.total .kicker{color:#cbbdfa}
.amount{display:block;font-size:clamp(40px,6vw,52px);letter-spacing:-.05em;margin:18px 0 0}.amount span{color:#cbbdfa;font-size:.5em}
.total p{color:#d4cfea;font-size:14px}
.status{align-items:center;background:rgba(141,221,191,.14);border:1px solid rgba(141,221,191,.3);border-radius:12px;color:var(--mint);display:flex;font-size:13px;gap:7px;margin-top:28px;padding:10px}
.status--wait{background:rgba(255,138,92,.14);border-color:rgba(255,138,92,.35);color:#ffc2a8}
.mix{display:grid;gap:18px;grid-template-columns:130px 1fr}.mix-head{display:flex;grid-column:1/-1;justify-content:space-between}.mix-head small{color:var(--ink-soft)}
.donut{align-items:center;border-radius:50%;display:flex;height:130px;justify-content:center;position:relative;width:130px}.donut::after{background:var(--white);border-radius:50%;content:'';inset:20px;position:absolute}
.donut span{position:relative;text-align:center;z-index:1}.donut strong,.donut small{display:block}.donut strong{font-size:24px}.donut small{color:var(--ink-soft);font-size:11px}
.legend{align-content:center;display:grid;gap:10px;list-style:none;margin:0;padding:0}.legend li{align-items:center;display:grid;font-size:13px;gap:8px;grid-template-columns:10px 1fr auto}.legend i{border-radius:50%;height:10px;width:10px}.legend li.muted{display:block}
.mascot{align-items:center;background:var(--yellow);border:2px solid var(--ink);border-radius:var(--radius);box-shadow:5px 5px 0 var(--ink);display:flex;flex-direction:column;justify-content:center;padding:22px;text-align:center}
.mascot p{font-size:13px;margin:6px 0 0}
.mini{align-items:center;background:var(--sky);border:2px solid var(--ink);border-radius:19px;box-shadow:4px 5px 0 var(--ink);display:flex;height:80px;justify-content:center;margin-bottom:16px;position:relative;width:88px}
.mini span{background:var(--orange);border:2px solid var(--ink);border-radius:50%;height:10px;position:absolute;right:9px;top:9px;width:10px}.mini app-icon{background:var(--white);border:2px solid var(--ink);border-radius:9px;padding:4px}
.totals{display:grid;gap:18px;grid-template-columns:repeat(3,minmax(0,1fr));margin:18px 0 8px}.totals div{background:var(--white);border:1px solid var(--line);border-radius:16px;padding:14px 16px}.totals small,.totals strong{display:block}.totals small{color:var(--ink-soft);font-size:12px}.totals strong{font-size:18px;margin-top:4px}
.claims{display:grid;gap:12px;grid-template-columns:minmax(0,1fr);list-style:none;margin:0;padding:0}.claim{border-top:1px solid var(--line);min-width:0;overflow-wrap:anywhere;padding:14px 0 4px}
.claim-top{align-items:center;display:grid;gap:12px;grid-template-columns:44px minmax(0,1fr) auto}.claim-copy strong,.claim-copy small{display:block}.claim-copy small{color:var(--ink-soft);font-size:12px;margin-top:3px}
.claim-end{display:grid;gap:4px;justify-items:end}.calc{margin:10px 0 2px}.approve{background:#fbfaf6;border:1px solid var(--line);border-radius:14px;margin-top:12px;padding:4px 14px 14px}
.issue{align-items:center;background:var(--yellow-soft);border-radius:14px;display:grid;font-size:14px;gap:10px;grid-template-columns:20px 1fr auto;margin-bottom:8px;padding:10px 12px}
.rule{border-top:1px solid var(--line);padding-top:14px}.upload{cursor:pointer;margin:0 0 10px;position:relative}.upload input{inset:0;opacity:0;position:absolute;width:100%}.evidence{background:#fbfaf6;border:1px solid var(--line);border-radius:14px;margin:8px 0;padding:4px 14px 12px}
.admin>summary{cursor:pointer;font-size:16px}.cards{margin:4px 0 8px;padding-left:18px}.each>summary{cursor:pointer;font-weight:700;margin:4px 0}.admin[open]{border:2px solid var(--orange-ink)}.update{color:inherit;text-decoration:none}.update div{flex:1}.rules-banner{align-items:start;background:var(--yellow-soft);display:flex;gap:14px;margin-bottom:18px}.rules-banner h2{font-size:18px;margin:0 0 4px}.rules-banner app-icon{color:var(--orange-ink);flex:none;margin-top:2px}
.sections{display:grid;gap:0;list-style:none;margin:0;padding:0}.sections li{align-items:center;border-top:1px solid var(--line);display:grid;gap:12px;grid-template-columns:minmax(0,1fr) auto auto;padding:12px 0}.sections strong,.sections small{display:block}.sections small{color:var(--ink-soft);font-size:12px}.entry{font-size:20px;font-variant-numeric:tabular-nums}
.batch{background:#fbfaf6;border:1px solid var(--line);border-radius:14px;margin-bottom:12px;padding:4px 14px 14px}
@media(max-width:960px){.hero{grid-template-columns:1fr 1fr}.hero .total{order:-2}.hero .todo{order:-1}.total{grid-column:1/-1;min-height:0}}
@media(max-width:700px){.hero{grid-template-columns:1fr}.mix{grid-template-columns:110px 1fr}.donut{height:110px;width:110px}.donut::after{inset:17px}.totals{grid-template-columns:1fr}.totals div{align-items:baseline;display:flex;justify-content:space-between}.totals strong{margin:0}.mascot{flex-direction:row;gap:14px;text-align:left}.mascot .mini{flex:none;height:60px;margin:0;width:64px}.claim-top{grid-template-columns:44px minmax(0,1fr)}.claim-end{align-items:center;display:flex;grid-column:2;justify-content:space-between}}
`]})
export class AnnualPreparation {
 readonly year=input.required<number>();readonly admin=input(false);private readonly http=inject(HttpClient);
 readonly data=signal<Assessment|null>(null);readonly rules=signal<Rule[]>([]);readonly reviews=signal<{ruleVersion:string;actorId:string;reviewedAt:string;note:string}[]>([]);readonly userId=computed(()=>this.session.account()?.id??'');private readonly session=inject(Session);readonly busy=signal(false);readonly message=signal('');
 readonly evidenceRow=signal<Row|null>(null);readonly documentOptions=signal<Receipt[]>([]);selectedDocuments=new Set<string>();search='';evidenceNote='';
 confirmations:Record<string,boolean>={};batchNote='Checked against the linked ATO pages and this year’s instructions.';batchConfirmed=false;superseding:Record<string,boolean>={};reviewNotes:Record<string,string>={};ruleConfirmed:Record<string,boolean>={};
 readonly approvedCount=computed(()=>this.data()?.rows.filter(r=>r.status==='Approved').length??0);
 readonly pending=computed(()=>this.data()?.rows.filter(r=>r.status==='Candidate').length??0);
 /** Claims the person has to fix before they can be approved (missing details or evidence). */
 readonly needsWork=computed(()=>this.data()?.rows.filter(r=>r.status==='NeedsReview'||r.status==='NeedsEvidence')??[]);
 /** Everything waiting on the person, shown together at the top of the page. */
 /** Everything waiting on the person, most urgent first: approving, then claims to fix, then receipt exceptions. */
 readonly todos=computed<Todo[]>(()=>[
  ...(this.pending()?[{kind:'approve' as const,key:'approve'}]:[]),
  ...this.needsWork().map(row=>({kind:'row' as const,key:'row-'+row.allocationId,row})),
  ...(this.data()?.issues??[]).map((issue,i)=>({kind:'issue' as const,key:'issue-'+i,issue}))]);
 readonly todoCount=computed(()=>this.todos().length);
 /** Breakdown filter: a status group, counted from the year's rows; groups with nothing in them aren't offered. */
 readonly status=signal('all');
 private readonly groups:{key:string;label:string;of:string[]}[]=[{key:'Candidate',label:'Ready to approve',of:['Candidate']},{key:'fix',label:'Needs details',of:['NeedsReview','NeedsEvidence']},
  {key:'UnverifiedRule',label:'Rule not checked yet',of:['UnverifiedRule']},{key:'Unsupported',label:'Needs advice',of:['Unsupported']},{key:'Approved',label:'Approved',of:['Approved']},{key:'Excluded',label:'Excluded',of:['Excluded']}];
 readonly statusFilters=computed(()=>{const rows=this.data()?.rows??[];return [{key:'all',label:'All',count:rows.length},...this.groups.map(g=>({key:g.key,label:g.label,count:rows.filter(r=>g.of.includes(r.status)).length})).filter(g=>g.count)];});
 readonly shownRows=computed(()=>{const group=this.groups.find(g=>g.key===this.status());return group?this.orderedRows().filter(r=>group.of.includes(r.status)):this.orderedRows();});
 showStatus(key:string){this.status.set(key);setTimeout(()=>document.getElementById('claims')?.scrollIntoView({behavior:'smooth',block:'start'}));}
 readonly flashTodos=signal(false);
 /** The claim whose Approve button was pressed. */
 readonly approving=signal<string|null>(null);
 /** The tile shows three; the full list sits full width just below, so no single column grows on its own. */
 showAllTodos(){const all=document.getElementById('all-todos');if(!all)return;const reduced=typeof matchMedia==='function'&&matchMedia('(prefers-reduced-motion: reduce)').matches;all.scrollIntoView({behavior:reduced?'auto':'smooth',block:'start'});this.flashTodos.set(true);setTimeout(()=>this.flashTodos.set(false),1600);}
 /** To-dos first, then rule checks, advice and exclusions, then what's already approved. */
 readonly orderedRows=computed(()=>{const rank:Record<string,number>={Candidate:0,NeedsReview:1,NeedsEvidence:1,UnverifiedRule:2,Unsupported:3,Excluded:4,Approved:5};return [...(this.data()?.rows??[])].sort((a,b)=>(rank[a.status]??3)-(rank[b.status]??3));});
 readonly ready=computed(()=>!!this.data()&&this.approvedCount()>0&&this.pending()===0&&!this.data()!.issues.length);
 /** Deductions (approved or awaiting approval) grouped by category, from the deterministic assessment. */
 readonly mix=computed(()=>{const totals=new Map<string,number>();for(const row of this.data()?.rows??[])if(row.deductibleAmount&&(row.status==='Approved'||row.status==='Candidate'))totals.set(row.category,(totals.get(row.category)??0)+row.deductibleAmount);
  return [...totals].sort((a,b)=>b[1]-a[1]).map(([key,amount])=>({key,amount,label:this.label(key),color:categories[key]?.color??'var(--yellow)'}));});
 readonly donut=computed(()=>{const items=this.mix();const sum=items.reduce((s,i)=>s+i.amount,0);if(!sum)return 'var(--purple-soft)';let start=0;
  return 'conic-gradient('+items.map(i=>{const end=start+i.amount/sum*100;const part=`${i.color} ${start}% ${end}%`;start=end;return part;}).join(',')+')';});
 constructor(){effect(()=>{this.year();void this.load();});effect(()=>{if(this.admin())void this.loadUpdates();});}
 label(category:string){return categoryLabel(category);}
 dollars(value:number){return '$'+Math.trunc(value).toLocaleString('en-AU');}
 cents(value:number){return String(Math.round((value-Math.trunc(value))*100)).padStart(2,'0');}
 tone(status:string){return status==='Approved'?'tone-mint':status==='Candidate'?'tone-purple':status==='Excluded'?'tone-yellow':'tone-orange';}
 chip(status:string){return status==='Approved'?'chip--done':status==='Candidate'?'chip--purple':'chip--review';}
 /** Rows that would be assessed once their rule is reviewed. */
 readonly pendingUpdate=signal<{id:string;financialYearStart:number}|null>(null);
 yearLabel(year:number){return `FY ${year}–${String(year+1).slice(2)}`;}
 /** Administrators see a rule update waiting for their check. */
 async loadUpdates(){if(!this.admin())return;try{const r=await firstValueFrom(this.http.get<{packs:{id:string;financialYearStart:number;status:string}[]}>('/api/rule-packs'));this.pendingUpdate.set(r.packs.find(p=>p.status==='Draft')??null);}catch{/* optional */}}
 readonly waitingOnRules=computed(()=>this.data()?.rows.filter(r=>r.status==='UnverifiedRule')??[]);
 yearName(){const y=this.year();return `FY ${y}–${String(y+1).slice(2)}`;}
 waitingRuleNames(){return [...new Set(this.waitingOnRules().map(r=>this.rules().find(rule=>rule.version===r.ruleVersion)?.label||this.label(r.category)))].join(', ');}
 openRuleReview(){const panel=document.getElementById('rule-review') as HTMLDetailsElement|null;if(!panel)return;panel.open=true;panel.scrollIntoView({behavior:'smooth',block:'start'});}
 readonly statusLabel=claimStatusLabel;
 /** Why there's no amount: KuTax doesn't calculate it, is still waiting on something, or it isn't claimed. */
 amountNote(status:string){return status==='Unsupported'?'Not calculated by KuTax':status==='Excluded'?'Not claimed':'Not calculated yet';}
 readonly unreviewed=computed(()=>this.rules().filter(r=>r.financialYearStart===this.year()&&!this.reviews().some(v=>v.ruleVersion===r.version)));
 unreviewedNames(){return this.unreviewed().map(r=>r.label||r.category).join(', ');}
 async copy(item:Section){try{await navigator.clipboard.writeText(String(item.entryAmount));this.message.set(`${item.section}: ${item.entryAmount} copied. Reconcile prefill before entering it.`);}catch{this.message.set('Clipboard unavailable. Copy the amount shown.');}}
 /** Approves every claim currently waiting, one by one with each claim's own version checks; stops at the first that changed. */
 async approveAll(){const rows=this.data()?.rows.filter(r=>r.status==='Candidate')??[];await this.action(async()=>{for(const row of rows)await firstValueFrom(this.http.post(`/api/allocations/${row.allocationId}/approve`,{allocationVersion:row.allocationVersion,transactionVersion:row.transactionVersion,ruleVersion:row.ruleVersion,confirm:true}));},`${rows.length} claims approved.`);}
 async reviewAll(){if(!this.batchNote.trim()){showAttention('batch-note','Add a short note, such as “Checked against the ATO pages”.');return;}if(!this.batchConfirmed){showAttention('batch-confirm','Tick this to confirm you checked the rules.');return;}const versions=this.unreviewed().map(r=>r.version);await this.action(()=>firstValueFrom(this.http.post('/api/tax-rules/review-batch',{versions,confirm:this.batchConfirmed,note:this.batchNote.trim()})),`${versions.length} rules reviewed.`);this.batchConfirmed=false;}
 yearRules(){return this.rules().filter(r=>r.financialYearStart===this.year());}
 ruleFor(row:Row){return this.rules().find(r=>r.version===row.ruleVersion);}
 async load(){try{const [data,rules]=await Promise.all([firstValueFrom(this.http.get<Assessment>(`/api/tax-years/${this.year()}/claims`)),firstValueFrom(this.http.get<{rules:Rule[];reviews:{ruleVersion:string;actorId:string;reviewedAt:string;note:string}[]}>('/api/tax-rules'))]);this.data.set(data);this.rules.set(rules.rules);this.reviews.set(rules.reviews);}catch{this.message.set('Could not validate this year. Refresh and try again.');}}
 async action(work:()=>Promise<unknown>,success:string){if(this.busy())return;this.busy.set(true);try{await work();this.message.set(success);await this.load();}catch(error){this.message.set(error instanceof HttpErrorResponse?error.error?.detail??'The record changed or the action could not be completed. Refresh and retry.':'Could not complete the action.');}finally{this.busy.set(false);}}
 async approve(row:Row){if(!this.confirmations[row.allocationId]){showAttention('approve-'+row.allocationId,'Tick this to confirm you checked the claim.');return;}this.approving.set(row.allocationId);await this.action(()=>firstValueFrom(this.http.post(`/api/allocations/${row.allocationId}/approve`,{allocationVersion:row.allocationVersion,transactionVersion:row.transactionVersion,ruleVersion:row.ruleVersion,confirm:this.confirmations[row.allocationId]})),'Claim approval recorded.');this.approving.set(null);}
 async reviewRule(rule:Rule){await this.action(()=>firstValueFrom(this.http.post(`/api/tax-rules/${rule.version}/review`,{confirm:this.ruleConfirmed[rule.version],note:this.reviewNotes[rule.version],supersede:!!this.superseding[rule.version]})),'Rule review recorded.');this.superseding[rule.version]=false;}
 reviewFor(rule:Rule){return this.reviews().find(r=>r.ruleVersion===rule.version)??null;}
 /** What extra paperwork is worth attaching for a category, if any. */
 evidenceHint(category:string){return ({PhoneInternet:'For phone and internet, a 4-week usage diary backs up your work-use percentage.',SelfEducation:'For study, an enrolment or fee statement showing a full-fee or FEE-HELP place helps.',WorkClothing:'For uniforms, your employer’s uniform policy helps.',Donations:'A receipt showing the charity’s DGR status is enough.'} as Record<string,string>)[category]??'Add more only if something else backs it up, such as a bank statement for a foreign-currency purchase.';}
 readonly uploading=signal(false);
 /** Uploads one supporting document (scanned, never read as a receipt) and attaches it to this claim straight away. */
 async uploadSupporting(row:Row,event:Event){const input=event.target as HTMLInputElement;const file=input.files?.[0];input.value='';if(!file)return;
  if(file.size>20*1024*1024){this.message.set('Each document must be 20 MB or smaller.');return;}
  this.uploading.set(true);
  try{const batch=await firstValueFrom(this.http.post<{id:string}>('/api/import-batches/',{sourceKind:'Files',expectedFiles:1,purpose:'Supporting'}));
   const item=await firstValueFrom(this.http.put<{documentId:string}>(`/api/import-batches/${batch.id}/items/${crypto.randomUUID()}`,file,{headers:{'X-File-Name':encodeURIComponent(file.name),'X-Relative-Path':encodeURIComponent(file.name),'Content-Type':'application/octet-stream'}}));
   await firstValueFrom(this.http.post(`/api/import-batches/${batch.id}/complete`,{}));
   this.selectedDocuments.add(item.documentId);
   if(!this.evidenceNote.trim())this.evidenceNote=file.name;
   await this.saveEvidence();
   this.message.set(`${file.name} is attached to ${row.description}. It’s checked for malware in a moment.`);}
  catch(error){this.message.set(error instanceof HttpErrorResponse?error.error?.detail??'That document couldn’t be uploaded.':'That document couldn’t be uploaded.');}
  finally{this.uploading.set(false);}}
 async editEvidence(row:Row){this.evidenceRow.set(row);this.evidenceNote='';this.search='';this.selectedDocuments=new Set(row.evidence.filter(e=>e.id!==row.documentId).map(e=>e.id));await this.findDocuments();}
 async findDocuments(){try{const search=this.search.trim().slice(0,100);const page=await firstValueFrom(this.http.get<{items:Receipt[]}>('/api/documents/',{params:search?{page:1,search,kind:'any'}:{page:1,kind:'any'}}));this.documentOptions.set(page.items);}catch{this.message.set('Could not load supporting documents.');}}
 toggleDocument(id:string,on:boolean){if(on)this.selectedDocuments.add(id);else this.selectedDocuments.delete(id);}
 async saveEvidence(){const row=this.evidenceRow();if(!row)return;await this.action(()=>firstValueFrom(this.http.put(`/api/allocations/${row.allocationId}/evidence`,{version:row.allocationVersion,documents:[...this.selectedDocuments],note:this.evidenceNote})),'Attachments saved. Approve the claim again.');this.evidenceRow.set(null);}
}
