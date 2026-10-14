import { Component, computed, inject, output, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Icon } from '../core/icon';
import { MachineArt } from '../core/machine-art';
import { TranslatePipe } from '../core/i18n';
import { Session } from '../core/session';

interface UploadRow { file: File; path: string; id: string; state: string; progress: number; }
@Component({
  selector: 'app-receipt-inbox',
  imports: [TranslatePipe, Icon, MachineArt, RouterLink],
  template: `
    <section class="board" [class.is-dragging]="dragging()" aria-labelledby="inbox-title"
      (dragenter)="dragOver($event)" (dragover)="dragOver($event)" (dragleave)="dragging.set(false)" (drop)="drop($event)">
      <span class="board-label"><span class="live-dot"></span>{{ 'Receipt inbox' | t }}</span>
      <app-machine-art class="art" />
      <div class="copy">
        @if (summary(); as done) {
          <span class="success" aria-hidden="true"><app-icon name="check" [size]="28" /></span>
          <h2 id="inbox-title">{{ (done.stored === 1 ? '{n} file landed safely' : '{n} files landed safely') | t: { n: done.stored } }}</h2>
          <p class="muted">{{ message() | t }}</p>
          <button class="btn" type="button" (click)="reset()">{{ 'Add more' | t }}</button>
        } @else {
          <span class="eyebrow">{{ 'Drop it. Done.' | t }}</span>
          <h2 id="inbox-title">{{ 'Send your tax stuff this way' | t }}</h2>
          <p class="muted">{{ 'Add receipts, invoices or a whole folder. KuTax checks each file privately, then reads the details for you to confirm.' | t }}</p>
          <div class="actions">
            <label class="btn btn--primary file-btn"><app-icon name="plus" [size]="18" />{{ 'Browse files' | t }}
              <input type="file" multiple accept="application/pdf,image/jpeg,image/png" (change)="choose($event, 'Files')" [disabled]="busy()" /></label>
            <label class="btn file-btn"><app-icon name="camera" [size]="18" />{{ 'Take a photo' | t }}
              <input type="file" accept="image/jpeg,image/png" capture="environment" (change)="choose($event, 'Camera')" [disabled]="busy()" /></label>
            <label class="btn file-btn folder"><app-icon name="folder" [size]="18" />{{ 'Import folder' | t }}
              <input type="file" multiple webkitdirectory (change)="choose($event, 'Folder')" [disabled]="busy()" /></label>
          </div>
          <p class="hint">{{ 'or drag files and folders anywhere here · folder paths are kept' | t }}</p>
          <div class="types"><span>{{ 'PDF' | t }}</span><span>{{ 'JPG' | t }}</span><span>{{ 'PNG' | t }}</span><span>{{ '≤ 20 MB' | t }}</span></div>
        }
      </div>
      <span class="privacy"><app-icon name="shield" [size]="14" /> {{ 'Private storage' | t }}</span>
    </section>

    @if (message() && !summary()) { <p class="notice" role="status">{{ message() | t }}</p> }
    @if (rows().length && !summary()) {
      <section class="card queue" aria-labelledby="queue-title">
        <div class="section-heading">
          <div><span class="kicker">{{ 'Ready to import' | t }}</span><h3 id="queue-title">{{ rows().length }} {{ rows().length === 1 ? 'file' : 'files' }}</h3></div>
          <div class="row">
            @if (busy()) { <button class="btn btn--quiet" type="button" (click)="cancel()">{{ 'Cancel remaining' | t }}</button> }
            @else { <button class="btn btn--quiet" type="button" (click)="reset()">{{ 'Clear' | t }}</button> }
            <button class="btn btn--primary" type="button" (click)="upload()" [disabled]="busy() || !canUpload()">
              <app-icon name="upload" [size]="18" />{{ batchId ? ('Retry failed files' | t) : ('Start import' | t) }}</button>
          </div>
        </div>
        @if (askAi(); as ai) {
          <div class="ai-ask" role="group" aria-labelledby="ai-ask-title">
            <app-icon name="spark" />
            <div>
              <strong id="ai-ask-title">{{ 'Let AI read these receipts for you?' | t }}</strong>
              <p class="small">{{ 'AI fills in the merchant, date and amount, and you just check them. The receipts are sent to {provider} to be read.' | t: { provider: ai.provider } }}
                <a routerLink="/settings" [queryParams]="{ tab: 'ai' }">{{ 'Learn more' | t }}</a></p>
              <div class="row">
                <button class="btn btn--primary btn--small" type="button" (click)="answerAi(true)" [disabled]="busy()">{{ 'Yes, use AI' | t }}</button>
                <button class="btn btn--quiet btn--small" type="button" (click)="answerAi(false)" [disabled]="busy()">{{ 'I’ll type them myself' | t }}</button>
              </div>
            </div>
          </div>
        }
        <ul class="files" aria-live="polite">
          @for (row of rows(); track row.id) {
            <li>
              <span class="path">{{ row.path }}<small>{{ (row.file.size / 1024).toFixed(0) }} KB</small></span>
              <span class="chip" [class]="tone(row.state)">{{ row.state | t }}@if (row.state === 'Uploading') { {{ row.progress }}% }</span>
              @if (row.state === 'Uploading') { <span class="bar" aria-hidden="true"><span [style.width.%]="row.progress"></span></span> }
            </li>
          }
        </ul>
      </section>
    }
  `,
  styles: `
    .ai-ask { align-items: flex-start; background: var(--yellow-soft); border: 2px solid var(--ink); border-radius: 14px; display: flex; gap: 12px; margin-bottom: 12px; padding: 12px 14px; }
    .ai-ask p { margin: 4px 0 8px; }
    :host { display: block; }
    .board { align-items: center; background: radial-gradient(circle at 20% 50%, rgba(139,215,238,.25), transparent 26%),
      radial-gradient(circle at 84% 46%, rgba(243,206,88,.16), transparent 28%), var(--purple-soft);
      border: 2px solid var(--ink); border-radius: 30px; box-shadow: 8px 8px 0 var(--ink); display: grid;
      grid-template-columns: 46% 54%; min-height: 400px; overflow: hidden; padding: 56px 6% 52px; position: relative;
      transition: transform 180ms ease, background 180ms ease; }
    .board.is-dragging { background: var(--mint-soft); transform: translateY(-4px); }
    .board-label, .privacy { align-items: center; background: rgba(255,255,255,.7); border: 1px solid rgba(52,42,92,.2);
      border-radius: 20px; display: flex; font-family: var(--mono); font-size: 11px; gap: 7px; letter-spacing: .08em;
      padding: 7px 10px; position: absolute; text-transform: uppercase; }
    .board-label { left: 20px; top: 18px; }
    .privacy { bottom: 18px; right: 20px; }
    .live-dot { background: var(--mint); border: 1px solid var(--ink); border-radius: 50%; box-shadow: 0 0 0 3px rgba(141,221,191,.35); height: 8px; width: 8px; }
    .copy { padding-left: 8%; position: relative; z-index: 2; }
    h2 { font-size: clamp(28px, 3vw, 42px); letter-spacing: -.045em; line-height: 1.08; margin: 10px 0 14px; }
    .actions { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 20px; }
    .hint { color: var(--ink-soft); font-size: 13px; margin: 12px 0 0; }
    .types { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 16px; }
    .types span { background: rgba(255,255,255,.6); border: 1px solid rgba(52,42,92,.25); border-radius: 6px; font-family: var(--mono); font-size: 11px; padding: 4px 7px; }
    .success { align-items: center; background: var(--mint); border: 2px solid var(--ink); border-radius: 50%; box-shadow: 3px 3px 0 var(--ink);
      display: flex; height: 52px; justify-content: center; width: 52px; }
    .queue { margin-top: 20px; }
    .files { display: grid; gap: 8px; list-style: none; margin: 0; max-height: 420px; overflow: auto; padding: 0; }
    .files li { align-items: center; border-bottom: 1px solid var(--line); display: grid; gap: 4px 12px; grid-template-columns: minmax(0,1fr) auto; padding: 8px 2px 10px; }
    .path { font-size: 14px; font-weight: 700; overflow-wrap: anywhere; }
    .path small { color: var(--ink-soft); display: block; font-weight: 400; margin-top: 2px; }
    .bar { background: #efede7; border-radius: 6px; grid-column: 1 / -1; height: 6px; overflow: hidden; }
    .bar span { background: var(--purple); display: block; height: 100%; transition: width 200ms ease; }
    @media (max-width: 960px) { .board { grid-template-columns: 1fr 1.2fr; padding: 56px 3% 52px; } }
    @media (max-width: 700px) {
      .board { border-radius: 24px; box-shadow: 5px 5px 0 var(--ink); display: flex; flex-direction: column; min-height: 0; padding: 52px 16px 56px; }
      .art { width: 100%; }
      .copy { padding: 0; text-align: center; }
      .actions { flex-direction: column; }
      .actions .btn { width: 100%; }
      .types { justify-content: center; }
      .success { margin: 0 auto; }
    }
    @media (pointer: coarse) and (max-width: 700px) { .folder { display: none; } }
  `,
})
export class ReceiptInbox {
  private readonly http = inject(HttpClient);
  private readonly session = inject(Session);
  /** The one-time AI question, while it's waiting for an answer. */
  readonly askAi = signal<{ provider: string; noticeVersion: string } | null>(null);
  readonly imported = output<void>();
  readonly rows = signal<UploadRow[]>([]);
  readonly busy = signal(false);
  readonly dragging = signal(false);
  readonly message = signal('');
  readonly finished = signal(false);
  readonly summary = computed(() => {
    if (!this.finished() || this.rows().some((row) => ['Failed', 'Cancelled', 'Ready'].includes(row.state))) return null;
    const stored = this.rows().filter((row) => row.state === 'Stored').length;
    return stored ? { stored } : null;
  });
  batchId = ''; source = 'Files'; stopped = false;
  private active = new Set<XMLHttpRequest>();
  tone(state: string) {
    return state === 'Stored' ? 'chip--done' : state === 'Uploading' || state === 'Ready' ? 'chip--info' : state === 'Duplicate' ? 'chip--purple' : 'chip--review';
  }
  dragOver(event: DragEvent) { event.preventDefault(); if (!this.busy()) this.dragging.set(true); }
  reset() { if (this.busy()) return; this.rows.set([]); this.batchId = ''; this.finished.set(false); this.message.set(''); }
  choose(event: Event, source: string) { const input = event.target as HTMLInputElement; this.select(Array.from(input.files ?? []).map(file => ({file, path: file.webkitRelativePath || file.name})), source); input.value = ''; }
  select(files: {file: File; path: string}[], source: string) {
    if (this.busy() || !files.length) return;
    if (files.length > 2000) { this.message.set('Choose at most 2,000 files.'); return; }
    this.source = source; this.batchId = ''; this.finished.set(false); this.message.set('Check the files below, then start your import. Duplicates are detected securely during upload.');
    this.rows.set(files.map(({file,path}) => ({file,path,id:crypto.randomUUID(),progress:0,state:/\.(pdf|png|jpe?g)$/i.test(file.name) && file.size <= 20*1024*1024 ? 'Ready' : 'Unsupported'})));
    void this.beginUpload();
  }
  private aiAskedKey() { return 'kutax.aiAsked.' + (this.session.account()?.id ?? ''); }
  private async beginUpload() {
    let asked = false;
    try { asked = localStorage.getItem(this.aiAskedKey()) === '1'; } catch { /* ask again next time */ }
    if (this.session.aiState() === 'consent' && !asked) {
      try {
        const privacy = await firstValueFrom(this.http.get<{ provider: string | null; noticeVersion: string }>('/api/privacy'));
        this.askAi.set({ provider: privacy.provider ?? 'the AI provider', noticeVersion: privacy.noticeVersion });
        return;
      } catch { /* no question: upload and type the details */ }
    }
    if (this.canUpload()) await this.upload();
  }
  /** Either answer uploads straight away; it's never asked again (it can be changed in Settings). */
  async answerAi(yes: boolean) {
    const ai = this.askAi();
    if (!ai) return;
    this.askAi.set(null);
    try { localStorage.setItem(this.aiAskedKey(), '1'); } catch { /* fine */ }
    if (yes) {
      try {
        await firstValueFrom(this.http.put('/api/privacy', { externalAiConsent: true, noticeVersion: ai.noticeVersion }));
        await this.session.refreshAi();
      } catch { this.message.set('AI reading couldn’t be turned on. You can do it later in Settings.'); }
    }
    if (this.canUpload()) await this.upload();
  }
  async drop(event: DragEvent) {
    event.preventDefault(); this.dragging.set(false); if (this.busy()) return;
    const found: {file:File;path:string}[] = [];
    const walk = async (entry: FileSystemEntry, prefix: string): Promise<void> => {
      if (found.length > 2000) return;
      if (entry.isFile) { const file = await new Promise<File>((resolve,reject) => (entry as FileSystemFileEntry).file(resolve,reject)); found.push({file,path:prefix+file.name}); }
      else { const reader = (entry as FileSystemDirectoryEntry).createReader(); let entries:FileSystemEntry[]; do { entries = await new Promise((resolve,reject)=>reader.readEntries(resolve,reject)); for (const child of entries) await walk(child,prefix+entry.name+'/'); } while(entries.length); }
    };
    try { const entries = Array.from(event.dataTransfer?.items ?? []).map(item=>item.webkitGetAsEntry()).filter((entry):entry is FileSystemEntry => !!entry); if(entries.length) { for(const entry of entries) await walk(entry,''); this.select(found,'Folder'); } else this.select(Array.from(event.dataTransfer?.files ?? []).map(file=>({file,path:file.name})),'Files'); }
    catch { this.message.set('This browser could not read the folder. Use Import folder or Browse files.'); }
  }
  canUpload() { return this.rows().some(row => ['Ready','Failed','Cancelled'].includes(row.state)) || !this.batchId && this.rows().some(row=>row.state==='Unsupported'); }
  cancel() { this.stopped = true; for(const request of this.active) request.abort(); }
  async upload() {
    if(this.busy()) return; this.busy.set(true); this.stopped=false;
    for(const row of this.rows()) if(row.state==='Cancelled') row.state='Ready';
    try {
      if(!this.batchId) this.batchId=(await firstValueFrom(this.http.post<{id:string}>('/api/import-batches/',{sourceKind:this.source,expectedFiles:this.rows().length}))).id;
      const queue=this.rows().filter(row=>!['Stored','Duplicate'].includes(row.state));
      const worker=async()=>{ while(queue.length) { const row=queue.shift()!; if(this.stopped) row.state='Cancelled';
        if(row.state==='Unsupported'||row.state==='Cancelled') { await firstValueFrom(this.http.post(`/api/import-batches/${this.batchId}/items/${row.id}/skip`,{relativePath:row.path,status:row.state})); this.rows.update(rows=>[...rows]); continue; }
        row.state='Uploading'; this.rows.update(rows=>[...rows]);
        try { const result=await this.send(row); row.state=result.status; } catch { row.state=this.stopped?'Cancelled':'Failed'; }
        this.rows.update(rows=>[...rows]);
      }};
      await Promise.all([worker(),worker()]);
      if(!this.rows().some(row=>row.state==='Failed'||row.state==='Cancelled')) await firstValueFrom(this.http.post(`/api/import-batches/${this.batchId}/complete`,{}));
      const count=(state:string)=>this.rows().filter(row=>row.state===state).length;
      this.message.set(`${count('Stored')} stored, ${count('Duplicate')} duplicates skipped, ${count('Failed')} failed, ${count('Unsupported')} unsupported, ${count('Cancelled')} cancelled. KuTax is checking and reading them now.`);
      this.finished.set(true);
      this.imported.emit();
    } catch { this.message.set('Import interrupted. Retry failed files when the connection returns.'); }
    finally { this.busy.set(false); }
  }
  private send(row: UploadRow): Promise<{status:string}> {
    return new Promise((resolve,reject)=>{
      const request=new XMLHttpRequest(); this.active.add(request); request.open('PUT',`/api/import-batches/${this.batchId}/items/${row.id}`);
      const csrf=document.cookie.split('; ').find(value=>value.startsWith('XSRF-TOKEN='))?.slice(11);
      request.setRequestHeader('X-XSRF-TOKEN',decodeURIComponent(csrf??'')); request.setRequestHeader('X-File-Name',encodeURIComponent(row.file.name)); request.setRequestHeader('X-Relative-Path',encodeURIComponent(row.path)); request.setRequestHeader('Content-Type','application/octet-stream');
      request.upload.onprogress=event=>{if(event.lengthComputable){row.progress=Math.round(event.loaded/event.total*100);this.rows.update(rows=>[...rows]);}};
      request.onload=()=>{this.active.delete(request); if(request.status>=200&&request.status<300) resolve(JSON.parse(request.responseText)); else reject(new Error('Upload failed'));};
      request.onerror=request.onabort=()=>{this.active.delete(request);reject(new Error('Upload interrupted'));}; request.send(row.file);
    });
  }
}
