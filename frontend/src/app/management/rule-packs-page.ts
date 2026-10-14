import { Component, computed, effect, inject, input, OnDestroy, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Icon } from '../core/icon';
import { yearLabel } from '../core/session';
import { categoryLabel } from '../core/categories';
import { showAttention } from '../core/attention';
import { TranslatePipe } from '../core/i18n';

interface YearRules { year: number; rules: number; reviewed: number; revision: number; fromPack: boolean; }
interface PackSummary { id: string; financialYearStart: number; status: string; origin: string; createdAt: string; decidedAt: string | null; errorCode: string | null; errors: number; warnings: number; changed: number; }
interface Overview { years: YearRules[]; nextYear: number; aiConfigured: boolean; check: { checkedAt: string; publishedAt: string | null; message: string | null } | null; packs: PackSummary[]; }
interface Field { category: string; field: string; previous: string | null; proposed: string | null; changed: boolean; quote: string | null; source: string | null; quoteVerified: boolean | null; }
interface Report { errors: string[]; warnings: string[]; fields: Field[]; changedFields: number; comparedWith: string; }
interface PackDetail extends PackSummary { note: string | null; retryAt: string | null; report: Report | null; pack: { sources: { id: string; url: string }[] } | null; }

const fieldNames: Record<string, string> = {
  label: 'Name', section: 'myTax section', paperReference: 'Label', claimTypes: 'Claim types', deductible: 'Formula', checks: 'Checks', conditions: 'Conditions', declaration: 'Owner declaration',
};
const statusNames: Record<string, string> = { Queued: 'Waiting to draft', Drafting: 'AI is drafting', Draft: 'Ready for your check', Active: 'In force', Rejected: 'Rejected', Superseded: 'Replaced', Failed: 'Draft failed' };

@Component({
  selector: 'app-rule-packs-page',
  imports: [TranslatePipe, DatePipe, FormsModule, RouterLink, Icon],
  template: `
    <div class="page">
      <section class="page-heading">
        <div>
          <span class="eyebrow">{{ 'Administrator' | t }}</span>
          <h1>{{ id() ? ('Check rule update' | t) : ('Tax rules' | t) }}</h1>
          <p>{{ 'Each financial year has its own rules: which myTax section an expense goes to, its conditions, limits and formula. When the ATO publishes a new year, KuTax drafts that year’s rules with AI, checks every figure against the official text, and waits for you.' | t }}</p>
        </div>
        @if (id()) { <a class="btn btn--quiet" routerLink="/rules"><app-icon name="back" [size]="18" />{{ 'All rules' | t }}</a> }
      </section>
      @if (message()) { <p class="notice" role="status">{{ message() | t }}</p> }

      @if (!id()) {
        @if (overview(); as data) {
          <section class="card section" aria-labelledby="next-title">
            <span class="kicker">{{ 'Next year' | t }}</span>
            <h2 id="next-title">{{ label(data.nextYear) }}</h2>
            <p class="small">
              @if (data.check?.publishedAt) { {{ 'The ATO instructions are published.' | t }} }
              @else if (data.check?.message) { {{ data.check!.message | t }} }
              @else { {{ 'From June {year}, KuTax checks for them when someone opens that year, and drafts the rules once they’re out.' | t: { year: data.nextYear + 1 } }} }
              @if (data.check) { <span class="muted">{{ 'Last checked {date}.' | t: { date: (data.check.checkedAt | date: ('d MMM yyyy, h:mm a' | t)) } }}</span> }
            </p>
            @if (!data.aiConfigured) { <p class="notice notice--warning small">{{ 'AI isn’t set up, so KuTax can’t draft rules automatically. Set it up in Settings, or import a pack.' | t }}</p> }
            <div class="row">
              <button class="btn" type="button" (click)="check()" [disabled]="busy()">{{ 'Check now' | t }}</button>
              @if (data.aiConfigured && latestYear(); as year) { @if (!drafting(year)) {
                <button class="btn btn--quiet" type="button" (click)="draft(year)" [disabled]="busy()">{{ 'Re-check {year} with AI' | t: { year: label(year) } }}</button>
              } }
            </div>
          </section>

          @if (data.packs.length) {
            <section class="card section" aria-labelledby="packs-title">
              <span class="kicker">{{ 'Updates' | t }}</span><h2 id="packs-title">{{ 'Rule updates' | t }}</h2>
              <ul class="list">
                @for (pack of data.packs; track pack.id) {
                  <li><a [routerLink]="['/rules', pack.id]">
                    <span><strong>{{ label(pack.financialYearStart) }}</strong><small>{{ pack.origin === 'AiDraft' ? ('Drafted with AI' | t) : ('Imported' | t) }} · {{ pack.createdAt | date: ('d MMM yyyy' | t) }}
                      @if (pack.status === 'Draft') { · {{ '{n} changes' | t: { n: pack.changed } }} · {{ '{n} to note' | t: { n: pack.warnings } }} @if (pack.errors) { · {{ (pack.errors === 1 ? '{n} problem' : '{n} problems') | t: { n: pack.errors } }} } }</small></span>
                    <span class="chip" [class.chip--review]="pack.status === 'Draft' || pack.status === 'Failed'" [class.chip--done]="pack.status === 'Active'" [class.chip--info]="pack.status === 'Queued' || pack.status === 'Drafting'">{{ pack.errorCode === 'RateLimited' && pack.status === 'Queued' ? ('Paused · AI quota' | t) : statusName(pack.status) }}</span>
                  </a></li>
                }
              </ul>
            </section>
          }

          <section class="card section" aria-labelledby="years-title">
            <span class="kicker">{{ 'In force' | t }}</span><h2 id="years-title">{{ 'Rules by year' | t }}</h2>
            <ul class="list">
              @for (year of data.years; track year.year) {
                <li class="static"><span><strong>{{ label(year.year) }}</strong><small>{{ '{n} categories · version {v}' | t: { n: year.rules, v: year.revision } }}{{ year.fromPack ? (' (from an update)' | t) : (' (built in)' | t) }} · {{ year.reviewed === year.rules ? ('all checked' | t) : ('{n} not checked yet' | t: { n: year.rules - year.reviewed }) }}</small></span>
                  <a class="btn btn--small export" [href]="'/api/rule-packs/export/' + year.year" download>{{ 'Export' | t }}<span class="sr-only"> {{ '{year} rules' | t: { year: label(year.year) } }}</span></a></li>
              }
            </ul>
            <details class="small"><summary>{{ 'Import a rule pack' | t }}</summary>
              <p>{{ 'For changes the AI can’t make, such as a new formula: export a year, edit the JSON (or get a reviewed pack from your adviser), and import it here. It’s validated and compared before you decide.' | t }}</p>
              <label class="field">{{ 'Rule pack (.json)' | t }}<input type="file" accept="application/json,.json" (change)="import($event)" [disabled]="busy()"></label>
            </details>
          </section>
        }
      } @else {
        @if (detail(); as pack) {
          <section class="card section" aria-labelledby="pack-title">
            <div class="section-heading"><div><span class="kicker">{{ pack.origin === 'AiDraft' ? ('Drafted with AI' | t) : ('Imported' | t) }}</span><h2 id="pack-title">{{ label(pack.financialYearStart) }}</h2></div>
              <span class="chip" [class.chip--review]="pack.status === 'Draft'" [class.chip--done]="pack.status === 'Active'">{{ statusName(pack.status) | t }}</span></div>
            @if (pack.errorCode === 'RateLimited' && pack.status === 'Queued') {
              <p class="notice notice--warning" role="status">{{ 'Paused: the AI provider’s usage limit was reached. KuTax carries on automatically around {date}, keeping the categories it already read.' | t: { date: (pack.retryAt | date: ('d MMM, h:mm a' | t)) } }}</p>
            } @else if (pack.status === 'Queued' || pack.status === 'Drafting') { <p class="notice reading" role="status"><span class="spinner" aria-hidden="true"></span>{{ 'Reading the ATO instructions and drafting each category twice. This takes a few minutes.' | t }}</p> }
            @if (pack.status === 'Failed') { <p class="notice notice--error">{{ 'The draft couldn’t be finished ({code}). Try again later or import a pack.' | t: { code: pack.errorCode } }}</p> }
            @if (pack.report; as report) {
              <p class="small">{{ 'Compared with {what}:' | t: { what: report.comparedWith } }} <strong>{{ (report.changedFields === 1 ? '{n} change' : '{n} changes') | t: { n: report.changedFields } }}</strong>{{ '. Every figure must be backed by a quote found word for word in the official text.' | t }}</p>
              @if (report.errors.length) {
                <div class="notice notice--error"><strong>{{ 'Can’t be activated yet' | t }}</strong><ul>@for (e of report.errors; track $index) { <li>{{ e | t }}</li> }</ul></div>
              }
              @if (report.warnings.length) {
                <div class="notice notice--warning"><strong>{{ 'Please look at these' | t }}</strong><ul>@for (w of report.warnings; track $index) { <li>{{ w | t }}</li> }</ul></div>
              }
              <label class="check toggle"><input type="checkbox" [(ngModel)]="showAll"><span>{{ 'Show unchanged fields too' | t }}</span></label>
              @for (group of groups(report); track group.category) {
                <article class="rule">
                  <h3>{{ category(group.category) | t }}</h3>
                  <dl>
                    @for (f of group.fields; track f.field) {
                      @if (showAll || f.changed || f.quoteVerified === false) {
                        <div [class.changed]="f.changed">
                          <dt>{{ fieldName(f.field) | t }}</dt>
                          <dd>
                            @if (f.changed && f.previous !== null) { <span class="was">{{ f.previous }}</span> → }
                            <span class="now">{{ f.proposed }}</span>
                            @if (f.quote) {
                              <blockquote [class.bad]="f.quoteVerified === false">“{{ f.quote }}”
                                <small>@if (f.quoteVerified === true) { ✓ {{ 'found in the official text' | t }} } @else if (f.quoteVerified === false) { ✗ {{ 'not found in the official text' | t }} } @else { {{ 'not machine-checked' | t }} } · {{ sourceName(pack, f.source) | t }}</small></blockquote>
                            }
                          </dd>
                        </div>
                      }
                    }
                  </dl>
                </article>
              }
              @if (pack.status === 'Draft') {
                <div class="decide">
                  <label class="field">{{ 'Review note' | t }}<textarea id="pack-note" [(ngModel)]="note" maxlength="2000" [placeholder]="'For example: checked the changed figures against the 2027 instructions' | t"></textarea></label>
                  <label class="check"><input id="pack-confirm" type="checkbox" [(ngModel)]="confirm"><span>{{ 'I checked the changes and quotes against the official sources. Put these rules in force for {year}.' | t: { year: label(pack.financialYearStart) } }}</span></label>
                  <div class="row">
                    <button class="btn btn--primary" type="button" (click)="activate(pack)" [disabled]="busy() || report.errors.length > 0">{{ 'Put in force' | t }}</button>
                    <button class="btn btn--quiet btn--danger" type="button" (click)="reject(pack)" [disabled]="busy()">{{ 'Reject' | t }}</button>
                  </div>
                  <p class="small muted">{{ 'Claims already approved for that year under older rules need approving again. Tax packs you’ve prepared don’t change.' | t }}</p>
                </div>
              }
            }
          </section>
        }
      }
    </div>
  `,
  styles: `
    .list { list-style: none; margin: 0; padding: 0; }
    .list li { border-top: 1px solid var(--line); }
    .list a, .list li.static { align-items: center; color: inherit; display: flex; gap: 12px; justify-content: space-between; padding: 12px 0; text-decoration: none; }
    .list strong, .list small { display: block; }
    .list small { color: var(--ink-soft); font-size: 12px; }
    .export { flex: none; padding: 0 16px; white-space: nowrap; }
    .reading { align-items: center; display: flex; gap: 10px; }
    .notice ul { margin: 6px 0 0; padding-left: 18px; }
    .toggle { margin: 10px 0; }
    .rule { border-top: 1px solid var(--line); padding-top: 12px; }
    .rule h3 { margin: 0 0 6px; }
    dl { display: grid; gap: 8px; margin: 0 0 12px; }
    dl div { display: grid; gap: 4px 12px; grid-template-columns: 150px minmax(0, 1fr); }
    dl div.changed dt { color: var(--orange-ink); }
    dt { font-size: 13px; font-weight: 800; }
    dd { margin: 0; min-width: 0; overflow-wrap: anywhere; }
    .was { color: var(--ink-soft); text-decoration: line-through; }
    .now { font-weight: 700; }
    blockquote { background: var(--mint-soft); border-left: 3px solid var(--mint-ink); font-size: 13px; margin: 6px 0 0; padding: 6px 10px; }
    blockquote.bad { background: var(--orange-soft); border-color: var(--orange-ink); }
    blockquote small { color: var(--ink-soft); display: block; margin-top: 2px; }
    .decide { background: #fbfaf6; border: 1px solid var(--line); border-radius: 14px; margin-top: 12px; padding: 4px 14px 14px; }
    @media (max-width: 600px) { dl div { grid-template-columns: 1fr; } }
  `,
})
export class RulePacksPage implements OnDestroy {
  readonly id = input<string>();
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  readonly overview = signal<Overview | null>(null);
  readonly detail = signal<PackDetail | null>(null);
  readonly busy = signal(false);
  readonly message = signal('');
  /** A draft for that year is already queued or being written. */
  drafting(year: number) { return this.overview()?.packs.some((p) => p.financialYearStart === year && (p.status === 'Queued' || p.status === 'Drafting')) ?? false; }
  readonly latestYear = computed(() => this.overview()?.years[0]?.year ?? null);
  showAll = false;
  note = 'Checked the changed figures and their quotes against the official sources.';
  confirm = false;
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

  label(year: number) { return yearLabel(year); }
  statusName(status: string) { return statusNames[status] ?? status; }
  category(key: string) { return categoryLabel(key); }
  fieldName(field: string) { return field.startsWith('parameters.') ? 'Limit: ' + field.slice(11) : fieldNames[field] ?? field; }
  sourceName(pack: PackDetail, id: string | null) {
    if (id === 'instructions') return 'Individual tax return instructions';
    return id?.startsWith('guidance-') ? 'ATO guidance page' : id ?? '';
  }
  groups(report: Report) {
    const byCategory = new Map<string, Field[]>();
    for (const field of report.fields) byCategory.set(field.category, [...(byCategory.get(field.category) ?? []), field]);
    return [...byCategory].map(([category, fields]) => ({ category, fields })).filter((g) => this.showAll || g.fields.some((f) => f.changed || f.quoteVerified === false));
  }

  async load() {
    clearTimeout(this.timer);
    const id = this.id();
    try {
      if (id) {
        const pack = await firstValueFrom(this.http.get<PackDetail>(`/api/rule-packs/${id}`));
        this.detail.set(pack);
        if (pack.status === 'Queued' || pack.status === 'Drafting') this.timer = setTimeout(() => void this.load(), 5000);
      } else {
        const overview = await firstValueFrom(this.http.get<Overview>('/api/rule-packs'));
        this.overview.set(overview);
        if (overview.packs.some((p) => p.status === 'Queued' || p.status === 'Drafting')) this.timer = setTimeout(() => void this.load(), 5000);
      }
    } catch {
      this.message.set('We couldn’t load the rules. Refresh to try again.');
    }
  }

  private async act(work: () => Promise<unknown>, success: string) {
    if (this.busy()) return;
    this.busy.set(true);
    try {
      await work();
      this.message.set(success);
      await this.load();
    } catch (error) {
      this.message.set(error instanceof HttpErrorResponse ? error.error?.detail ?? 'That didn’t work. Try again.' : 'That didn’t work.');
    } finally {
      this.busy.set(false);
    }
  }

  check() {
    return this.act(() => firstValueFrom(this.http.post('/api/rule-packs/check', {})), 'Checked the ATO publications.');
  }

  draft(year: number) {
    return this.act(async () => {
      const result = await firstValueFrom(this.http.post<{ id: string }>('/api/rule-packs/draft', { financialYearStart: year }));
      await this.router.navigate(['/rules', result.id]);
    }, 'Drafting started. You can leave this page; it keeps going on the server.');
  }

  async import(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    await this.act(async () => {
      const text = await file.text();
      const result = await firstValueFrom(this.http.post<{ id: string }>('/api/rule-packs/import', text, { headers: { 'Content-Type': 'application/json' } }));
      await this.router.navigate(['/rules', result.id]);
    }, 'Imported. Check the comparison below.');
  }

  activate(pack: PackDetail) {
    if (!this.note.trim()) { showAttention('pack-note', 'Add a short note about what you checked.'); return Promise.resolve(); }
    if (!this.confirm) { showAttention('pack-confirm', 'Tick this to confirm you checked the changes.'); return Promise.resolve(); }
    return this.act(() => firstValueFrom(this.http.post(`/api/rule-packs/${pack.id}/activate`, { confirm: this.confirm, note: this.note.trim() })), 'These rules are now in force.')
      .then(() => { this.confirm = false; });
  }

  reject(pack: PackDetail) {
    return this.act(() => firstValueFrom(this.http.post(`/api/rule-packs/${pack.id}/reject`, { confirm: true, note: this.note.trim() || null })), 'Update rejected.');
  }
}
