import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { retryReads } from './retry';

describe('retrying reads', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([retryReads])), provideHttpClientTesting()] });
  });
  afterEach(() => vi.useRealTimers());

  it('waits out a restart for reads but never repeats a write', async () => {
    const http = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    const read = firstValueFrom(http.get('/api/documents/'));
    backend.expectOne('/api/documents/').flush(null, { status: 503, statusText: 'Service Unavailable' });
    await vi.advanceTimersByTimeAsync(2000);
    backend.expectOne('/api/documents/').flush({ ok: true });
    expect(await read).toEqual({ ok: true });

    const write = firstValueFrom(http.post('/api/transactions/x', {})).catch((error) => error.status);
    backend.expectOne('/api/transactions/x').flush(null, { status: 503, statusText: 'Service Unavailable' });
    expect(await write).toBe(503);
    // A real error (not a restart) isn't retried either.
    const missing = firstValueFrom(http.get('/api/documents/missing')).catch((error) => error.status);
    backend.expectOne('/api/documents/missing').flush(null, { status: 404, statusText: 'Not Found' });
    expect(await missing).toBe(404);
    backend.verify();
  });
});
