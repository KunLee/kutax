import { Component, computed, ElementRef, inject, input, model, signal, viewChildren } from '@angular/core';
import { Icon } from './icon';
import { TranslatePipe } from './i18n';

export interface MenuOption { value: string; label: string; }

/**
 * A small themed dropdown (the browser's own list can't be styled to match): a button showing the choice and a
 * floating list. Keyboard: arrows, Home/End, Enter or Space to choose, Escape to close; any press outside closes it.
 */
@Component({
  selector: 'app-select-menu',
  imports: [Icon, TranslatePipe],
  host: { '(document:pointerdown)': 'outside($event)', '(document:keydown.escape)': 'close(true)' },
  template: `
    <button class="trigger" type="button" aria-haspopup="listbox" [attr.aria-expanded]="open()" [attr.aria-label]="(label() | t) + ': ' + (current()?.label ?? '' | t)" (click)="toggle()" (keydown)="triggerKey($event)">
      <span class="caption">{{ label() | t }}</span><strong>{{ current()?.label ?? '' | t }}</strong><app-icon name="chevron" [size]="16" />
    </button>
    @if (open()) {
      <ul class="list" role="listbox" [attr.aria-label]="label() | t" (keydown)="listKey($event)">
        @for (option of options(); track option.value) {
          <li #item role="option" tabindex="-1" [attr.aria-selected]="option.value === value()" [class.selected]="option.value === value()" (click)="choose(option.value)">
            <span>{{ option.label | t }}</span>@if (option.value === value()) { <app-icon name="check" [size]="16" /> }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host { display: inline-block; position: relative; }
    .trigger { align-items: center; background: var(--white); border: 1px solid var(--line); border-radius: 12px; color: var(--ink); cursor: pointer; display: flex; font: inherit; gap: 6px; min-height: 40px; padding: 4px 10px; }
    .trigger:focus-visible { outline: 3px solid var(--purple); outline-offset: 2px; }
    .caption { color: var(--ink-soft); font-size: 12px; }
    .trigger strong { font-size: 13px; font-weight: 800; white-space: nowrap; }
    .trigger[aria-expanded='true'] app-icon { transform: rotate(180deg); }
    .list { background: var(--white); border: 2px solid var(--ink); border-radius: 14px; box-shadow: 4px 4px 0 var(--ink); display: grid; gap: 2px; list-style: none; margin: 0; min-width: 100%;
      padding: 6px; position: absolute; right: 0; top: calc(100% + 6px); width: max-content; z-index: 40; }
    .list li { align-items: center; border-radius: 9px; cursor: pointer; display: flex; font-size: 14px; font-weight: 700; gap: 12px; justify-content: space-between; min-height: 40px; padding: 4px 10px; }
    .list li:hover, .list li:focus { background: var(--purple-soft); outline: none; }
    .list li.selected { background: var(--mint-soft); }
  `,
})
export class SelectMenu {
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly items = viewChildren<ElementRef<HTMLElement>>('item');
  readonly label = input.required<string>();
  readonly options = input.required<MenuOption[]>();
  readonly value = model.required<string>();
  readonly open = signal(false);
  readonly current = computed(() => this.options().find((o) => o.value === this.value()));

  toggle() {
    if (this.open()) { this.close(false); return; }
    this.open.set(true);
    setTimeout(() => this.focus(Math.max(0, this.options().findIndex((o) => o.value === this.value()))));
  }

  close(returnFocus: boolean) {
    if (!this.open()) return;
    this.open.set(false);
    if (returnFocus) (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>('.trigger')?.focus();
  }

  outside(event: Event) {
    if (this.open() && !(event.target instanceof Node && (this.host.nativeElement as HTMLElement).contains(event.target))) this.close(false);
  }

  choose(value: string) {
    this.value.set(value);
    this.close(true);
  }

  triggerKey(event: KeyboardEvent) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') { event.preventDefault(); if (!this.open()) this.toggle(); }
  }

  listKey(event: KeyboardEvent) {
    const items = this.items();
    const current = items.findIndex((item) => item.nativeElement === document.activeElement);
    const move: Record<string, number> = { ArrowDown: current + 1, ArrowUp: current - 1, Home: 0, End: items.length - 1 };
    if (event.key in move) { event.preventDefault(); this.focus(Math.min(items.length - 1, Math.max(0, move[event.key]))); }
    else if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); const option = this.options()[current]; if (option) this.choose(option.value); }
    else if (event.key === 'Tab') this.close(false);
  }

  private focus(index: number) {
    this.items()[index]?.nativeElement.focus();
  }
}
