import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { InboxPage } from './inbox-page';
import { Session } from '../core/session';

describe('inbox page', () => {
  it('shows counts, review progress and approved deductions only from the API', async () => {
    TestBed.configureTestingModule({
      imports: [InboxPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    TestBed.inject(Session).account.set({
      id: 'owner', email: 'owner@kutax.test', displayName: 'Alex Synthetic', isAdministrator: false, mustChangePassword: false,
      financialYearStart: 2025, financialYearLabel: 'FY 2025–26', financialYearFrom: '2025-07-01', financialYearTo: '2026-06-30',
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(InboxPage);
    fixture.detectChanges();
    http.expectOne('/api/documents/?page=1').flush({
      items: [{ id: 'a', originalFileName: 'union.pdf', relativePath: 'union.pdf', contentType: 'application/pdf', size: 1, status: 'NeedsReview',
        createdAt: '2025-08-02T00:00:00Z', reviewState: 'NeedsReview', merchant: 'Synthetic Union', date: '2025-08-01', amount: 42.5 }],
      total: 5, page: 1, pageSize: 50, counts: { total: 5, needsReview: 2, processing: 1, confirmed: 1, blocked: 1 },
    });
    http.expectOne('/api/tax-years/2025/claims').flush({ approvedTotal: 110 });
    http.expectOne('/api/tax-years/2025/exports').flush([]);
    // The quick-check numbers come from the quick review itself, so the two pages always agree.
    http.match('/api/quick-review/2025').forEach((request) => request.flush({ items: [{}, {}], needsCheck: 1 }));
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    await fixture.whenStable();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('G’day, Alex');
    // One of the four readable receipts is confirmed; the blocked file is excluded.
    expect(text).toContain('25% reviewed');
    expect(text).toContain('$110.00');
    expect(text).toContain('Synthetic Union');
    expect(text).toContain('1 being read');
    // One next step: two receipts read clearly can be approved together.
    const next: HTMLElement = fixture.nativeElement.querySelector('.next-step');
    expect(next.textContent).toContain('2 receipts are ready to approve together');
    expect(next.textContent).toContain('Step 2 of 4');
    expect(next.querySelector('a')?.getAttribute('href')).toBe('/review/quick');
    expect(fixture.nativeElement.querySelector('a.stat--link strong')?.textContent).toContain('2');
    expect(TestBed.inject(Session).counts()?.needsReview).toBe(2);
    http.verify();
  });
});
