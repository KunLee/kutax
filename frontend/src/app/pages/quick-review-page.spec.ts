import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Session } from '../core/session';
import { QuickReviewPage } from './quick-review-page';

const rule = (category: string, label: string, overrides: object = {}) => ({
  category, label, section: 'Other work-related expenses', paperReference: 'D5 E', conditions: 'Synthetic conditions', declaration: `Synthetic ${label} declaration`,
  claimTypes: [], maxItemCost: null, workUseAbove: null, wholeAmountOnly: false, sourceUrl: 'https://www.ato.gov.au/', version: 'AU-2025-' + category + '-v1', reviewed: true, defaultWorkUse: 100, defaultClaimType: null, ...overrides,
});
const item = (id: string, merchant: string, amount: number, suggestedCategory: string | null) => ({
  id, version: 'v-' + id, documentId: 'd-' + id, fileName: merchant + '.pdf', merchant, date: '2025-09-01', amount, gst: null, status: 'Draft', suggestedCategory, note: null,
});

describe('quick review', () => {
  it('groups suggestions, requires each group’s conditions, and approves the batch in one request', async () => {
    TestBed.configureTestingModule({ imports: [QuickReviewPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const session = TestBed.inject(Session);
    session.account.set({ id: 'u', email: 'synthetic@kutax.test', financialYearStart: 2025, financialYearFrom: '2025-07-01', financialYearTo: '2026-06-30', displayName: 'Alex', isAdministrator: false, mustChangePassword: false } as never);
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(QuickReviewPage);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne('/api/quick-review/2025').flush({
      year: 2025, needsCheck: 3,
      items: [item('a', 'University of Sydney', 1450, 'SelfEducation'), item('b', 'Anysphere', 29.99, 'SoftwareSubscriptions'), item('c', 'Woolworths', 88.1, 'Personal'), item('d', 'Mystery Pty', 10, null)],
      rules: [rule('SelfEducation', 'Self-education', { section: 'Work-related self-education expenses', paperReference: 'D4 D', claimTypes: ['K', 'I'] }),
        rule('SoftwareSubscriptions', 'Software and subscriptions', { workUseAbove: 50, defaultWorkUse: 80 })],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    const page = fixture.componentInstance;
    expect(page.groups().map((g) => [g.key, g.rows.length, g.selected.length])).toEqual([['SelfEducation', 1, 1], ['SoftwareSubscriptions', 1, 1], ['Personal', 1, 1], ['', 1, 0]]);
    expect(page.selectedCount()).toBe(3);
    expect(page.missing()).toEqual({ id: 'q-claim-SelfEducation', message: 'Choose the claim type for Self-education.' });
    expect(fixture.nativeElement.textContent).toContain('3 receipts need a closer look');

    page.setClaimType('SelfEducation', 'K');
    page.declare('SelfEducation', true);
    expect(page.missing()?.id).toBe('q-declare-SoftwareSubscriptions');
    page.declare('SoftwareSubscriptions', true);
    expect(page.missing()).toBeNull();

    void page.approve();
    const request = http.expectOne('/api/quick-review/2025/approve');
    expect(request.request.body).toEqual({
      confirm: true,
      declarations: ['SelfEducation', 'SoftwareSubscriptions'],
      items: [
        { transactionId: 'a', version: 'v-a', category: 'SelfEducation', workUsePercent: 100, claimType: 'K' },
        { transactionId: 'b', version: 'v-b', category: 'SoftwareSubscriptions', workUsePercent: 80, claimType: null },
        { transactionId: 'c', version: 'v-c', category: 'Personal', workUsePercent: 100, claimType: null },
      ],
    });
    request.flush({ approved: 2, approvedTotal: 1473.99, skipped: 1, notApproved: [] });
    await new Promise((resolve) => setTimeout(resolve));
    http.expectOne('/api/quick-review/2025').flush({ year: 2025, needsCheck: 3, items: [item('d', 'Mystery Pty', 10, null)], rules: [] });
    http.expectOne('/api/documents/?state=review').flush({ items: [], total: 0, counts: null });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.result()?.approved).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('2 claims approved');
    http.verify();
  });
});
