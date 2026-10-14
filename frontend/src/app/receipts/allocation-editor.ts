import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Icon } from '../core/icon';
import { claimTypes } from '../core/categories';
import { showAttention } from '../core/attention';
import { TranslatePipe } from '../core/i18n';
import { Toasts } from '../core/toast';
interface Allocation { description:string;category:string;claimType:string|null;amount:number;workUsePercent:number;workReimbursement:number;basis:string;eligibilityConfirmed:boolean;status?:string; }
interface Rule { category:string;label:string;section:string;paperReference:string;declaration:string;conditions:string;claimTypes:string[];wholeAmountOnly:boolean;financialYearStart:number; }
/** True for amounts with at most two decimal places. Compared with a tolerance: 71.9 × 100 is 7190.000000000001 in floating point. */
export function wholeCents(amount:number){return Math.abs(amount*100-Math.round(amount*100))<1e-6;}

@Component({selector:'app-allocation-editor',imports:[TranslatePipe, CurrencyPipe,FormsModule,Icon],template:`
 <div class="section-heading"><div><span class="kicker">{{ 'Step 2 · Split into expenses' | t }}</span><h3>{{ 'Expenses on this receipt' | t }}</h3></div>
  <span class="chip" [class.chip--review]="dirty()||remaining()!==0" [class.chip--done]="!dirty()&&remaining()===0">{{dirty()?('Unsaved changes' | t):remaining()===0?('Fully allocated' | t):(remaining()|currency:('AUD' | t):'symbol-narrow')+(' unallocated' | t)}}</span></div>
 <p class="muted small">{{ 'Choose what kind of expense this is. Split it only if one receipt covers different things. Any unallocated balance stays visible as an exception in your annual summary.' | t }}</p>
 <details class="scope"><summary>{{ 'What KuTax can calculate' | t }}</summary><ul class="small">@for(rule of rules();track rule.category){<li><strong>{{rule.label | t}}</strong> — {{rule.section}} ({{rule.paperReference}})</li>}</ul><p class="small">{{ 'Anything else — such as car, travel, working from home, or items over $300 — goes under “needs advice”: kept with your records, without a calculated deduction.' | t }}</p></details>
 @if(message()){<p class="notice" [class.notice--error]="failed()" [class.notice--success]="!failed()" [attr.role]="failed()?'alert':'status'">{{message() | t}}</p>}
 @for(row of rows();track $index;let i=$index){<fieldset class="allocation" [disabled]="busy()"><legend>{{ 'Expense {n}' | t: { n: i+1 } }} <span class="chip">{{(row.status??'Unsaved') | t}}</span></legend>
 <label class="field">{{ 'Category' | t }}<select [id]="'a'+i+'-category'" [(ngModel)]="row.category" (ngModelChange)="categoryChanged(row)">@for(rule of rules();track rule.category){<option [value]="rule.category">{{rule.label | t}}</option>}<option value="Unsupported">{{ 'Other work expense (needs advice)' | t }}</option><option value="Excluded">{{ 'Excluded / private expense' | t }}</option></select></label>
 @if(ruleFor(row.category);as rule){@if(rule.claimTypes.length){<label class="field">{{ 'Claim type' | t }}<select [id]="'a'+i+'-claimType'" [(ngModel)]="row.claimType" (ngModelChange)="touch()"><option [ngValue]="null" disabled>{{ 'Choose…' | t }}</option>@for(code of rule.claimTypes;track code){<option [ngValue]="code">{{claimLabel(code) | t}}</option>}</select></label>}}
 <label class="field">{{ 'Description' | t }}<input [id]="'a'+i+'-description'" [(ngModel)]="row.description" (ngModelChange)="touch()" maxlength="200"></label>
 <div class="numbers"><label class="field">{{ 'Amount (AUD)' | t }}<input [id]="'a'+i+'-amount'" type="number" inputmode="decimal" min="0.01" step="0.01" [(ngModel)]="row.amount" (ngModelChange)="touch()"></label>
  @if(!ruleFor(row.category)?.wholeAmountOnly){<label class="field">{{ 'Work use %' | t }}<input [id]="'a'+i+'-workUse'" type="number" inputmode="decimal" min="0" max="100" step="0.01" [(ngModel)]="row.workUsePercent" (ngModelChange)="touch()"></label><label class="field">{{ 'Reimbursed (AUD)' | t }}<input [id]="'a'+i+'-reimbursed'" type="number" inputmode="decimal" min="0" step="0.01" [(ngModel)]="row.workReimbursement" (ngModelChange)="touch()"></label>}</div>
 @if(ruleFor(row.category);as rule){<label class="check"><input type="checkbox" [(ngModel)]="row.eligibilityConfirmed" (ngModelChange)="touch()"><span>{{rule.declaration | t}}</span></label>
  @if(!row.eligibilityConfirmed){<p class="small hint-warn">{{ 'Tick this if it’s true. Without it the expense is saved but waits as “needs details” instead of being calculated.' | t }}</p>}}
 <label class="field">{{ 'Notes (optional)' | t }}<textarea [(ngModel)]="row.basis" (ngModelChange)="touch()" maxlength="2000" [placeholder]="'Anything worth recording, such as how you worked out the work-use percentage' | t"></textarea></label>
 <button class="btn btn--quiet btn--small btn--danger" type="button" (click)="remove(i)">{{ 'Remove expense {n}' | t: { n: i+1 } }}</button></fieldset>}
 <div class="row actions"><button class="btn" id="add-expense" type="button" (click)="add()" [disabled]="busy()||rows().length>=50"><app-icon name="plus" [size]="18" />{{ 'Add expense' | t }}</button><button class="btn btn--primary" type="button" (click)="save()" [disabled]="busy()" [attr.aria-busy]="busy()">{{ 'Save expenses' | t }}</button></div>
 <p class="muted small">{{ 'Saving removes earlier claim approvals. Approve eligible claims in your tax summary after reviewing their rules.' | t }}</p>`,
 styles:[`.scope{margin-bottom:8px}.scope ul{margin:6px 0;padding-left:18px}.allocation{border:1px solid var(--line);border-radius:16px;margin:14px 0;padding:16px}legend{align-items:center;display:flex;font-weight:800;gap:8px;padding:0 6px}.numbers{display:grid;gap:0 12px;grid-template-columns:repeat(3,minmax(0,1fr))}.actions{margin-top:8px}.hint-warn{color:var(--orange-ink);margin:-4px 0 12px}@media(max-width:560px){.numbers{grid-template-columns:1fr 1fr}.numbers .field:first-child{grid-column:1/-1}.actions .btn{flex:1}}`]})
export class AllocationEditor {
 readonly transactionId=input.required<string>();readonly version=input.required<string>();readonly amount=input.required<number>();readonly merchant=input<string>('');readonly suggested=input<string|null>(null);readonly saved=output<void>();
 private readonly http=inject(HttpClient);private readonly toasts=inject(Toasts);readonly rows=signal<Allocation[]>([]);readonly busy=signal(false);readonly message=signal('');readonly failed=signal(false);
 /** True once anything is edited after the last load or save, so "Fully allocated" only describes saved allocations. */
 readonly dirty=signal(false);private readonly revision=signal(0);
 /** One definition per category (the newest year's wording; categories are the same across supported years). */
 readonly rules=signal<Rule[]>([]);
 readonly remaining=computed(()=>{this.revision();return Math.round((this.amount()-this.rows().reduce((sum,row)=>sum+Number(row.amount||0),0))*100)/100;});
 private readonly rulesReady=this.loadRules();
 constructor(){effect(()=>{this.transactionId();this.version();void this.load();});}
 ruleFor(category:string){return this.rules().find(r=>r.category===category)??null;}
 claimLabel(code:string){return claimTypes[code]??code;}
 touch(){this.dirty.set(true);this.revision.update(v=>v+1);}
 categoryChanged(row:Allocation){const rule=this.ruleFor(row.category);if(!rule?.claimTypes.includes(row.claimType??''))row.claimType=null;if(rule?.wholeAmountOnly){row.workUsePercent=100;row.workReimbursement=0;}row.eligibilityConfirmed=false;this.touch();}
 async loadRules(){try{const result=await firstValueFrom(this.http.get<{rules:Rule[]}>('/api/tax-rules'));const latest=new Map<string,Rule>();for(const rule of [...result.rules].sort((a,b)=>a.financialYearStart-b.financialYearStart))latest.set(rule.category,rule);this.rules.set([...latest.values()]);}catch{/* Categories still save; the server validates them. */}}
 async load(){try{const rows=await firstValueFrom(this.http.get<Allocation[]>(`/api/transactions/${this.transactionId()}/allocations`));this.rows.set(rows);this.dirty.set(false);this.revision.update(v=>v+1);if(!rows.length)await this.prefill();}catch{this.message.set('Could not load the expenses.');}}
 /** A new receipt starts with one expense already filled in: the whole amount, the merchant, and the category and work use KuTax expects. */
 async prefill(){const id=this.transactionId();await this.rulesReady;if(id!==this.transactionId()||this.rows().length)return;this.add();this.dirty.set(false);
  try{const p=await firstValueFrom(this.http.post<{category:string|null;workUsePercent:number|null;claimType:string|null}>(`/api/transactions/${id}/prefill`,{}));
   const row=this.rows()[0];if(id!==this.transactionId()||!row||this.dirty())return;
   const category=p.category&&(this.ruleFor(p.category)||p.category==='Unsupported'||p.category==='Excluded')?p.category:row.category;
   const rule=this.ruleFor(category);
   this.rows.set([{...row,category,claimType:rule?.claimTypes.includes(p.claimType??'')?p.claimType:rule?.claimTypes.length===1?rule.claimTypes[0]:null,workUsePercent:rule?.wholeAmountOnly?100:p.workUsePercent??row.workUsePercent}]);this.revision.update(v=>v+1);}
  catch{/* the person can still choose */}}
 add(){this.rows.update(rows=>[...rows,{description:rows.length?'':this.merchant(),category:!rows.length&&this.suggested()&&this.ruleFor(this.suggested()!)?this.suggested()!:'Unsupported',claimType:null,amount:Math.max(0,Math.round((this.amount()-rows.reduce((sum,row)=>sum+Number(row.amount),0))*100)/100),workUsePercent:100,workReimbursement:0,basis:'',eligibilityConfirmed:false}]);this.touch();}
 remove(index:number){this.rows.update(rows=>rows.filter((_,i)=>i!==index));this.touch();}
 /** The first thing the server would refuse, with the field to point at, so nothing is left to guessing. */
 problem():{field:string;message:string}|null{const rows=this.rows();if(!rows.length)return {field:'add-expense',message:'Add at least one expense.'};
  for(const [i,row] of rows.entries()){const at=rows.length===1?'':`Expense ${i+1}: `;const rule=this.ruleFor(row.category);const f=(name:string,message:string)=>({field:`a${i}-${name}`,message:at+message});
   if(!row.description?.trim())return f('description','Add a short description.');
   if(rule?.claimTypes.length&&!row.claimType)return f('claimType','Choose the claim type.');
   const amount=Number(row.amount),use=Number(row.workUsePercent),paid=Number(row.workReimbursement||0);
   if(!(amount>0)||!wholeCents(amount))return f('amount','Enter an amount above $0 with at most two decimal places.');
   if(!(use>=0&&use<=100))return f('workUse','Work use must be between 0 and 100%.');
   if(paid<0)return f('reimbursed','The reimbursed amount can’t be negative.');
   const work=Math.round(amount*use)/100;if(paid>work)return f('reimbursed',`The reimbursement can’t be more than the work portion ($${work.toFixed(2)}).`);}
  const total=rows.reduce((sum,row)=>sum+Number(row.amount||0),0);
  if(Math.round(total*100)>Math.round(this.amount()*100))return {field:`a${rows.length-1}-amount`,message:`The expenses add up to $${total.toFixed(2)}, more than the receipt total of $${this.amount().toFixed(2)}.`};
  return null;}
 async save(){const problem=this.problem();if(problem){this.message.set('');if(!showAttention(problem.field,problem.message)){this.failed.set(true);this.message.set(problem.message);}return;}this.busy.set(true);this.failed.set(false);try{await firstValueFrom(this.http.put(`/api/transactions/${this.transactionId()}/allocations`,{transactionVersion:this.version(),items:this.rows()}));this.dirty.set(false);this.message.set('Saved. Approve eligible claims in your tax summary, or keep going with the next receipt.');this.toasts.show({text:'Expenses saved.'});this.saved.emit();}catch(error){this.failed.set(true);this.message.set(error instanceof HttpErrorResponse?error.error?.detail??'Could not save. Reload the receipt and try again.':'Could not save.');}finally{this.busy.set(false);}}
}
