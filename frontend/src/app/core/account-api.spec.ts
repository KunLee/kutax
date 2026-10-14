import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AccountApi } from './account-api';

describe('AccountApi', () => {
  it('refreshes CSRF before login and after the identity changes', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const promise = TestBed.inject(AccountApi).login('owner@kutax.test', 'synthetic-password');
    http.expectOne('/api/auth/csrf').flush(null);
    await Promise.resolve();
    const login = http.expectOne('/api/auth/login');
    expect(login.request.method).toBe('POST');
    login.flush(null);
    await Promise.resolve();
    http.expectOne('/api/auth/csrf').flush(null);
    await Promise.resolve();
    http.expectOne('/api/auth/session').flush({ id: 'owner' });
    expect((await promise).id).toBe('owner');
    http.verify();
  });
});
