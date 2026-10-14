import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ReviewPage } from './review-page';
import { Toasts } from '../core/toast';
import { Session } from '../core/session';

const page = (items: object[] = []) => ({ items, total: items.length, page: 1, pageSize: 50, counts: { total: 3, needsReview: 1, processing: 0, confirmed: 1, blocked: 0, notClaimed: 1 } });

describe('review page', () => {
  beforeEach(() => sessionStorage.clear());
  it('lists skipped receipts and undoes a skip from the toast', async () => {
    TestBed.configureTestingModule({ imports: [ReviewPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ReviewPage);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne('/api/documents/?state=review').flush(page());
    const review = fixture.componentInstance;

    review.setFilter('skipped');
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne('/api/documents/?state=skipped').flush(page([{ id: 'd1', originalFileName: 'groceries.pdf', relativePath: 'groceries.pdf', contentType: 'application/pdf', size: 1, status: 'NeedsReview',
      createdAt: '2025-09-01T00:00:00Z', reviewState: 'NotClaimed', merchant: 'Synthetic Grocer', date: '2025-09-01', amount: 5, aiRead: true, skipReason: 'Personal' }]));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Skipped · Personal');

    review.showUndo({ transactionId: 't1', version: 'v2', merchant: 'Synthetic Grocer' });
    const toasts = TestBed.inject(Toasts);
    expect(toasts.current()?.text).toContain('Skipped Synthetic Grocer');
    void toasts.act();
    const request = http.expectOne('/api/transactions/t1/dismiss');
    expect(request.request.body).toEqual({ version: 'v2', dismiss: false });
    request.flush({});
    await new Promise((resolve) => setTimeout(resolve));
    http.expectOne('/api/documents/?state=skipped').flush(page());
    await fixture.whenStable();
    fixture.detectChanges();
    expect(toasts.current()?.text).toBe('Brought back to your review queue.');
    toasts.close();
    review.ngOnDestroy();
    http.verify();
  });

  it('opens the next receipt that still needs confirming after one is confirmed', async () => {
    TestBed.configureTestingModule({ imports: [ReviewPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(ReviewPage);
    fixture.componentRef.setInput('id', 'd1');
    fixture.detectChanges();
    await fixture.whenStable();
    const receipt = (id: string, reviewState: string) => ({ id, originalFileName: id + '.pdf', relativePath: id + '.pdf', contentType: 'application/pdf', size: 1, status: 'Ready',
      createdAt: '2025-09-01T00:00:00Z', reviewState, merchant: 'Synthetic ' + id, date: '2025-09-01', amount: 5, aiRead: true });
    // Already confirmed receipts are passed over; the next one waiting is opened.
    http.expectOne('/api/documents/?state=review').flush(page([receipt('d1', 'NeedsReview'), receipt('d2', 'Confirmed'), receipt('d3', 'NeedsReview')]));
    http.match(() => true).forEach((request) => request.flush({}));
    await fixture.whenStable();
    await fixture.componentInstance.afterConfirm('Synthetic d1');
    expect(navigate).toHaveBeenCalledWith('/review/d3');
    expect(TestBed.inject(Toasts).current()?.text).toBe('Confirmed Synthetic d1. Here’s the next one.');
    TestBed.inject(Toasts).close();
    fixture.componentInstance.ngOnDestroy();
  });

  it('searches and sorts on the server, waiting for a pause in typing', async () => {
    TestBed.configureTestingModule({ imports: [ReviewPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ReviewPage);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne('/api/documents/?state=review').flush(page());
    const review = fixture.componentInstance;
    review.setFilter('all');
    review.sort.set('merchant');
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne('/api/documents/?sort=merchant').flush(page());
    review.typed('71');
    review.typed('71.9');
    await new Promise((resolve) => setTimeout(resolve, 350));
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne('/api/documents/?search=71.9&sort=merchant').flush(page());
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No receipts match “71.9”');
    expect(sessionStorage.getItem('kutax.review.sort')).toBe('merchant');
    review.ngOnDestroy();
    http.verify();
  });

  it('limits the list to the chosen financial year and skips several receipts at once', async () => {
    TestBed.configureTestingModule({ imports: [ReviewPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(Session).account.set({ id: 'owner', email: 'owner@kutax.test', displayName: 'Owner', isAdministrator: false, mustChangePassword: false,
      financialYearStart: 2025, financialYearLabel: 'FY 2025–26', financialYearFrom: '2025-07-01', financialYearTo: '2026-06-30' } as never);
    const fixture = TestBed.createComponent(ReviewPage);
    fixture.detectChanges();
    await fixture.whenStable();
    const receipt = (id: string) => ({ id, originalFileName: id + '.pdf', relativePath: id + '.pdf', contentType: 'application/pdf', size: 1, status: 'Ready',
      createdAt: '2025-09-01T00:00:00Z', reviewState: 'NeedsReview', merchant: 'Synthetic ' + id, date: '2025-09-01', amount: 5, aiRead: true });
    http.match('/api/documents/?state=review').forEach((r) => r.flush(page()));
    const review = fixture.componentInstance;
    review.setThisYear(true);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne('/api/documents/?state=review&year=2025').flush(page([receipt('a'), receipt('b'), receipt('c')]));
    await fixture.whenStable();
    review.setSelecting(true);
    review.toggle('a');
    review.toggle('c');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('2 selected');
    const done = review.batch(true, 'Duplicate');
    const request = http.expectOne('/api/documents/skip-batch');
    expect(request.request.body).toEqual({ documentIds: ['a', 'c'], skip: true, reason: 'Duplicate' });
    request.flush({ changed: 1, notChanged: [{ documentId: 'c', reason: 'Still being read.' }] });
    await new Promise((resolve) => setTimeout(resolve));
    http.match('/api/documents/?state=review&year=2025').forEach((r) => r.flush(page()));
    http.match('/api/documents/?state=review').forEach((r) => r.flush(page()));
    await done;
    expect(TestBed.inject(Toasts).current()?.text).toBe('1 skipped.');
    expect(review.notice()).toBe('1 couldn’t change: Still being read.');
    expect(review.selecting()).toBe(false);
    TestBed.inject(Toasts).close();
    review.ngOnDestroy();
  });
  it('opens narrowed to a report item and shows everything again in one tap', async () => {
    TestBed.configureTestingModule({ imports: [ReviewPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([{ path: 'review', component: ReviewPage }])] });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    const review = await harness.navigateByUrl('/review?year=2025&merchant=Synthetic%20Telco', ReviewPage);
    harness.detectChanges();
    await harness.fixture.whenStable();
    // Every matching receipt, whatever its state, within that financial year.
    http.expectOne('/api/documents/?year=2025&merchant=Synthetic+Telco').flush(page());
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Synthetic Telco · FY 2025–26');
    review.clearFocus();
    harness.detectChanges();
    await harness.fixture.whenStable();
    http.expectOne('/api/documents/').flush(page());
    expect(sessionStorage.getItem('kutax.review.focus')).toBeNull();
    review.ngOnDestroy();
  });
});
