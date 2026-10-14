import { Component, computed, inject, input } from '@angular/core';
import { Icon, IconName } from '../core/icon';
import { I18n, TranslatePipe } from '../core/i18n';

export interface PackFile { kind: string; fileName: string; size: number; }

/** What each tax-pack file is, in plain words. */
const kinds: Record<string, { title: string; hint: string; icon: IconName }> = {
  summary: { title: 'Summary', hint: 'Your year on one page, to read or print', icon: 'report' },
  guide: { title: 'myTax guide', hint: 'What to enter, section by section', icon: 'file' },
  workbook: { title: 'Spreadsheet', hint: 'Every expense, for you or your tax agent', icon: 'archive' },
  evidence: { title: 'Receipts', hint: 'All the originals in one ZIP', icon: 'folder' },
};
const order = ['summary', 'guide', 'workbook', 'evidence'];

/**
 * A tax pack's downloads, in the language you're using: the entry guide (what you need for myTax) and everything in
 * one ZIP. Single files are one tap away for anyone who wants just one (a tax agent, say).
 */
@Component({
  selector: 'app-pack-files',
  imports: [Icon, TranslatePipe],
  template: `
    <div class="main">
      @if (guide(); as file) {
        <a class="btn btn--primary big" [href]="url(file)" [attr.aria-label]="'Download {name} ({type}, {size})' | t: { name: 'myTax guide' | t, type: type(file), size: size(file) }">
          <app-icon name="file" [size]="22" /><span><strong>{{ 'Entry guide' | t }}</strong><small>{{ 'Follow it to fill in myTax' | t }} · {{ type(file) }}</small></span></a>
      }
      <a class="btn big" [href]="'/api/tax-exports/' + packId() + '/download?lang=' + language()">
        <app-icon name="download" [size]="22" /><span><strong>{{ 'Download everything' | t }}</strong><small>{{ 'Summary, spreadsheet and receipts in one ZIP' | t }}</small></span></a>
    </div>
    <details class="single">
      <summary>{{ 'Download a single file' | t }}</summary>
      <ul>
        @for (file of sorted(); track file.kind) {
          <li><a [href]="url(file)"><app-icon [name]="info(file).icon" [size]="16" />{{ info(file).title | t }} <span class="type">{{ type(file) }} · {{ size(file) }}</span></a><small>{{ info(file).hint | t }}</small></li>
        }
      </ul>
    </details>
  `,
  styles: `
    .main { display: grid; gap: 10px; grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .big { align-items: center; display: flex; gap: 12px; justify-content: flex-start; min-height: 64px; padding: 10px 16px; text-align: left; }
    .big strong, .big small { display: block; }
    .big small { font-size: 12px; font-weight: 600; opacity: .8; }
    .single { margin-top: 12px; }
    .single summary { cursor: pointer; font-size: 13px; font-weight: 700; }
    .single ul { display: grid; gap: 6px; list-style: none; margin: 8px 0 0; padding: 0; }
    .single li a { align-items: center; display: inline-flex; font-weight: 700; gap: 6px; }
    .single li small { color: var(--ink-soft); display: block; font-size: 12px; margin-left: 22px; }
    .type { color: var(--ink-soft); font-size: 11px; font-weight: 700; }
    @media (max-width: 640px) { .main { grid-template-columns: minmax(0, 1fr); } }
  `,
})
export class PackFiles {
  private readonly i18n = inject(I18n);
  readonly packId = input.required<string>();
  readonly files = input.required<PackFile[]>();
  readonly language = computed(() => this.i18n.language());
  /** Each kind in the interface language, or English for packs prepared before Chinese files existed. */
  readonly sorted = computed(() => {
    const byKind = new Map(this.files().map((f) => [f.kind, f]));
    const zh = this.language() === 'zh';
    return order.map((kind) => (zh ? byKind.get(kind + '-zh') : undefined) ?? byKind.get(kind)).filter((f): f is PackFile => !!f);
  });
  readonly guide = computed(() => this.sorted().find((f) => f.kind.startsWith('guide')) ?? null);
  url(file: PackFile) { return '/api/tax-exports/' + this.packId() + '/files/' + file.kind; }
  info(file: PackFile) { return kinds[file.kind.replace(/-zh$/, '')] ?? { title: file.fileName, hint: '', icon: 'file' as IconName }; }
  type(file: PackFile) { return (file.fileName.split('.').pop() ?? '').toUpperCase().replace('XLSX', 'EXCEL'); }
  size(file: PackFile) { return file.size >= 1024 * 1024 ? (file.size / 1024 / 1024).toFixed(1) + ' MB' : Math.max(1, Math.round(file.size / 1024)) + ' KB'; }
}
