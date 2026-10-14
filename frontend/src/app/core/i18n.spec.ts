import { TestBed } from '@angular/core/testing';
import { applyStoredLanguage, I18n } from './i18n';

describe('i18n', () => {
  beforeEach(() => localStorage.clear());

  it('shows English keys unchanged and fills parameters', () => {
    localStorage.setItem('kutax.language', 'en');
    const i18n = TestBed.inject(I18n);
    expect(i18n.t('{n} receipts', { n: 3 })).toBe('3 receipts');
  });

  it('switches to Chinese, keeps the choice and translates server messages with names in them', async () => {
    localStorage.setItem('kutax.language', 'zh');
    await applyStoredLanguage();
    const i18n = TestBed.inject(I18n);
    expect(document.documentElement.lang).toBe('zh-CN');
    expect(i18n.t('{n} receipts', { n: 3 })).toBe('3 张收据');
    expect(i18n.t('Confirm the conditions for Work clothing.')).toBe('请确认“工作服装”的条件。');
    expect(i18n.t('Expense 2: Work use must be 0–100%.')).toBe('费用 2：工作用途必须在 0–100% 之间。');
    expect(i18n.t('Something new from the server')).toBe('Something new from the server');
    i18n.set('en');
    expect(i18n.t('Approve all {n}', { n: 4 })).toBe('Approve all 4');
    expect(localStorage.getItem('kutax.language')).toBe('en');
  });
});
