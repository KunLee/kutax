import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '../core/icon';
import { links, myTaxOtherWorkExpenses } from '../core/links';
import { TranslatePipe } from '../core/i18n';

// The return people lodge is for the last completed 1 July – 30 June year.
const lastCompletedYear = (today = new Date()) => today.getFullYear() - (today.getMonth() >= 6 ? 1 : 2);

@Component({
  selector: 'app-help-page',
  imports: [TranslatePipe, RouterLink, Icon],
  template: `
    <div class="page">
      <section class="page-heading">
        <div>
          <span class="eyebrow">{{ 'Help' | t }}</span>
          <h1>{{ 'How KuTax works' | t }}</h1>
          <p>{{ 'KuTax keeps your receipts organised all year and turns the ones you can claim into figures you can copy into myTax. You stay in charge of every number.' | t }}</p>
        </div>
      </section>

      <section class="card" aria-labelledby="steps-title">
        <h2 id="steps-title">{{ 'Four steps to tax time' | t }}</h2>
        <ol class="steps">
          <li><span class="thumb tone-blue"><app-icon name="upload" /></span><div><strong>{{ 'Add receipts as you go' | t }}</strong><p>{{ 'Snap a photo, upload PDFs or drop a whole folder on the' | t }} <a routerLink="/">{{ 'Inbox' | t }}</a>{{ '. Each file is checked for malware and stored privately.' | t }}</p></div></li>
          <li><span class="thumb tone-orange"><app-icon name="clock" /></span><div><strong>{{ 'Check the details' | t }}</strong><p>{{ 'In' | t }} <a routerLink="/review">{{ 'Review' | t }}</a>{{ ', compare the merchant, date and amount with the original and confirm. If AI reading is on, KuTax fills these in for you to check.' | t }}</p></div></li>
          <li><span class="thumb tone-purple"><app-icon name="spark" /></span><div><strong>{{ 'Approve in one go' | t }}</strong><p>{{ 'In' | t }} <a routerLink="/review/quick">{{ 'Quick review' | t }}</a>{{ ', KuTax groups clearly read receipts by what they look like. Fix any category, set the work-use share, confirm each group’s conditions once and approve them together. Anything unusual stays in the one-by-one review.' | t }}</p></div></li>
          <li><span class="thumb tone-mint"><app-icon name="download" /></span><div><strong>{{ 'Prepare your tax pack' | t }}</strong><p>{{ 'Freeze your approved figures into a tax pack with a summary, a myTax entry guide, a spreadsheet and your receipts. Find every year’s pack in' | t }} <a routerLink="/history">{{ 'History' | t }}</a>.</p></div></li>
        </ol>
      </section>

      <div class="grid section">
        <section class="card" aria-labelledby="covers-title">
          <h2 id="covers-title">{{ 'What KuTax can calculate' | t }}</h2>
          <ul class="ticks">
            <li><app-icon name="check" [size]="18" /><span><strong>{{ 'Self-education' | t }}</strong> {{ 'for your current job (D4) — not Commonwealth supported places or HELP repayments' | t }}</span></li>
            <li><app-icon name="check" [size]="18" /><span><strong>{{ 'Work clothing' | t }}</strong>{{ ': uniforms, protective and occupation-specific clothing (D3)' | t }}</span></li>
            <li><app-icon name="check" [size]="18" /><span><strong>{{ 'Tools, equipment, software and subscriptions' | t }}</strong> {{ 'up to $300 an item, mainly for work (D5)' | t }}</span></li>
            <li><app-icon name="check" [size]="18" /><span><strong>{{ 'Phone and internet' | t }}</strong> {{ 'bills, work-use share (D5)' | t }}</span></li>
            <li><app-icon name="check" [size]="18" /><span><strong>{{ 'Union fees and professional memberships' | t }}</strong> (D5)</span></li>
            <li><app-icon name="check" [size]="18" /><span><strong>{{ 'Gifts and donations' | t }}</strong> {{ 'of $2 or more to registered charities (D9)' | t }}</span></li>
            <li><app-icon name="check" [size]="18" /><span><strong>{{ 'Tax agent fees' | t }}</strong> {{ 'and managing your tax affairs (D10)' | t }}</span></li>
          </ul>
          <p class="small muted">{{ 'For FY 2024–25 and 2025–26. Each category follows the ATO’s conditions, which you confirm when approving.' | t }}</p>
          <h3>{{ 'Not calculated yet' | t }}</h3>
          <p class="small">{{ 'Working from home, car and travel, items over $300 (they decline in value over several years), investments and income. You can still store these receipts in KuTax — they show as' | t }} <em>{{ 'Needs advice' | t }}</em> {{ 'and stay out of your approved totals. Check the' | t }} <a [href]="links.deductionsYouCanClaim" target="_blank" rel="noopener">{{ 'ATO’s deductions guide' | t }}</a> {{ 'or a' | t }} <a [href]="links.taxAgentRegister" target="_blank" rel="noopener">{{ 'registered tax agent' | t }}</a>.</p>
        </section>
        <section class="card" aria-labelledby="dates-title">
          <h2 id="dates-title">{{ 'Good to know' | t }}</h2>
          <dl class="facts">
            <div><dt>{{ 'Lodging yourself' | t }}</dt><dd>{{ 'Due 31 October each year (the next business day if it falls on a weekend). A registered tax agent may be able to lodge later.' | t }}</dd></div>
            <div><dt>{{ 'Over $300 of work expenses?' | t }}</dt><dd>{{ 'You need written evidence such as receipts for all of them.' | t }}</dd></div>
            <div><dt>{{ 'Prefilled amounts' | t }}</dt><dd>{{ 'Union and membership fees from your income statement may already be in myTax. Don’t claim them twice.' | t }}</dd></div>
            <div><dt>{{ 'Keep your records' | t }}</dt><dd>{{ 'Generally for five years from when you lodge. KuTax keeps the originals for you.' | t }}</dd></div>
          </dl>
          <p class="small"><a [href]="links.recordsToKeep" target="_blank" rel="noopener">{{ 'ATO: records you need to keep' | t }}</a></p>
        </section>
      </div>

      <section class="card section" aria-labelledby="links-title">
        <h2 id="links-title">{{ 'Useful official links' | t }}</h2>
        <ul class="links">
          @for (link of officialLinks; track link.url) {
            <li><a [href]="link.url" target="_blank" rel="noopener"><span><strong>{{ link.title | t }}</strong><small>{{ link.detail | t }}</small></span><app-icon name="arrow" [size]="18" /><span class="sr-only"> {{ '(opens in a new tab)' | t }}</span></a></li>
          }
        </ul>
      </section>

      <section class="card section" aria-labelledby="faq-title">
        <h2 id="faq-title">{{ 'Questions' | t }}</h2>
        @for (item of faq; track item.q) {
          <details><summary>{{ item.q | t }}</summary><p class="small">{{ item.a | t }}</p></details>
        }
      </section>
    </div>
  `,
  styles: `
    .steps { display: grid; gap: 14px; list-style: none; margin: 14px 0 0; padding: 0; }
    .steps li { align-items: flex-start; display: flex; gap: 14px; }
    .steps p { color: var(--ink-soft); margin: 2px 0 0; }
    .grid { display: grid; gap: 20px; grid-template-columns: 1fr 1fr; }
    .ticks { display: grid; gap: 8px; list-style: none; margin: 8px 0 12px; padding: 0; }
    .ticks li { align-items: flex-start; display: flex; font-weight: 400; gap: 8px; margin-bottom: 6px; }
    .ticks app-icon { color: var(--mint-ink); flex: none; margin-top: 2px; }
    .ticks app-icon { color: var(--mint-ink); }
    h3 { font-size: 15px; margin: 16px 0 4px; }
    .facts { display: grid; gap: 12px; margin: 8px 0 12px; }
    .facts dt { font-weight: 800; }
    .facts dd { color: var(--ink-soft); font-size: 14px; line-height: 1.5; margin: 2px 0 0; }
    .links { display: grid; gap: 8px; grid-template-columns: 1fr 1fr; list-style: none; margin: 8px 0 0; padding: 0; }
    .links a { align-items: center; border: 1px solid var(--line); border-radius: 16px; color: var(--ink); display: flex; font-weight: 400; gap: 10px;
      justify-content: space-between; min-height: 64px; padding: 10px 14px; text-decoration: none; }
    .links a:hover { background: var(--purple-soft); }
    .links strong, .links small { display: block; }
    .links small { color: var(--ink-soft); font-size: 13px; margin-top: 2px; }
    details { border-top: 1px solid var(--line); }
    details p { margin: 0 0 12px; }
    @media (max-width: 700px) { .grid, .links { grid-template-columns: 1fr; } }
  `,
})
export class HelpPage {
  readonly links = links;
  readonly officialLinks = [
    { title: 'Sign in to myGov', detail: 'Where you lodge with myTax', url: links.myGov },
    { title: 'Lodge with myTax', detail: 'ATO step-by-step guide', url: links.lodgeWithMyTax },
    { title: 'Other work-related expenses', detail: 'myTax instructions for this year', url: myTaxOtherWorkExpenses(lastCompletedYear()) },
    { title: 'Union fees and memberships', detail: 'What the ATO lets you claim', url: links.unionFees },
    { title: 'Preparing your tax return', detail: 'What to have ready before you start', url: links.preparingReturn },
    { title: 'Records you need to keep', detail: 'How long and what counts', url: links.recordsToKeep },
    { title: 'ATO app: myDeductions', detail: 'The ATO’s own record-keeping tool', url: links.myDeductions },
    { title: 'Find a registered tax agent', detail: 'Tax Practitioners Board register', url: links.taxAgentRegister },
  ];
  readonly faq = [
    { q: 'Does KuTax lodge my tax return?', a: 'No. KuTax prepares your figures and evidence. You enter them in myTax yourself, or give the tax pack to your tax agent.' },
    { q: 'Is this tax advice?', a: 'No. KuTax applies a small set of reviewed ATO rules to the details you confirm. If you’re unsure whether something is claimable, check the ATO guidance or ask a registered tax agent.' },
    { q: 'Who can see my receipts?', a: 'Only you. Files are stored privately and served only to your signed-in account. Administrators manage accounts but can’t open your receipts in the app.' },
    { q: 'Does AI read my receipts?', a: 'Only if you said yes when KuTax asked (or turned it on in Settings → AI). You can change that any time, and you always check the details yourself.' },
    { q: 'Why does a receipt say “Needs advice”?', a: 'KuTax calculates the categories listed above. Anything else — such as car, travel, working from home, or items over $300 — is kept with your records but left out of your approved totals.' },
    { q: 'A receipt isn’t for my tax return. What do I do?', a: 'Tap “Skip — not for my tax return” and, if you like, say why (personal, not deductible, duplicate, other year). It stays in your records but out of your claims. Skipped by mistake? Tap Undo straight away, or find it under Review → Skipped and restore it.' },
    { q: 'I uploaded the wrong file. Can I remove it?', a: 'Yes: open it and choose “Delete this file”. That removes the original and anything read or split from it. Only do this for mistakes — keep real receipts for five years after you lodge. Files in a tax pack or behind an approved claim can’t be deleted; skip them instead.' },
    { q: 'Why can’t AI approve my claims for me?', a: 'Your tax return is your responsibility, so you approve every claim. KuTax keeps it to one tap: Quick review confirms, allocates and approves a whole group of clearly read receipts at once.' },
    { q: 'My iPhone photo was rejected.', a: 'KuTax accepts PDF, JPG and PNG. Uploading from Safari on your phone usually converts photos automatically; from a computer, export HEIC photos as JPG first.' },
    { q: 'I changed a receipt after preparing a tax pack.', a: 'Your tax pack doesn’t change. Approve the updated claim and prepare a new tax pack; History keeps both versions.' },
  ];
}
