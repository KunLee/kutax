import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { actAs, actingFor, navigation } from './sharing';

describe('acting for someone', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([actAs])), provideHttpClientTesting()] });
  });

  it('sends act-as on data requests only, and goes back to your own records when access has ended', async () => {
    const http = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    void firstValueFrom(http.get('/api/documents/'));
    expect(backend.expectOne('/api/documents/').request.headers.has('X-KuTax-Act-As')).toBe(false);

    localStorage.setItem('kutax.actingFor', JSON.stringify({ ownerId: 'owner-1', name: 'Li Hua', email: 'li@kutax.test' }));
    void firstValueFrom(http.get('/api/tax-years/2025/claims'));
    expect(backend.expectOne('/api/tax-years/2025/claims').request.headers.get('X-KuTax-Act-As')).toBe('owner-1');
    // Your own account and sharing are never acted on for someone else.
    void firstValueFrom(http.get('/api/sharing'));
    expect(backend.expectOne('/api/sharing').request.headers.has('X-KuTax-Act-As')).toBe(false);
    void firstValueFrom(http.get('/api/auth/session'));
    expect(backend.expectOne('/api/auth/session').request.headers.has('X-KuTax-Act-As')).toBe(false);

    const assign = vi.spyOn(navigation, 'go').mockImplementation(() => undefined);
    const ended = firstValueFrom(http.get('/api/documents/')).catch((error) => error.status);
    backend.expectOne('/api/documents/').flush({ code: 'DelegationEnded' }, { status: 403, statusText: 'Forbidden' });
    expect(await ended).toBe(403);
    expect(actingFor()).toBeNull();
    expect(assign).toHaveBeenCalledWith('/');
    backend.verify();
  });
});
