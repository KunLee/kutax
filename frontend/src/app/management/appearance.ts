import { Component, inject } from '@angular/core';
import { Theme, ThemeName } from '../core/theme';
import { I18n, Language, TranslatePipe } from '../core/i18n';

/** Lets each person choose the KuTax look on this device. */
@Component({
  selector: 'app-appearance', imports: [TranslatePipe],
  template: `
    <section class="card section" aria-labelledby="appearance-title">
      <span class="kicker">{{ 'Appearance' | t }}</span>
      <h2 id="appearance-title">{{ 'Choose your look' | t }}</h2>
      <div class="options" role="radiogroup" aria-labelledby="appearance-title">
        @for (option of options; track option.value) {
          <label class="option" [class.selected]="theme.current() === option.value">
            <input type="radio" name="theme" [value]="option.value" [checked]="theme.current() === option.value" (change)="theme.set(option.value)" />
            <span class="swatches" aria-hidden="true">@for (colour of option.colours; track colour) { <i [style.background]="colour"></i> }</span>
            <span><strong>{{ option.label | t }}</strong><small>{{ option.detail | t }}</small></span>
          </label>
        }
      </div>
      <p class="muted small">{{ 'Saved on this device. Everything works the same in both.' | t }}</p>
      <h3 id="language-title">{{ 'Language' | t }} · Language</h3>
      <div class="options" role="radiogroup" aria-labelledby="language-title">
        @for (option of languages; track option.value) {
          <label class="option" [class.selected]="i18n.language() === option.value" [attr.lang]="option.lang">
            <input type="radio" name="language" [value]="option.value" [checked]="i18n.language() === option.value" (change)="i18n.set(option.value)" />
            <span class="flag" aria-hidden="true">{{ option.mark }}</span>
            <span><strong>{{ option.label }}</strong><small>{{ option.detail }}</small></span>
          </label>
        }
      </div>
      <p class="muted small">{{ 'Tax packs are always prepared in both English and Chinese. myTax itself is in English, so section names stay in English.' | t }}</p>
    </section>
  `,
  styles: `
    .options { display: grid; gap: 12px; grid-template-columns: 1fr 1fr; margin: 14px 0 8px; }
    .option { align-items: center; border: 2px solid var(--line); border-radius: 14px; cursor: pointer; display: grid; gap: 12px; grid-template-columns: auto auto 1fr; padding: 14px; }
    .option.selected { border-color: var(--ink); box-shadow: 3px 3px 0 var(--ink); }
    .option input { accent-color: var(--purple); height: 20px; margin: 0; width: 20px; }
    .option strong, .option small { display: block; }
    .option small { color: var(--ink-soft); font-size: 13px; margin-top: 2px; }
    .swatches { display: flex; }
    .flag { align-items: center; background: var(--ink); border-radius: 8px; color: var(--white); display: flex; font-weight: 800; height: 32px; justify-content: center; width: 40px; }
    h3 { margin: 22px 0 0; }
    .swatches i { border: 2px solid var(--ink); border-radius: 50%; height: 22px; margin-left: -6px; width: 22px; }
    .swatches i:first-child { margin-left: 0; }
    @media (max-width: 560px) { .options { grid-template-columns: 1fr; } }
  `,
})
export class Appearance {
  readonly theme = inject(Theme);
  readonly i18n = inject(I18n);
  /** Each language is described in itself, so it can be found whichever one is showing. */
  readonly languages: { value: Language; lang: string; mark: string; label: string; detail: string }[] = [
    { value: 'en', lang: 'en-AU', mark: 'EN', label: 'English', detail: 'Matches myTax and the ATO.' },
    { value: 'zh', lang: 'zh-CN', mark: '中', label: '简体中文', detail: '界面使用中文；myTax 的栏目名称保留英文。' },
  ];
  readonly options: { value: ThemeName; label: string; detail: string; colours: string[] }[] = [
    { value: 'retro', label: 'Retro', detail: 'Teal and navy, like a pocket gadget. Default.', colours: ['#20c9c6', '#06375e', '#f5dc83', '#f47d6b'] },
    { value: 'classic', label: 'Classic', detail: 'Soft paper and purple, the original look.', colours: ['#f8f6ef', '#7856d8', '#8bd7ee', '#ff8a5c'] },
  ];
}
