import { Component, effect, inject, input, OnDestroy, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Icon } from '../core/icon';
import { RouterLink } from '@angular/router';
import { showAttention } from '../core/attention';
import { PackFiles } from './pack-files';
import { MyTaxSection, progressFor, sectionsOf } from '../core/mytax';
import { TranslatePipe } from '../core/i18n';

interface Preview {hash:string;assessment:{approvedTotal:number;candidateTotal:number;unassessedExpenseTotal:number;excludedExpenseTotal:number;entryAmount:number;rows:{status:string}[];issues:unknown[];sections?:MyTaxSection[]};blockers:string[];warnings:string[];
 counts?:{receipts:number;skipped:number;approvedClaims:number;keptForAdvice:number;otherOpen:number;evidenceFiles:number};}
interface History {id:string;approvedAt:string;approvedBy:string;sha256:string;status:string;errorCode:string|null;}
interface Package extends History {model:{approvalNote:string;assessment:Preview['assessment']};progress:{section:string;status:string;updatedAt:string}[];files:{kind:string;fileName:string;size:number;sha256:string}[];}
@Component({selector:'app-tax-exports',imports:[TranslatePipe, CurrencyPipe,DatePipe,FormsModule,RouterLink,Icon,PackFiles],template:`
<section class="strip" aria-labelledby="pack-title">
 <span class="strip-icon"><app-icon name="report" /></span>
 <span class="strip-copy"><strong id="pack-title">{{ 'Your tax pack' | t }}</strong><small>{{ 'A summary, a myTax guide, a spreadsheet and your receipts — in English and Chinese' | t }}</small></span>
 <button class="btn btn--dark" type="button" (click)="validate()" [disabled]="busy()"><app-icon name="check" [size]="18" />{{ 'Check & prepare' | t }}</button>
</section>
@if(message()){<p class="notice" role="status">{{message() | t}}</p>}
@if(preview();as value){<section class="card section" aria-labelledby="snapshot-title">
 <span class="kicker">{{ 'Before you freeze it' | t }}</span><h2 id="snapshot-title">{{ 'FY {year} at a glance' | t: { year: year() + '–' + (year()+1).toString().slice(-2) } }}</h2>
 <p class="small muted">{{ 'This is what goes into your tax pack. Preparing it freezes these figures; later changes don’t alter it (you can prepare a new one any time).' | t }}</p>
 <div class="lead"><small>{{ 'Deductions you can claim' | t }}</small><strong>{{value.assessment.approvedTotal|currency:('AUD' | t):'symbol-narrow'}}</strong>
  <p class="small">{{ 'This lowers your taxable income — it isn’t your refund. What you get back depends on your tax rate and the tax already withheld from your pay, which myTax works out.' | t }}</p></div>
 @if(value.assessment.sections?.length){<h3 class="sub">{{ 'What you’ll enter in myTax' | t }}</h3>
  <ul class="entries">@for(part of value.assessment.sections;track part.paperReference){<li><span><strong>{{part.section}}</strong><small>{{part.paperReference}}@if(part.claimType){ · {{ 'claim type' | t }} {{part.claimType}}} · {{ (part.claims===1?'{n} claim':'{n} claims') | t: { n: part.claims } }}</small></span><strong class="dollars">{{'$'+part.entryAmount.toLocaleString('en-AU')}}</strong></li>}</ul>}
 @if(value.counts;as c){<div class="figures">
  <div><small>{{ 'Receipts this year' | t }}</small><strong>{{c.receipts}}</strong></div>
  <div><small>{{ 'Claimed' | t }}</small><strong>{{c.approvedClaims}}</strong></div>
  <div><small>{{ 'Skipped' | t }}</small><strong>{{c.skipped}}</strong></div>
  <div><small>{{ 'Kept for advice' | t }}</small><strong>{{c.keptForAdvice}}</strong></div>
 </div>}
 <details class="small more"><summary>{{ 'Other amounts' | t }}</summary><ul>
  <li><strong>{{value.assessment.unassessedExpenseTotal|currency:('AUD' | t):'symbol-narrow'}}</strong> {{ 'of expenses KuTax doesn’t calculate (kept for advice or still missing details) — listed in the pack so you or your agent can decide.' | t }}</li>
  <li><strong>{{value.assessment.candidateTotal|currency:('AUD' | t):'symbol-narrow'}}</strong> {{ 'calculated but not approved yet — not included.' | t }}</li>
  <li><strong>{{value.assessment.excludedExpenseTotal|currency:('AUD' | t):'symbol-narrow'}}</strong> {{ 'marked private or excluded.' | t }}</li>
  @if(value.counts;as c){<li>{{ '{n} receipts and attachments go into the ZIP.' | t: { n: c.evidenceFiles } }}</li>}
 </ul></details>
 @for(blocker of value.blockers;track blocker){<p class="notice notice--error" role="alert">{{blocker | t}}</p>}@for(warning of value.warnings;track warning){<p class="notice notice--warning">{{warning | t}}</p>}
 <label class="check"><input id="pack-approve" type="checkbox" [(ngModel)]="confirm"><span>{{ 'I’ve checked these figures and exceptions. I’ll reconcile anything prefilled in myTax before entering amounts.' | t }}</span></label>
 <details class="small"><summary>{{ 'Add a note (optional)' | t }}</summary><label class="field">{{ 'Note' | t }}<textarea [(ngModel)]="note" maxlength="2000" [placeholder]="'For example: checked against my payslips' | t"></textarea></label></details>
 <button class="btn btn--primary btn--block" type="button" (click)="prepare()" [disabled]="busy()||value.blockers.length>0" [attr.aria-busy]="busy()">{{ 'Prepare tax package' | t }}</button>
 <p class="muted small">{{ 'Changes after this need a new tax pack. Earlier tax packs stay as they were.' | t }}</p>
</section>}
@if(selected();as item){<section class="card section" aria-labelledby="package-title">
 <div class="section-heading"><div><span class="kicker">{{ 'Tax pack prepared' | t }}</span><h2 id="package-title">{{item.approvedAt|date:('d MMM yyyy, h:mm a' | t)}}</h2></div><span class="chip" [class.chip--done]="item.status==='Complete'" [class.chip--info]="item.status==='Queued'||item.status==='Processing'" [class.chip--review]="item.status==='Failed'">{{item.status | t}}</span></div>
 @if(item.model.approvalNote){<p class="small">“{{item.model.approvalNote}}”</p>}
 @if(item.status==='Queued'||item.status==='Processing'){<p class="notice" role="status">{{ 'Your package is being prepared securely. You can leave this page; it keeps going on the server.' | t }}</p>}
 @if(item.status==='Failed'){<p class="notice notice--error" role="alert">{{ 'The tax pack couldn’t be prepared ({code}). Try again.' | t: { code: item.errorCode } }}</p><button class="btn" type="button" (click)="retry()" [disabled]="busy()">{{ 'Retry package' | t }}</button>}
 <div class="assistant">
  <span class="kicker">{{ 'myTax entry assistant' | t }}</span>
  @for(part of sections(item);track part.paperReference){<div class="part">
   <p class="small"><strong>{{part.section}}</strong> · {{ 'paper reference' | t }} {{part.paperReference}}@if(part.claimType){ · {{ 'claim type' | t }} <strong>{{part.claimType}}</strong>}</p>
   <div class="entry"><strong class="entry-amount" [attr.aria-label]="'Whole-dollar amount for '+part.section">{{part.entryAmount.toFixed(0)}}</strong><button class="btn btn--primary" type="button" (click)="copyAmount(part)">{{ 'Copy' | t }}<span class="sr-only"> {{part.section}}</span></button>
    <label class="field progress"><span class="sr-only">{{part.section}} </span>{{ 'Entry progress' | t }}<select [ngModel]="progressOf(item,part)" (ngModelChange)="progress($event,part)" [disabled]="busy()"><option value="NotStarted">{{ 'Not started' | t }}</option><option value="Entered">{{ 'Entered' | t }}</option><option value="Checked">{{ 'Checked' | t }}</option></select></label></div>
  </div>}
  <p class="small muted">{{ 'Exact approved deductions {exact}. Each section drops its cents once after adding up: {cents} in total.' | t: { exact: (item.model.assessment.approvedTotal|currency:'AUD'), cents: ((item.model.assessment.approvedTotal-item.model.assessment.entryAmount)|currency:'AUD') } }}</p>
  <p class="small">{{ 'Check the official myTax screens for this year. Reconcile anything prefilled, other expenses in the same section and duplicate records before entering your final amounts.' | t }}</p>
  <div class="row"><a class="btn" href="https://www.ato.gov.au/online-services" target="_blank" rel="noopener">{{ 'ATO online services' | t }}<span class="sr-only"> {{ '(opens in a new tab)' | t }}</span></a></div>
  <p class="small muted">{{ 'Preparation only. KuTax doesn’t access your myGov account or lodge your return.' | t }} <a [routerLink]="['/history', item.id]">{{ 'Open full details' | t }}</a></p>
 </div>
 @if(item.status==='Complete'){<h3>{{ 'Your files' | t }}</h3><app-pack-files [packId]="item.id" [files]="item.files" />
  <p class="small muted">{{ 'Only approved claims count towards the amounts above. The spreadsheet also lists anything left out, so nothing gets lost.' | t }}</p>}
</section>}
@if(history()[0];as latest){<p class="earlier"><app-icon name="archive" [size]="18" /><span>{{ (history().length===1?'{n} tax pack prepared for this year':'{n} tax packs prepared for this year') | t: { n: history().length } }} · {{ 'latest {date}' | t: { date: (latest.approvedAt|date:('d MMM yyyy, h:mm a' | t)) } }}</span>
 @if(selected()?.id!==latest.id){<button class="btn btn--small" type="button" (click)="open(latest.id)" [disabled]="busy()">{{ 'Open latest' | t }}</button>}
 <a class="btn btn--small btn--quiet" routerLink="/history" [queryParams]="{ year: year() }">{{ 'All in History' | t }}<app-icon name="arrow" [size]="16" /></a></p>}
`,styles:[`
:host{display:block;scroll-margin-top:96px}
.strip{align-items:center;background:var(--mint);border:2px solid var(--ink);border-radius:20px;box-shadow:5px 5px 0 var(--ink);display:flex;gap:14px;margin-top:24px;padding:18px}
.strip-icon{align-items:center;background:var(--white);border:1px solid var(--ink);border-radius:12px;display:flex;flex:none;height:46px;justify-content:center;width:46px}
.strip-copy{flex:1}.strip-copy strong,.strip-copy small{display:block}.strip-copy small{font-size:13px;margin-top:3px}
.lead{background:var(--mint-soft);border-radius:16px;margin:12px 0;padding:14px 16px}.lead small{display:block}.lead strong{display:block;font-size:30px}.lead p{margin:4px 0 0}.sub{font-size:16px;margin:16px 0 4px}.entries{list-style:none;margin:0 0 8px;padding:0}.entries li{align-items:center;border-top:1px solid var(--line);display:flex;gap:12px;justify-content:space-between;padding:10px 0}.entries strong,.entries small{display:block}.entries small{color:var(--ink-soft);font-size:12px}.dollars{font-size:18px;font-variant-numeric:tabular-nums}.more{margin-bottom:12px}.more ul{margin:6px 0;padding-left:18px}.figures{display:grid;gap:10px;grid-template-columns:repeat(4,1fr);margin:16px 0}.figures div{background:#fbfaf6;border:1px solid var(--line);border-radius:14px;padding:12px}.figures small,.figures strong{display:block}.figures small{color:var(--ink-soft);font-size:12px}.figures strong{font-size:17px;margin-top:2px}
.assistant{background:var(--purple-soft);border-radius:18px;margin:16px 0;padding:18px}.assistant p{margin-bottom:8px}
.entry{align-items:center;display:flex;flex-wrap:wrap;gap:14px;margin:6px 0 10px}.entry-amount{font-family:var(--mono);font-size:44px;letter-spacing:-.02em}
.earlier{align-items:center;display:flex;flex-wrap:wrap;font-size:13px;font-weight:700;gap:8px 10px;margin:12px 4px 0}.earlier span{flex:1;min-width:200px}.part{border-top:1px solid rgba(0,0,0,.08);padding-top:10px}.progress{margin:0;min-width:180px}.progress select{background:var(--white)}


@media(max-width:700px){.strip{align-items:stretch;flex-wrap:wrap}.strip .btn{width:100%}.figures{grid-template-columns:1fr 1fr}.entry .btn{flex:1}.progress{flex:1}}
`]})
export class TaxExports implements OnDestroy {
 readonly year=input.required<number>();private readonly http=inject(HttpClient);readonly preview=signal<Preview|null>(null);readonly history=signal<History[]>([]);readonly selected=signal<Package|null>(null);readonly busy=signal(false);readonly message=signal('');note='';confirm=false;acknowledge=false;private timer:ReturnType<typeof setTimeout>|undefined;private revision=0;
 constructor(){effect(()=>{this.year();this.revision++;this.preview.set(null);this.selected.set(null);this.confirm=false;this.acknowledge=false;clearTimeout(this.timer);void this.loadHistory();});}
 ngOnDestroy(){this.revision++;clearTimeout(this.timer);}
 async loadHistory(){const year=this.year();try{const items=await firstValueFrom(this.http.get<History[]>(`/api/tax-years/${year}/exports`));if(year===this.year())this.history.set(items);}catch{this.message.set('Could not load export history.');}}
 async validate(){this.busy.set(true);this.preview.set(null);this.confirm=false;this.acknowledge=false;const year=this.year();try{const preview=await firstValueFrom(this.http.get<Preview>(`/api/tax-years/${year}/export-preview`));if(year===this.year()){this.preview.set(preview);this.message.set('Validation complete. Review these figures and exceptions.');}}catch{this.message.set('Could not validate the export. Refresh and retry.');}finally{this.busy.set(false);}}
 async prepare(){const preview=this.preview();if(!preview||this.busy())return;if(!this.confirm){showAttention('pack-approve','Tick this to freeze these figures into your tax pack.');return;}this.busy.set(true);try{const result=await firstValueFrom(this.http.post<{id:string}>(`/api/tax-years/${this.year()}/exports`,{previewHash:preview.hash,confirm:true,acknowledgeExceptions:true,note:this.note.trim()||null}));this.preview.set(null);this.confirm=false;this.acknowledge=false;await this.open(result.id);await this.loadHistory();this.message.set('Preparing your tax pack…');}catch(error){this.message.set(error instanceof HttpErrorResponse?error.error?.detail??'Could not prepare the package. Validate again.':'Could not prepare the package.');}finally{this.busy.set(false);}}
 async open(id:string){clearTimeout(this.timer);const revision=++this.revision;try{const item=await firstValueFrom(this.http.get<Package>(`/api/tax-exports/${id}`));if(revision!==this.revision)return;this.selected.set(item);if(item.status==='Queued'||item.status==='Processing')this.timer=setTimeout(()=>void this.open(id),2000);else await this.loadHistory();}catch{this.message.set('Could not load this package.');}}
 async retry(){const item=this.selected();if(!item)return;this.busy.set(true);try{await firstValueFrom(this.http.post(`/api/tax-exports/${item.id}/retry`,{}));await this.open(item.id);}catch{this.message.set('Could not retry this package.');}finally{this.busy.set(false);}}
 sections(item:Package){return sectionsOf(item.model.assessment);}
 progressOf(item:Package,part:MyTaxSection){return progressFor(item.progress,part.section);}
 async copyAmount(part:MyTaxSection){try{await navigator.clipboard.writeText(part.entryAmount.toFixed(0));this.message.set(`${part.section}: whole-dollar amount copied. Reconcile prefill before entering it.`);}catch{this.message.set('Clipboard unavailable. Copy the displayed whole-dollar amount manually.');}}
 async progress(status:string,part:MyTaxSection){const item=this.selected();if(!item)return;this.busy.set(true);try{await firstValueFrom(this.http.put(`/api/tax-exports/${item.id}/entry-progress`,{status,section:item.model.assessment.sections?part.section:null}));await this.open(item.id);this.message.set('Entry progress saved.');}catch{this.message.set('Could not save entry progress.');}finally{this.busy.set(false);}}
}
