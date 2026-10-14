import { Component, inject, OnInit, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { Icon } from '../core/icon';
import { apiError } from './api-error';
import { TranslatePipe } from '../core/i18n';

interface AiSettings {
  source: 'environment' | 'database'; enabled: boolean; provider: 'Groq' | 'OpenAI'; model: string; textModel: string; region: string;
  retentionNotice: string; apiKeySet: boolean; apiKeyHint: string | null; apiKeyUnreadable: boolean; configured: boolean; endpoint: string;
  updatedAt: string | null; providers: string[];
}
// Starting points only, from Groq's documentation (checked 10 October 2026); the administrator reviews them before saving.
const groqDefaults = {
  model: 'qwen/qwen3.8-27b',
  textModel: 'openai/gpt-oss-120b',
  region: 'Groq inference fleet (US and other regions); any retained data stored in Google Cloud, United States',
  retentionNotice: 'Groq does not keep inference inputs or outputs by default. Without Zero Data Retention it may log them for up to 30 days for reliability or abuse checks. Zero Data Retention is enabled for this workspace.',
};

/** Administrator settings for the workspace AI provider. The API key is write-only. */
@Component({
  selector: 'app-ai-settings',
  imports: [TranslatePipe, DatePipe, FormsModule, Icon],
  template: `
    <section class="card section" aria-labelledby="ai-settings-title">
      <span class="kicker">{{ 'Administrator' | t }}</span>
      <h2 id="ai-settings-title">{{ 'AI provider' | t }}</h2>
      @if (data(); as current) {
        <p class="muted small">
          @if (current.source === 'environment') { {{ 'Using the server settings (infra/.env) until you save here.' | t }} }
          @else { {{ (current.updatedAt ? 'Saved in KuTax on {date}.' : 'Saved in KuTax.') | t: { date: (current.updatedAt | date: ('d MMM yyyy, h:mm a' | t)) } }} }
          <span class="chip" [class.chip--done]="current.configured">{{ current.configured ? ('Ready' | t) : ('Not ready' | t) }}</span>
        </p>
        @if (current.apiKeyUnreadable) {
          <p class="notice notice--error" role="alert">{{ 'The saved key can’t be decrypted (the server’s protection keys changed). Enter it again.' | t }}</p>
        }
        <form (ngSubmit)="save()" #form="ngForm">
          <fieldset [disabled]="busy()">
            <label class="check"><input type="checkbox" name="enabled" [(ngModel)]="enabled" /><span><strong>{{ 'Read receipts with AI' | t }}</strong> {{ '— users still have to consent, and they confirm every value.' | t }}</span></label>
            <label class="field">{{ 'Provider' | t }}
              <select name="provider" [(ngModel)]="provider" (ngModelChange)="providerChanged()">
                @for (option of current.providers; track option) { <option [value]="option">{{ option }}</option> }
              </select>
            </label>
            <label class="field">{{ 'API key' | t }}
              <input name="apiKey" type="password" autocomplete="off" spellcheck="false" maxlength="500" [(ngModel)]="apiKey"
                [placeholder]="current.apiKeySet && !clearKey ? 'Saved key ' + current.apiKeyHint + ' — leave blank to keep it' : 'Paste your ' + provider + ' API key'" />
            </label>
            @if (current.apiKeySet) {
              <label class="check small"><input type="checkbox" name="clearKey" [(ngModel)]="clearKey" /><span>{{ 'Remove the saved key' | t }}</span></label>
            }
            <div class="row models-row">
              <button class="btn btn--small" type="button" (click)="loadModels()" [disabled]="busy() || !current.apiKeySet"><app-icon name="check" [size]="16" />{{ 'Check key & load models' | t }}</button>
              <span class="muted small">{{ models().length ? models().length + (' models available' | t) : current.apiKeySet ? ('Uses the saved key' | t) : ('Save a key first' | t) }}</span>
            </div>
            <label class="field">{{ provider === 'Groq' ? ('Photo model (reads images)' | t) : ('Model' | t) }}
              <input name="model" list="ai-models" autocomplete="off" spellcheck="false" maxlength="200" [(ngModel)]="model" />
            </label>
            @if (provider === 'Groq') {
              <label class="field">{{ 'PDF text model' | t }}
                <input name="textModel" list="ai-models" autocomplete="off" spellcheck="false" maxlength="200" [(ngModel)]="textModel" />
              </label>
            }
            <datalist id="ai-models">@for (id of models(); track id) { <option [value]="id"></option> }</datalist>
            <label class="field">{{ 'Processing region' | t }}
              <input name="region" maxlength="500" [(ngModel)]="region" />
            </label>
            <label class="field">{{ 'Retention notice shown to users' | t }}
              <textarea name="retentionNotice" maxlength="2000" [(ngModel)]="retentionNotice"></textarea>
            </label>
            <p class="notice notice--warning small">{{ 'Users see these details before they consent. Changing the provider, a model, the region or the notice asks every user to consent again. The key itself is encrypted and never shown again.' | t }}</p>
            @if (error()) { <p class="notice notice--error" role="alert">{{ error() | t }}</p> }
            @if (message()) { <p class="notice notice--success" role="status">{{ message() | t }}</p> }
            <button class="btn btn--primary" type="submit">{{ 'Save AI settings' | t }}</button>
          </fieldset>
        </form>
      } @else if (error()) {
        <p class="notice notice--error" role="alert">{{ error() | t }}</p>
      }
    </section>
  `,
  styles: `.models-row { margin: -4px 0 14px; } h2 { margin-top: 4px; } .chip { margin-left: 6px; }`,
})
export class AiSettingsEditor implements OnInit {
  private readonly http = inject(HttpClient);
  readonly saved = output<void>();
  readonly data = signal<AiSettings | null>(null);
  readonly models = signal<string[]>([]);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  enabled = false; provider: 'Groq' | 'OpenAI' = 'Groq'; apiKey = ''; clearKey = false;
  model = ''; textModel = ''; region = ''; retentionNotice = '';

  ngOnInit() { void this.load(); }

  async load() {
    try {
      this.apply(await firstValueFrom(this.http.get<AiSettings>('/api/admin/ai-settings')));
    } catch (error) {
      this.error.set(apiError(error, 'We couldn’t load the AI settings.'));
    }
  }

  providerChanged() {
    this.models.set([]);
    if (this.provider === 'Groq') {
      // Offer reviewed starting points instead of empty fields; nothing is saved until the administrator saves.
      this.model ||= groqDefaults.model;
      this.textModel ||= groqDefaults.textModel;
      this.region ||= groqDefaults.region;
      this.retentionNotice ||= groqDefaults.retentionNotice;
    }
  }

  async save() {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(''); this.message.set('');
    try {
      const result = await firstValueFrom(this.http.put<AiSettings>('/api/admin/ai-settings', {
        enabled: this.enabled, provider: this.provider, apiKey: this.apiKey.trim() || null, clearApiKey: this.clearKey,
        model: this.model, textModel: this.provider === 'Groq' ? this.textModel : '', region: this.region, retentionNotice: this.retentionNotice,
      }));
      this.apply(result);
      this.message.set(result.configured ? 'AI settings saved. Users can now give consent in their settings.' : 'AI settings saved. AI stays off until everything is filled in and enabled.');
      this.saved.emit();
    } catch (error) {
      this.error.set(apiError(error, 'We couldn’t save the AI settings.'));
    } finally {
      this.busy.set(false);
    }
  }

  async loadModels() {
    if (this.busy()) return;
    this.busy.set(true); this.error.set(''); this.message.set('');
    try {
      const result = await firstValueFrom(this.http.post<{ models: string[] }>('/api/admin/ai-settings/models', {}));
      this.models.set(result.models);
      this.message.set(`Key works. ${result.models.length} models are available — pick them from the suggestions.`);
    } catch (error) {
      this.error.set(apiError(error, 'We couldn’t reach the provider with the saved key.'));
    } finally {
      this.busy.set(false);
    }
  }

  private apply(value: AiSettings) {
    this.data.set(value);
    this.enabled = value.enabled; this.provider = value.provider; this.apiKey = ''; this.clearKey = false;
    this.model = value.model; this.textModel = value.textModel; this.region = value.region; this.retentionNotice = value.retentionNotice;
    if (!value.model && value.provider === 'Groq') this.providerChanged();
  }
}
