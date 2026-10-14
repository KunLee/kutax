import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { App } from './app';
import { routes } from './app.routes';
import { AccountApi } from './core/account-api';

const account = {
  id: 'owner',
  email: 'owner@kutax.test',
  displayName: 'Owner',
  isAdministrator: false,
  mustChangePassword: false,
  financialYearStart: 2025,
  financialYearLabel: 'FY 2025–26',
  financialYearFrom: '2025-07-01',
  financialYearTo: '2026-06-30',
};

describe('workspace', () => {
  async function setup(signedIn = false, overrides = {}, registration = false) {
    const sessionAccount = { ...account, ...overrides };
    const api = {
      registrationEnabled: vi.fn().mockResolvedValue(registration),
      register: vi.fn().mockResolvedValue({ ...account, id: 'new', email: 'new@kutax.test', displayName: 'New Person' }),
      csrf: vi.fn().mockResolvedValue(undefined),
      session: signedIn
        ? vi.fn().mockResolvedValue(sessionAccount)
        : vi.fn().mockRejectedValue(new HttpErrorResponse({ status: 401 })),
      login: vi.fn().mockResolvedValue(account),
      logout: vi.fn().mockResolvedValue(undefined),
      ready: vi.fn().mockResolvedValue({ status: 'ready' }),
      selectYear: vi.fn().mockResolvedValue({
        ...account,
        financialYearStart: 2024,
        financialYearLabel: 'FY 2024–25',
      }),
    };
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes), { provide: AccountApi, useValue: api }],
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    await TestBed.inject(Router).navigateByUrl('/');
    await fixture.whenStable();
    return { fixture, api };
  }

  it('shows sign-in when the session is unauthenticated', async () => {
    const { fixture } = await setup();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('h2').textContent).toContain('Welcome to KuTax');
    expect(fixture.nativeElement.querySelector('input[type=password]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[role=alert]')).toBeNull();
  });

  it('shows the selected financial year without invented receipt or deduction totals', async () => {
    const { fixture } = await setup(true);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('FY 2025–26');
    expect(fixture.nativeElement.textContent).toContain('1 Jul 2025');
    expect(fixture.nativeElement.textContent).toContain('30 Jun 2026');
    expect(fixture.nativeElement.textContent).toContain('Receipt inbox');
    expect(fixture.nativeElement.textContent).not.toContain('Coming next');
    const picker: HTMLButtonElement = fixture.nativeElement.querySelector('#financial-year');
    expect(picker.textContent).toContain('FY 2025–26');
    // The themed list opens on click and marks the chosen year.
    picker.click();
    fixture.detectChanges();
    const selected = fixture.nativeElement.querySelector('#year-list [aria-selected="true"]');
    expect(selected.textContent).toContain('FY 2025–26');
  });

  it('saves the year against the signed-in account', async () => {
    const { fixture, api } = await setup(true);
    await fixture.whenStable();
    await fixture.componentInstance.selectYear('2024');
    await fixture.whenStable();
    expect(api.selectYear).toHaveBeenCalledWith('owner', 2024);
    expect(fixture.componentInstance.account()?.financialYearStart).toBe(2024);
  });

  it('offers sign-up only when the server enables it', async () => {
    const closed = await setup();
    expect(closed.fixture.nativeElement.textContent).not.toContain('Create account');
    expect(closed.fixture.nativeElement.textContent).toContain('Your administrator creates accounts');
    TestBed.resetTestingModule();
    const { fixture } = await setup(false, {}, true);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Create account');
  });

  it('creates an account only when both passwords match', async () => {
    const { fixture, api } = await setup(false, {}, true);
    const component = fixture.componentInstance;
    component.switchMode('register');
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#register-name')).toBeTruthy();
    component.displayName = 'New Person';
    component.email = 'new@kutax.test';
    component.password = 'Synthetic-Register-481!';
    component.confirmPassword = 'different';
    await component.register();
    expect(api.register).not.toHaveBeenCalled();
    expect(component.mismatch()).toBe(true);
    component.confirmPassword = component.password;
    await component.register();
    expect(api.register).toHaveBeenCalledWith('new@kutax.test', 'New Person', 'Synthetic-Register-481!', null);
    expect(component.account()?.email).toBe('new@kutax.test');
    expect(component.password).toBe('');
  });

  it('keeps the login form usable when credentials are rejected', async () => {
    const { fixture, api } = await setup();
    await fixture.whenStable();
    api.login.mockRejectedValue(new HttpErrorResponse({ status: 401 }));
    fixture.componentInstance.email = 'owner@kutax.test';
    fixture.componentInstance.password = 'invalid';
    await fixture.componentInstance.signIn();
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain(
      'Check your details',
    );
    expect(fixture.componentInstance.busy()).toBe(false);
    expect(fixture.componentInstance.account()).toBeNull();
  });

  it('shows the Users navigation only for an administrator', async () => {
    const openMenu = async (fixture: { nativeElement: HTMLElement; whenStable(): Promise<unknown> }) => {
      fixture.nativeElement.querySelector<HTMLButtonElement>('button[aria-controls=account-menu]')!.click();
      await fixture.whenStable();
      return fixture.nativeElement.querySelector('#account-menu')!.textContent;
    };
    const { fixture } = await setup(true, { isAdministrator: true });
    // Four places: Home, Receipts, Tax and History (settings live in the account menu).
    expect(fixture.nativeElement.querySelector('nav[aria-label=Main]').textContent.replace(/\s/g, '')).toBe('HomeReceiptsTaxHistory');
    expect(await openMenu(fixture)).toContain('Users');
    TestBed.resetTestingModule();
    const member = await setup(true);
    expect(await openMenu(member.fixture)).not.toContain('Users');
  });

  it('requires a password change before showing workspace navigation', async () => {
    const { fixture } = await setup(true, { mustChangePassword: true });
    expect(fixture.nativeElement.querySelector('nav')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Choose a new password');
    expect(fixture.nativeElement.querySelector('#financial-year')).toBeNull();
  });
});
