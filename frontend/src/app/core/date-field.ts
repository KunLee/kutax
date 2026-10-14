import { Component, computed, effect, ElementRef, forwardRef, inject, input, signal, untracked } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { Icon } from './icon';
import { I18n, TranslatePipe } from './i18n';

/** A calendar date as "yyyy-MM-dd", the same value a native date input gives. */
type Iso = string;
const pad = (n: number) => String(n).padStart(2, '0');
const iso = (d: Date): Iso => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const fromIso = (value: Iso) => { const [y, m, d] = value.split('-').map(Number); return new Date(y, m - 1, d); };
const addDays = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);
const monthsEn = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

/** Reads what people type: 26/08/2026, 26-8-2026, 26.08.2026, 2026-08-26 or 2026/8/26. */
export function parseDate(text: string): Iso | null {
  const t = text.trim();
  let y: number, m: number, d: number;
  let match = /^(\d{4})[-/.年](\d{1,2})[-/.月](\d{1,2})日?$/.exec(t);
  if (match) [y, m, d] = [+match[1], +match[2], +match[3]];
  else if ((match = /^(\d{1,2})[-/.](\d{1,2})[-/.](\d{4})$/.exec(t))) [d, m, y] = [+match[1], +match[2], +match[3]];
  else return null;
  const date = new Date(y, m - 1, d);
  return date.getFullYear() === y && date.getMonth() === m - 1 && date.getDate() === d ? iso(date) : null;
}

/**
 * A date field in the app's own look: type the date, or pick it from a themed calendar (weeks start on Monday, as in
 * Australia). Replaces the browser's date picker, which can't be styled. Works with ngModel like a native date input.
 */
@Component({
  selector: 'app-date-field',
  imports: [Icon, TranslatePipe],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => DateField), multi: true }],
  host: { '(document:pointerdown)': 'outside($event)' },
  template: `
    <div class="field-box">
      <input class="input" [id]="inputId()" type="text" inputmode="numeric" autocomplete="off" [value]="text()" [placeholder]="placeholder()" [disabled]="disabled()"
             [attr.aria-required]="required() || null" (input)="typed($any($event.target).value)" (blur)="commit()" (keydown)="inputKey($event)">
      <button class="open" type="button" [disabled]="disabled()" [attr.aria-label]="'Choose a date' | t" [attr.aria-expanded]="open()" aria-haspopup="dialog" (click)="toggle()">
        <app-icon name="calendar" [size]="18" /></button>
    </div>
    @if (open()) {
      <div class="calendar" role="dialog" [attr.aria-label]="'Choose a date' | t" (keydown)="calendarKey($event)">
        <div class="month">
          <button type="button" class="step" (click)="shift(-1)" [attr.aria-label]="'Previous month' | t"><app-icon name="back" [size]="18" /></button>
          <strong aria-live="polite">{{ monthTitle() }}</strong>
          <button type="button" class="step" (click)="shift(1)" [attr.aria-label]="'Next month' | t"><app-icon name="arrow" [size]="18" /></button>
        </div>
        <div class="grid" role="grid">
          @for (name of weekdays(); track $index) { <span class="weekday" role="columnheader">{{ name }}</span> }
          @for (day of days(); track day.iso) {
            <button type="button" class="day" role="gridcell" [class.other]="!day.inMonth" [class.today]="day.iso === today" [class.selected]="day.iso === value()"
                    [disabled]="day.iso > latest()" [attr.aria-selected]="day.iso === value()" [attr.data-date]="day.iso" [tabindex]="day.iso === focused() ? 0 : -1"
                    (click)="pick(day.iso)">{{ day.date }}</button>
          }
        </div>
        <div class="foot"><button type="button" class="btn btn--small btn--quiet" (click)="pick(today)">{{ 'Today' | t }}</button></div>
      </div>
    }
  `,
  styles: `
    :host { display: block; position: relative; }
    .field-box { position: relative; }
    .field-box input { padding-right: 46px; width: 100%; }
    .open { align-items: center; background: none; border: 0; border-radius: 10px; color: var(--ink); cursor: pointer; display: flex; height: 36px; justify-content: center;
      position: absolute; right: 6px; top: 50%; transform: translateY(-50%); width: 36px; }
    .open:hover { background: var(--purple-soft); }
    .open:focus-visible, .step:focus-visible, .day:focus-visible { outline: 3px solid var(--purple); outline-offset: 1px; }
    .calendar { background: var(--white); border: 2px solid var(--ink); border-radius: 18px; box-shadow: 5px 5px 0 var(--ink); left: 0; padding: 12px; position: absolute;
      top: calc(100% + 8px); width: min(320px, calc(100vw - 32px)); z-index: 40; }
    .month { align-items: center; display: flex; justify-content: space-between; margin-bottom: 8px; }
    .month strong { font-size: 15px; font-weight: 800; }
    .step { align-items: center; background: var(--yellow); border: 2px solid var(--ink); border-radius: 10px; box-shadow: 2px 2px 0 var(--ink); color: var(--ink); cursor: pointer;
      display: flex; height: 34px; justify-content: center; width: 34px; }
    .step:active { box-shadow: none; transform: translate(2px, 2px); }
    .grid { display: grid; gap: 3px; grid-template-columns: repeat(7, 1fr); }
    .weekday { color: var(--ink-soft); font-family: var(--mono); font-size: 11px; font-weight: 700; padding: 4px 0; text-align: center; }
    .day { aspect-ratio: 1; background: none; border: 1.5px solid transparent; border-radius: 10px; color: var(--ink); cursor: pointer; font: inherit; font-size: 14px; font-weight: 700; min-height: 36px; }
    .day:hover:not(:disabled) { background: var(--purple-soft); }
    .day.other { color: var(--ink-soft); font-weight: 500; opacity: .55; }
    .day.today { border-color: var(--ink); }
    .day.selected { background: var(--mint, #2ec4a6); border-color: var(--ink); box-shadow: 2px 2px 0 var(--ink); color: var(--ink); }
    .day:disabled { cursor: not-allowed; opacity: .25; }
    .foot { border-top: 1.5px dashed var(--line); display: flex; justify-content: flex-end; margin-top: 8px; padding-top: 8px; }
  `,
})
export class DateField implements ControlValueAccessor {
  private readonly i18n = inject(I18n);
  private readonly host = inject(ElementRef<HTMLElement>);
  /** The id of the text box, so labels and "needs attention" can point at it. */
  readonly inputId = input.required<string>();
  readonly required = input(false);
  /** The latest date that can be picked, "yyyy-MM-dd"; defaults to tomorrow (receipts can't be from the future). */
  readonly max = input<Iso | null>(null);

  readonly value = signal<Iso>('');
  readonly text = signal('');
  readonly disabled = signal(false);
  readonly open = signal(false);
  readonly focused = signal<Iso>('');
  /** First day of the month on show. */
  readonly month = signal(new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  readonly today = iso(new Date());
  readonly latest = computed(() => this.max() ?? iso(addDays(new Date(), 1)));
  readonly chinese = computed(() => this.i18n.language() === 'zh');
  readonly placeholder = computed(() => (this.chinese() ? 'yyyy-mm-dd' : 'dd/mm/yyyy'));
  readonly weekdays = computed(() => (this.chinese() ? ['一', '二', '三', '四', '五', '六', '日'] : ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su']));
  readonly monthTitle = computed(() => { const m = this.month(); return this.chinese() ? `${m.getFullYear()}年${m.getMonth() + 1}月` : `${monthsEn[m.getMonth()]} ${m.getFullYear()}`; });
  /** Six weeks from the Monday on or before the 1st. */
  readonly days = computed(() => {
    const first = this.month();
    const start = addDays(first, -((first.getDay() + 6) % 7));
    return Array.from({ length: 42 }, (_, i) => { const d = addDays(start, i); return { iso: iso(d), date: d.getDate(), inMonth: d.getMonth() === first.getMonth() }; });
  });

  private onChange: (value: Iso) => void = () => {};
  private onTouched: () => void = () => {};

  constructor() {
    // Re-show the date in the new language's format when the language switches (not while typing).
    effect(() => { this.chinese(); untracked(() => this.text.set(this.format(this.value()))); });
  }

  writeValue(value: Iso | null) {
    this.value.set(value ?? '');
    this.text.set(this.format(value ?? ''));
  }
  registerOnChange(fn: (value: Iso) => void) { this.onChange = fn; }
  registerOnTouched(fn: () => void) { this.onTouched = fn; }
  setDisabledState(disabled: boolean) { this.disabled.set(disabled); if (disabled) this.open.set(false); }

  format(value: Iso) {
    if (!value) return '';
    const [y, m, d] = value.split('-');
    return this.chinese() ? `${y}-${m}-${d}` : `${d}/${m}/${y}`;
  }

  typed(text: string) {
    this.text.set(text);
    const parsed = parseDate(text);
    if (parsed && parsed !== this.value()) this.set(parsed, false);
    else if (!text.trim() && this.value()) this.set('', false);
  }

  /** On leaving the box, tidy what was typed into the standard format (or put back the last good date). */
  commit() {
    this.onTouched();
    this.text.set(this.format(this.value()));
  }

  toggle() {
    if (this.open()) { this.open.set(false); return; }
    const shown = this.value() ? fromIso(this.value()) : new Date();
    this.month.set(new Date(shown.getFullYear(), shown.getMonth(), 1));
    this.focused.set(this.value() || this.today);
    this.open.set(true);
    setTimeout(() => this.focusDay());
  }

  shift(months: number) {
    const m = this.month();
    this.month.set(new Date(m.getFullYear(), m.getMonth() + months, 1));
  }

  pick(value: Iso) {
    if (value > this.latest()) return;
    this.set(value, true);
    this.open.set(false);
    this.box()?.focus();
  }

  inputKey(event: KeyboardEvent) {
    if (event.key === 'ArrowDown' && event.altKey) { event.preventDefault(); this.toggle(); }
    else if (event.key === 'Enter') { this.commit(); }
  }

  calendarKey(event: KeyboardEvent) {
    const steps: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
    const current = fromIso(this.focused() || this.today);
    let next: Date | null = null;
    if (event.key in steps) next = addDays(current, steps[event.key]);
    else if (event.key === 'PageUp' || event.key === 'PageDown') next = new Date(current.getFullYear(), current.getMonth() + (event.key === 'PageUp' ? -1 : 1), current.getDate());
    else if (event.key === 'Escape') { event.preventDefault(); this.open.set(false); this.box()?.focus(); return; }
    if (!next) return;
    event.preventDefault();
    this.focused.set(iso(next));
    this.month.set(new Date(next.getFullYear(), next.getMonth(), 1));
    setTimeout(() => this.focusDay());
  }

  outside(event: Event) {
    if (this.open() && !(event.target instanceof Node && (this.host.nativeElement as HTMLElement).contains(event.target))) this.open.set(false);
  }

  private set(value: Iso, reformat: boolean) {
    this.value.set(value);
    if (reformat) this.text.set(this.format(value));
    this.onChange(value);
    // The "needs attention" outline clears when the field changes, as it does for a native input.
    this.box()?.dispatchEvent(new Event('change'));
  }

  private box() {
    return (this.host.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input');
  }

  private focusDay() {
    (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>(`.day[data-date="${this.focused()}"]`)?.focus();
  }
}
