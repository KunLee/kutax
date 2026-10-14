import { Component, computed, ElementRef, inject, output, signal, viewChildren } from '@angular/core';
import { Icon } from './icon';
import { Session, yearLabel } from './session';
import { TranslatePipe } from './i18n';

/**
 * The financial-year switcher: a themed list instead of the browser's native one, keyboard friendly
 * (arrows, Home/End, Enter, Escape) and closed by any press outside it.
 */
@Component({
  selector: 'app-year-picker',
  imports: [TranslatePipe, Icon],
  host: { '(document:pointerdown)': 'outside($event)', '(document:keydown.escape)': 'close(true)' },
  template: `
    <button id="financial-year" class="year-select" type="button" aria-haspopup="listbox" [attr.aria-expanded]="open()" aria-controls="year-list"
            [attr.aria-label]="'Financial year: ' + label()" [disabled]="session.busy()" (click)="toggle()" (keydown)="triggerKey($event)">
      <span class="year-select__label">{{ 'Tax year' | t }}</span>
      <strong>{{ label() }}</strong>
      <app-icon name="chevron" [size]="16" />
    </button>
    @if (open()) {
      <ul id="year-list" class="menu year-list" role="listbox" [attr.aria-label]="'Financial year' | t" tabindex="-1" (keydown)="listKey($event)">
        @for (year of session.years(); track year.start; let i = $index) {
          <li #option role="option" tabindex="-1" [id]="'year-' + year.start" [attr.aria-selected]="year.start === selected()"
              [class.selected]="year.start === selected()" (click)="choose(year.start)">
            <span>{{ year.label }}</span>
            <small>{{ 'July {from} – June {to}' | t: { from: year.start, to: year.start + 1 } }}@if (year.current) { · {{ 'This year' | t }} } @else if (year.hasRecords) { · {{ 'Has records' | t }} }</small>
            @if (year.start === selected()) { <app-icon name="check" [size]="16" /> }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host { display: block; position: relative; }
    .year-select { align-items: center; background: var(--white); border: 1px solid var(--line); border-radius: 14px; color: var(--ink); cursor: pointer; display: grid; font: inherit;
      grid-template-columns: 1fr 18px; min-width: 150px; padding: 6px 12px; text-align: left; }
    .year-select:focus-visible { outline: 3px solid var(--purple); outline-offset: 2px; }
    .year-select__label { color: var(--ink-soft); font-family: var(--mono); font-size: 10px; letter-spacing: 0.08em; text-transform: uppercase; }
    .year-select strong { font-size: 15px; font-weight: 800; grid-column: 1; }
    .year-select app-icon { grid-column: 2; grid-row: 1 / span 2; transition: transform .15s; }
    .year-select[aria-expanded='true'] app-icon { transform: rotate(180deg); }
    /* A floating panel over the page (the shell's .menu styles don't reach into this component; themes add their look on top). */
    .year-list { background: var(--white); border: 1px solid var(--line); border-radius: 18px; box-shadow: var(--shadow); display: grid; gap: 2px; left: 0; list-style: none; margin: 0;
      max-height: min(60vh, 420px); overflow-y: auto; padding: 8px; position: absolute; right: auto; top: calc(100% + 8px); width: max(100%, 220px); z-index: 40; }
    .year-list li { align-items: center; border-radius: 10px; cursor: pointer; display: grid; font-weight: 700; gap: 2px 8px; grid-template-columns: 1fr auto; min-height: 44px; padding: 6px 10px; }
    .year-list li small { color: var(--ink-soft); font-size: 11px; font-weight: 600; grid-column: 1; }
    .year-list li app-icon { grid-column: 2; grid-row: 1 / span 2; }
    .year-list li:hover, .year-list li:focus { background: var(--purple-soft); outline: none; }
    .year-list li.selected { background: var(--mint-soft); }
    /* On narrow screens the picker sits near the right edge: open the list leftwards so it stays on screen. */
    @media (max-width: 960px) { .year-select { min-width: 0; } .year-list { left: auto; right: 0; } }
  `,
})
export class YearPicker {
  readonly session = inject(Session);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly options = viewChildren<ElementRef<HTMLElement>>('option');
  readonly changed = output<number>();
  readonly open = signal(false);
  readonly selected = computed(() => this.session.account()?.financialYearStart ?? null);
  readonly label = computed(() => (this.selected() === null ? '' : yearLabel(this.selected()!)));

  toggle() {
    if (this.open()) this.close(false);
    else this.show();
  }

  private show() {
    this.open.set(true);
    // Focus the selected year once the list renders, so arrows and Enter work straight away.
    setTimeout(() => this.focus(Math.max(0, this.session.years().findIndex((y) => y.start === this.selected()))));
  }

  close(returnFocus: boolean) {
    if (!this.open()) return;
    this.open.set(false);
    if (returnFocus) (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>('#financial-year')?.focus();
  }

  outside(event: Event) {
    if (this.open() && !(event.target instanceof Node && (this.host.nativeElement as HTMLElement).contains(event.target))) this.close(false);
  }

  choose(start: number) {
    this.close(true);
    if (start !== this.selected()) this.changed.emit(start);
  }

  triggerKey(event: KeyboardEvent) {
    if (['ArrowDown', 'ArrowUp'].includes(event.key)) {
      event.preventDefault();
      this.show();
    }
  }

  listKey(event: KeyboardEvent) {
    const items = this.options();
    const current = items.findIndex((item) => item.nativeElement === document.activeElement);
    const move: Record<string, number> = { ArrowDown: current + 1, ArrowUp: current - 1, Home: 0, End: items.length - 1 };
    if (event.key in move) {
      event.preventDefault();
      this.focus(Math.min(items.length - 1, Math.max(0, move[event.key])));
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      const year = this.session.years()[current];
      if (year) this.choose(year.start);
    } else if (event.key === 'Tab') {
      this.close(false);
    }
  }

  private focus(index: number) {
    this.options()[index]?.nativeElement.focus();
  }
}
