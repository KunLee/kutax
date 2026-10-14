import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ReceiptReview } from './receipt-review';

describe('receipt review', () => {
  it('offers to undo edits and puts every field back as it was saved', async () => {
    TestBed.configureTestingModule({ imports: [ReceiptReview], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ReceiptReview);
    fixture.componentRef.setInput('id', 'd1');
    fixture.detectChanges();
    http.expectOne('/api/documents/d1/review').flush({
      document: { id: 'd1', status: 'NeedsReview', scanStatus: 'Clean', originalFileName: 'receipt.pdf', contentType: 'application/pdf' },
      transaction: { id: 't1', version: 'v1', status: 'Draft', merchant: 'Synthetic Shop', date: '2026-07-03', amount: 71.9, gst: null, notes: '', originalCurrency: null, originalAmount: null, conversionSource: null },
      candidates: [], job: null, history: [], possibleDuplicate: false,
    });
    http.match(() => true).forEach((request) => request.flush(null));
    await fixture.whenStable();
    fixture.detectChanges();
    const review = fixture.componentInstance;
    const undo = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((b) => b.textContent?.includes('Undo changes'));
    expect(undo()).toBeUndefined();
    // Forms connect their fields a moment after rendering.
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    // Typed by the person, as in the browser.
    const type = (selector: string, value: string) => { const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(selector)!; input.value = value; input.dispatchEvent(new Event('input')); };
    type('#r-date', '30/07/2026');
    type('#r-amount', '7.19');
    await fixture.whenStable();
    fixture.detectChanges();
    expect([review.date, review.amount]).toEqual(['2026-07-30', 7.19]);
    expect(undo()).toBeDefined();
    undo()!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect([review.date, review.amount, review.merchant]).toEqual(['2026-07-03', 71.9, 'Synthetic Shop']);
    expect(undo()).toBeUndefined();
  });

  it('confirms and claims in one press, asking for the conditions first', async () => {
    TestBed.configureTestingModule({ imports: [ReceiptReview], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ReceiptReview);
    fixture.componentRef.setInput('id', 'd1');
    fixture.detectChanges();
    const transaction = { id: 't1', version: 'v1', status: 'Draft', merchant: 'Anysphere', date: '2025-09-01', amount: 29.99, gst: null, notes: '', originalCurrency: null, originalAmount: null, conversionSource: null, suggestedCategory: 'SoftwareSubscriptions' };
    const review = (t: object) => ({ document: { id: 'd1', status: 'NeedsReview', scanStatus: 'Clean', originalFileName: 'r.pdf', contentType: 'application/pdf' }, transaction: t, candidates: [], job: null, history: [], possibleDuplicate: false });
    http.expectOne('/api/documents/d1/review').flush(review(transaction));
    await Promise.resolve();
    http.match('/api/tax-rules').forEach((r) => r.flush({ rules: [{ category: 'SoftwareSubscriptions', label: 'Software', financialYearStart: 2025, claimTypes: [], wholeAmountOnly: false, declaration: 'Each item cost $300 or less.', isActive: true }], reviews: [] }));
    http.match(() => true).forEach((r) => r.flush(null));
    await fixture.whenStable();
    fixture.detectChanges();
    const component = fixture.componentInstance;
    expect(component.category()).toBe('SoftwareSubscriptions'); // the AI's suggestion, preselected
    component.workUse = 80;

    // Without the conditions confirmed, the details are saved but nothing is claimed.
    void component.save(true);
    http.expectOne('/api/transactions/t1').flush({});
    await new Promise((resolve) => setTimeout(resolve));
    http.expectOne('/api/documents/d1/review').flush(review({ ...transaction, status: 'Confirmed', version: 'v2' }));
    await new Promise((resolve) => setTimeout(resolve));
    http.expectNone('/api/transactions/t1/claim');

    component.declared = true;
    const done = component.save(true);
    http.expectOne('/api/transactions/t1').flush({});
    await new Promise((resolve) => setTimeout(resolve));
    http.expectOne('/api/documents/d1/review').flush(review({ ...transaction, status: 'Confirmed', version: 'v3' }));
    await new Promise((resolve) => setTimeout(resolve));
    const claim = http.expectOne('/api/transactions/t1/claim');
    expect(claim.request.body).toEqual({ version: 'v3', category: 'SoftwareSubscriptions', workUsePercent: 80, claimType: null, declared: true });
    claim.flush({ approved: true, status: 'Approved', deductibleAmount: 23.99 });
    await new Promise((resolve) => setTimeout(resolve));
    http.match('/api/documents/d1/review').forEach((r) => r.flush(review({ ...transaction, status: 'Confirmed', version: 'v4' })));
    http.match(() => true).forEach((r) => r.flush([]));
    await done;
  });
});
