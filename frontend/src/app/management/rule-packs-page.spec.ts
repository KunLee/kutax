import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { RulePacksPage } from './rule-packs-page';

const field = (overrides: object) => ({ category: 'ToolsEquipment', field: 'label', previous: 'Tools and equipment', proposed: 'Tools and equipment', changed: false, quote: null, source: null, quoteVerified: null, ...overrides });

describe('rule packs page', () => {
  it('shows changed figures with their verified quotes and only allows activation without errors', async () => {
    TestBed.configureTestingModule({ imports: [RulePacksPage], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RulePacksPage);
    document.body.appendChild(fixture.nativeElement);
    fixture.componentRef.setInput('id', 'p1');
    fixture.detectChanges();
    await fixture.whenStable();
    const report = {
      errors: [], warnings: ['PhoneInternet: two readings disagree on the label (D5 E vs D5 F).'], changedFields: 1, comparedWith: 'the FY 2025–26 rules in force',
      fields: [
        field({}),
        field({ field: 'parameters.maxItemCost', previous: '300', proposed: '400', changed: true, quote: 'costing $400 or less', source: 'instructions', quoteVerified: true }),
      ],
    };
    http.expectOne('/api/rule-packs/p1').flush({ id: 'p1', financialYearStart: 2026, status: 'Draft', origin: 'AiDraft', createdAt: '2027-06-20T00:00:00Z', decidedAt: null, errorCode: null, errors: 0, warnings: 1, changed: 1, note: null, pack: { sources: [] }, report });
    await fixture.whenStable();
    fixture.detectChanges();
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('FY 2026–27');
    expect(text).toContain('Limit: maxItemCost');
    expect(text).toContain('✓ found in the official text');
    expect(text).toContain('two readings disagree');
    expect(text).not.toContain('Name'); // unchanged fields stay hidden by default
    // The button stays usable; without the tick it points at the confirmation instead of doing anything.
    const page = fixture.componentInstance;
    void page.activate({ id: 'p1' } as never);
    fixture.detectChanges();
    expect(document.activeElement?.id).toBe('pack-confirm');
    expect(document.getElementById('pack-confirm')?.classList.contains('needs-attention')).toBe(true);
    page.confirm = true;
    void page.activate({ id: 'p1' } as never);
    http.expectOne('/api/rule-packs/p1/activate').flush(null);
    await new Promise((resolve) => setTimeout(resolve));
    http.match('/api/rule-packs/p1').forEach((r) => r.flush({ id: 'p1', financialYearStart: 2026, status: 'Active', origin: 'AiDraft', createdAt: '2027-06-20T00:00:00Z', decidedAt: null, errorCode: null, errors: 0, warnings: 0, changed: 1, note: null, retryAt: null, pack: null, report: null }));
    fixture.nativeElement.remove();
    http.verify();
  });
});
