import { TestBed } from '@angular/core/testing';
import { Account, AccountApi } from '../core/account-api';
import { AccountSettings } from './account-settings';

const account: Account = {
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

describe('personal settings', () => {
  function setup(mustChangePassword = false) {
    const api = {
      changePassword: vi.fn().mockResolvedValue(account),
      saveProfile: vi.fn().mockResolvedValue(account),
    };
    TestBed.configureTestingModule({
      imports: [AccountSettings],
      providers: [{ provide: AccountApi, useValue: api }],
    });
    const fixture = TestBed.createComponent(AccountSettings);
    fixture.componentRef.setInput('account', { ...account, mustChangePassword });
    fixture.detectChanges();
    return { fixture, api };
  }

  it('requires matching passwords and clears credentials after a successful change', async () => {
    const { fixture, api } = setup();
    const component = fixture.componentInstance;
    component.currentPassword = 'Synthetic-Current-481!';
    component.newPassword = 'Synthetic-New-Password-692!';
    component.confirmPassword = 'does-not-match';
    await component.changePassword();
    expect(api.changePassword).not.toHaveBeenCalled();
    expect(component.error()).toContain('do not match');
    component.confirmPassword = component.newPassword;
    const saved = vi.fn();
    component.saved.subscribe(saved);
    await component.changePassword();
    expect(saved).toHaveBeenCalledWith(account);
    expect(component.currentPassword).toBe('');
    expect(component.newPassword).toBe('');
    expect(component.confirmPassword).toBe('');
  });

  it('shows only the password flow for temporary-password accounts', () => {
    const { fixture } = setup(true);
    expect(fixture.nativeElement.textContent).toContain('Choose a new password');
    expect(fixture.nativeElement.querySelector('#display-name')).toBeNull();
    expect(fixture.nativeElement.querySelector('#current-password')).toBeTruthy();
  });
});
