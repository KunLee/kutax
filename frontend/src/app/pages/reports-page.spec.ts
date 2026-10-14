import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { signal } from '@angular/core';
import { ReportsPage } from './reports-page';
import { Session } from '../core/session';

const months = Array.from({ length: 12 }, (_, i) => ({ month: `${i < 6 ? 2025 : 2026}-${String(((i + 6) % 12) + 1).padStart(2, '0')}`, spent: i === 2 ? 100 : 0, claimed: 0, receipts: i === 2 ? 1 : 0 }));
const report = {
  year: 2025,
  totals: { receipts: 3, spent: 300, claimed: 900, ready: 100, skipped: 0, toCheck: 0, expenses: 2 },
  claims: { approved: 1, ready: 1, waiting: 0, advice: 0, excluded: 0 },
  months, categories: [{ category: 'UnionFees', claimed: 900, ready: 100, spent: 1000, expenses: 2 }],
  merchants: [{ merchant: 'Synthetic Union', spent: 300, receipts: 3 }], currencies: [], years: [{ year: 2025, claimed: 900, spent: 1000 }],
  missing: [{ merchant: 'Synthetic Telco', months: 10, typical: 60, category: 'PhoneInternet', missing: ['2026-03', '2026-04'] }],
  compare: { previousYear: 2024, items: [{ category: 'SelfEducation', lastYear: 500, thisYear: 0 }] },
  rates: { brackets: [{ from: 0, to: 18200, rate: 0 }, { from: 18200, to: 45000, rate: 0.16 }, { from: 45000, to: 135000, rate: 0.3 }], medicareLevy: 0.02, source: 'https://www.ato.gov.au/' },
};

describe('reports page', () => {
  beforeEach(() => localStorage.clear());
  async function setup() {
    TestBed.configureTestingModule({ imports: [ReportsPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
      { provide: Session, useValue: { account: signal({ id: 'u', financialYearStart: 2025 }) } }] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ReportsPage);
    fixture.detectChanges();
    await fixture.whenStable();
    http.expectOne('/api/reports/2025').flush(report);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('points out missing months and last year’s claims, and estimates the tax saved only once an income band is chosen', async () => {
    const fixture = await setup();
    const page = fixture.componentInstance;
    const text = () => (fixture.nativeElement as HTMLElement).textContent!;
    expect(text()).toContain('Synthetic Telco');
    expect(text()).toContain('Mar');
    expect(text()).toContain('None yet');
    expect(page.saving()).toBeNull();
    page.setBracket('2');
    // $1,000 of claimed and ready deductions at 30% plus the 2% levy.
    expect(page.saving()).toBe(320);
    expect(page.ratePercent()).toBe('32%');
    page.setBracket('0');
    expect(page.saving()).toBe(0);
    expect(localStorage.getItem('kutax.reports.bracket')).toBe('0');
  });

  it('opens the matching receipts when a month or kind of expense is chosen', async () => {
    const fixture = await setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const page = fixture.componentInstance;
    if (page.touch) { page.month(2, true); }
    page.month(2, true);
    expect(navigate).toHaveBeenCalledWith(['/review'], { queryParams: { year: 2025, month: '2025-09' } });
    if (page.touch) { page.kind('UnionFees', true); }
    page.kind('UnionFees', true);
    expect(navigate).toHaveBeenLastCalledWith(['/review'], { queryParams: { year: 2025, category: 'UnionFees' } });
  });
});
