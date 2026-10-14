import { inject, Injectable, Pipe, PipeTransform, signal } from '@angular/core';

type Dictionary = { words: Record<string, string>; patterns: [RegExp, string][] };
/** Chinese is loaded only when it's used, so English visitors don't download it. */
let chinese: Dictionary | undefined;
async function loadChinese(): Promise<Dictionary> {
  if (!chinese) {
    const [{ zh }, { zhPatterns }] = await Promise.all([import('./i18n-zh'), import('./i18n-zh-patterns')]);
    chinese = { words: zh, patterns: zhPatterns };
  }
  return chinese;
}

/**
 * Interface languages. English text is the key: every string is written in English in the templates and looked up
 * here, so a missing translation simply shows English. Chinese is a translation aid; myTax itself is in English.
 */
export type Language = 'en' | 'zh';
const key = 'kutax.language';
export type Params = Record<string, string | number | null | undefined>;

function stored(): Language {
  try {
    const saved = localStorage.getItem(key);
    if (saved === 'zh' || saved === 'en') return saved;
  } catch {
    // Storage can be unavailable (private mode); fall back to the browser's language.
  }
  return typeof navigator !== 'undefined' && navigator.language?.toLowerCase().startsWith('zh') ? 'zh' : 'en';
}

function apply(language: Language) {
  document.documentElement.lang = language === 'zh' ? 'zh-CN' : 'en-AU';
}

/** Called before Angular starts, so the first paint is already in the chosen language. */
export async function applyStoredLanguage() {
  const language = stored();
  apply(language);
  if (language === 'zh') await loadChinese().catch(() => undefined);
}

/** Replaces {name} placeholders with values. */
export function fill(text: string, params?: Params) {
  return params ? text.replace(/\{(\w+)\}/g, (match, name: string) => (params[name] ?? match).toString()) : text;
}

let active: I18n | undefined;

/** For plain functions outside templates (such as the attention note); English until the app has started. */
export function translate(text: string, params?: Params) {
  return active ? active.t(text, params) : fill(text, params);
}

@Injectable({ providedIn: 'root' })
export class I18n {
  readonly language = signal<Language>(stored());
  /** Bumped when the Chinese dictionary arrives, so the pipe re-renders. */
  private readonly loaded = signal(!!chinese);

  constructor() {
    active = this;
  }

  set(language: Language) {
    if (language === 'zh' && !chinese) void loadChinese().then(() => this.loaded.set(true), () => undefined);
    this.language.set(language);
    apply(language);
    try {
      localStorage.setItem(key, language);
    } catch {
      // Applied for this visit only.
    }
  }

  /**
   * Translates English text (a key, or any message from the server). Exact matches come first, then patterns for
   * messages with numbers or names in them. Unknown text is returned unchanged, so nothing ever goes blank.
   */
  t(text: string | null | undefined, params?: Params): string {
    if (text === null || text === undefined) return '';
    if (this.language() !== 'zh' || !this.loaded() || !chinese) return fill(text, params);
    const exact = chinese.words[text];
    if (exact !== undefined) return fill(exact, params);
    // Patterns write {1}, {2}… for captured parts, which are translated in turn (a rule's name inside a message).
    for (const [pattern, template] of chinese.patterns) {
      const match = pattern.exec(text);
      if (match) return template.replace(/\{(\d)\}/g, (_, index: string) => this.t(match[+index]));
    }
    return fill(text, params);
  }
}

/** `{{ 'Approve in one go' | t }}` or `{{ '{n} receipts' | t: { n: count } }}`. Re-runs when the language changes. */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18n);
  transform(text: string | null | undefined, params?: Params): string {
    return this.i18n.t(text, params);
  }
}
