import { Component, inject, OnInit, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Icon } from '../core/icon';
import { Session } from '../core/session';
import { TranslatePipe } from '../core/i18n';
interface Privacy {externalAiConsent:boolean;noticeVersion:string;configured:boolean;provider:string;model:string|null;textModel:string|null;region:string|null;retentionNotice:string|null;privacyUrl:string;}
@Component({selector:'app-ai-privacy',imports:[TranslatePipe, FormsModule,Icon],template:`<section class="card section" aria-labelledby="privacy-title">
 <span class="kicker">{{ 'Privacy' | t }}</span><h2 id="privacy-title">{{ 'Receipt privacy and AI' | t }}</h2>
 <p class="muted">{{ 'Original receipts are private. Manual review works without sending anything to an AI provider.' | t }}</p>
 @if(message()){<p class="notice" role="status">{{ message() | t }}</p>}
 @if(data();as notice){
  <dl class="facts"><div><dt>{{ 'Provider' | t }}</dt><dd>{{notice.configured?notice.provider:('Off' | t)}}</dd></div><div><dt>{{notice.textModel?('Photo model' | t):('Model' | t)}}</dt><dd>{{notice.model||('Not configured' | t)}}</dd></div>@if(notice.textModel){<div><dt>{{ 'PDF text model' | t }}</dt><dd>{{notice.textModel}}</dd></div>}<div><dt>{{ 'Processing region' | t }}</dt><dd>{{notice.region??('Not configured' | t)}}</dd></div></dl>
  <p class="small">{{notice.retentionNotice??('The operator must review processing location, retention and training terms before external AI can be enabled.' | t)}}</p>
  <p class="small"><a [href]="notice.privacyUrl" target="_blank" rel="noopener">{{ 'Read the provider’s data controls' | t }}<span class="sr-only"> {{ '(opens in a new tab)' | t }}</span></a></p>
  @if(!notice.configured){<p class="notice notice--warning">{{ 'AI reading isn’t set up here, so you enter receipt details yourself.' | t }}</p>}
  <label class="check"><input type="checkbox" [(ngModel)]="consent" [disabled]="busy()||!notice.configured&&!notice.externalAiConsent"><span>{{ 'I consent to sending receipt content to this configured provider for extraction under the region and retention notice above.' | t }}</span></label>
  <p class="muted small">{{ 'Changing the provider, model, region or retention needs fresh consent. Revoking stops future requests; one already sent may finish. AI suggestions always need your review.' | t }}</p>
  <div class="row"><button class="btn btn--primary" type="button" (click)="save()" [disabled]="busy()">{{ 'Save AI preference' | t }}</button><span class="muted small" [title]="notice.noticeVersion">{{ 'Notice {id}' | t: { id: notice.noticeVersion.slice(-8) } }}</span></div>
 }
 <details><summary><app-icon name="shield" [size]="16" /> {{ 'Storage and recovery' | t }}</summary><p class="small">{{ 'Receipt originals and exports are held in private storage and served only through your signed-in account. Files stay blocked until malware scanning clears them. Your operator manages encrypted disks, retention and protected backups; ask them before importing real personal records.' | t }}</p></details>
</section>`,styles:[`.facts{display:grid;gap:10px;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));margin:16px 0}.facts div{background:#fbfaf6;border:1px solid var(--line);border-radius:14px;padding:10px 12px}dt{color:var(--ink-soft);font-size:12px}dd{font-weight:800;margin:2px 0 0;overflow-wrap:anywhere}details{border-top:1px solid var(--line);margin-top:16px;padding-top:4px}summary{align-items:center;display:flex;gap:6px}@media(max-width:700px){.facts{grid-template-columns:1fr}}`]})
export class AiPrivacy implements OnInit {
 private readonly http=inject(HttpClient);private readonly session=inject(Session);readonly data=signal<Privacy|null>(null);readonly busy=signal(false);readonly message=signal('');consent=false;
 ngOnInit(){void this.load();}
 async load(){try{const value=await firstValueFrom(this.http.get<Privacy>('/api/privacy'));this.data.set(value);this.consent=value.externalAiConsent;this.session.ai.set({configured:value.configured,consent:value.externalAiConsent});}catch{this.message.set('Could not load privacy settings.');}}
 async save(){const value=this.data();if(!value||this.busy())return;this.busy.set(true);try{await firstValueFrom(this.http.put('/api/privacy',{externalAiConsent:this.consent,noticeVersion:value.noticeVersion}));await this.load();this.message.set('AI privacy preference saved.');}catch(error){this.message.set(error instanceof HttpErrorResponse?error.error?.detail??'Could not save. Reload and check the current notice.':'Could not save privacy settings.');}finally{this.busy.set(false);}}
}
