import { afterNextRender, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Session, yearLabel } from '../core/session';
import { Icon } from '../core/icon';
import { AnnualPreparation } from '../claims/annual-preparation';
import { TaxExports } from '../claims/tax-exports';
import { TranslatePipe } from '../core/i18n';

@Component({
  selector: 'app-summary-page',
  imports: [TranslatePipe, Icon, AnnualPreparation, TaxExports],
  template: `
    @if (session.account(); as user) {
      <div class="page">
        <section class="page-heading">
          <div>
            <span class="eyebrow">{{ 'Tax summary · {year}' | t: { year: label(user.financialYearStart) } }}</span>
            <h1>{{ 'Your year, neatly sorted.' | t }}</h1>
            <p>{{ 'Limited employment deductions, calculated by reviewed rules for this year. You approve every claim before it’s used.' | t }}</p>
          </div>
          <button class="btn btn--primary" type="button" (click)="toPack()"><app-icon name="download" [size]="18" />{{ 'Tax pack' | t }}</button>
        </section>
        <app-annual-preparation [year]="user.financialYearStart" [admin]="user.isAdministrator" />
        <app-tax-exports id="tax-pack" [year]="user.financialYearStart" />
      </div>
    }
  `,
})
export class SummaryPage {
  readonly session = inject(Session);
  readonly label = yearLabel;
  constructor() {
    // "New tax pack" in History lands straight on the tax pack strip.
    const fragment = inject(ActivatedRoute).snapshot.fragment;
    if (fragment === 'tax-pack') afterNextRender(() => setTimeout(() => this.toPack(), 300));
  }
  toPack() {
    document.getElementById('tax-pack')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
}
