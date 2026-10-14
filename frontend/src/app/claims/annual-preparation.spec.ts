import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AnnualPreparation } from './annual-preparation';

const row = (overrides: object) => ({
  allocationId: crypto.randomUUID(), allocationVersion: 'v', transactionVersion: 't', documentId: 'd', merchant: 'Synthetic', date: '2025-08-01',
  description: 'Fee', category: 'UnionFees', expenseAmount: 100, workUsePercent: 100, workReimbursement: 0, basis: '', status: 'Approved',
  deductibleAmount: 100, reason: '', ruleVersion: null, approvedAt: null, evidence: [], ...overrides,
});

describe('annual preparation', () => {
  it('builds the deduction mix from approved and awaiting deductions only', async () => {
    TestBed.configureTestingModule({
      imports: [AnnualPreparation],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AnnualPreparation);
    fixture.componentRef.setInput('year', 2025);
    fixture.detectChanges();
    http.expectOne('/api/tax-years/2025/claims').flush({
      financialYearStart: 2025, issues: [], approvedTotal: 3842.6, candidateTotal: 40, excludedExpenseTotal: 15, unassessedExpenseTotal: 20, entryAmount: 3842,
      rows: [
        row({ deductibleAmount: 3842.6 }),
        row({ category: 'ProfessionalMembership', status: 'Candidate', deductibleAmount: 40 }),
        row({ category: 'Excluded', status: 'Excluded', deductibleAmount: 15 }),
        row({ category: 'Unsupported', status: 'Unsupported', deductibleAmount: null }),
      ],
    });
    http.expectOne('/api/tax-rules').flush({ rules: [], reviews: [] });
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;
    expect(component.mix().map((item) => [item.label, item.amount])).toEqual([['Union fees', 3842.6], ['Professional memberships', 40]]);
    expect(component.dollars(3842.6)).toBe('$3,842');
    expect(component.cents(3842.6)).toBe('60');
    expect(component.pending()).toBe(1);
    expect(component.ready()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('1 waiting for your approval');
    http.verify();
  });

  it('shows three to-dos in the summary tile and every one full width below', async () => {
    TestBed.configureTestingModule({
      imports: [AnnualPreparation],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AnnualPreparation);
    fixture.componentRef.setInput('year', 2025);
    fixture.detectChanges();
    http.expectOne('/api/tax-years/2025/claims').flush({
      financialYearStart: 2025, approvedTotal: 0, candidateTotal: 40, excludedExpenseTotal: 0, unassessedExpenseTotal: 0, entryAmount: 0,
      issues: [{ documentId: 'd3', description: 'Receipt has no reviewed date; its financial year is unknown.', amount: null },
        { documentId: 'd4', description: 'Unallocated receipt balance: Synthetic Shop', amount: 12 }],
      rows: [
        row({ status: 'Candidate', deductibleAmount: 40 }),
        row({ description: 'Phone bill', status: 'NeedsEvidence', deductibleAmount: null }),
        row({ description: 'Course fee', status: 'NeedsReview', deductibleAmount: null }),
      ],
    });
    http.expectOne('/api/tax-rules').flush({ rules: [], reviews: [] });
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    await fixture.whenStable();
    const page: HTMLElement = fixture.nativeElement;
    expect(fixture.componentInstance.todos().map((item) => item.kind)).toEqual(['approve', 'row', 'row', 'issue', 'issue']);
    expect(page.querySelectorAll('.todo .todo-list > li').length).toBe(3);
    expect(page.querySelector('.todo .todo-more')?.textContent).toContain('And 2 more');
    expect(page.querySelectorAll('#all-todos .todo-grid > li').length).toBe(5);
    // The breakdown filters by status, offering only groups that have claims, with their counts.
    const component = fixture.componentInstance;
    expect(component.statusFilters().map((f) => [f.key, f.count])).toEqual([['all', 3], ['Candidate', 1], ['fix', 2]]);
    component.showStatus('fix');
    fixture.detectChanges();
    expect(page.querySelectorAll('#claims li.claim').length).toBe(2);
    expect(page.querySelector('.filters [aria-pressed="true"]')?.textContent).toContain('Needs details 2');
    http.verify();
  });
});
