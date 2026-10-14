import { effect, inject, Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { I18n } from './i18n';

/** Route titles are written in English and shown in the chosen language, updating when it changes. */
@Injectable({ providedIn: 'root' })
export class TranslatedTitle extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly i18n = inject(I18n);
  private current = 'KuTax';

  constructor() {
    super();
    effect(() => this.title.setTitle(this.i18n.t(this.current)));
  }

  override updateTitle(snapshot: RouterStateSnapshot) {
    this.current = this.buildTitle(snapshot) ?? 'KuTax';
    this.title.setTitle(this.i18n.t(this.current));
  }
}
