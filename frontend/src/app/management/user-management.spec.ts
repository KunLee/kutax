import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { AccountApi, ManagedUser } from '../core/account-api';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Session } from '../core/session';
import { UserManagement } from './user-management';

const member: ManagedUser = {
  id: 'member',
  email: 'member@kutax.test',
  displayName: 'Member',
  isEnabled: true,
  isAdministrator: false,
  mustChangePassword: false,
  isLockedOut: false,
};

describe('user management', () => {
  async function setup() {
    const api = {
      users: vi
        .fn()
        .mockResolvedValue({
          items: [
            member,
            { ...member, id: 'admin', email: 'admin@kutax.test', isAdministrator: true },
          ],
          total: 2,
          page: 1,
          pageSize: 25,
        }),
      createUser: vi.fn().mockResolvedValue(member),
      setEnabled: vi.fn().mockResolvedValue(member),
      setAdministrator: vi.fn().mockResolvedValue(member),
      saveNote: vi.fn().mockResolvedValue(member),
      deleteUser: vi.fn().mockResolvedValue(undefined),
      resetPassword: vi.fn().mockResolvedValue(member),
    };
    TestBed.configureTestingModule({
      imports: [UserManagement],
      providers: [provideRouter([]), { provide: AccountApi, useValue: api }, { provide: Session, useValue: { account: signal({ id: 'admin' }) } }],
    });
    const fixture = TestBed.createComponent(UserManagement);
    fixture.detectChanges();
    await vi.waitFor(() => expect(fixture.componentInstance.busy()).toBe(false));
    await fixture.whenStable();
    return { fixture, api };
  }

  it('offers every action on other accounts but none on your own', async () => {
    const { fixture } = await setup();
    const page: HTMLElement = fixture.nativeElement;
    // A note and administrator access, but never someone else's name or email.
    expect(page.querySelectorAll('button[aria-label^="Note about "]').length).toBe(1);
    expect(page.textContent).toContain('Make administrator');
    expect(page.querySelector('#user-name')?.closest('form')?.textContent).toContain('Create account');
    expect(page.querySelector('button[aria-label="Delete member@kutax.test"]')).not.toBeNull();
    expect(page.querySelector('button[aria-label="Disable member@kutax.test"]')).not.toBeNull();
    expect(page.textContent).toContain('Change your own details in personal settings');
  });

  it('disables in one tap, deletes empty accounts and steers accounts with records to disabling', async () => {
    const { fixture, api } = await setup();
    const component = fixture.componentInstance;
    await component.setEnabled(member, false);
    expect(api.setEnabled).toHaveBeenCalledWith('member', false);
    component.result.set({ ...component.result(), items: [{ ...member, hasRecords: true }] });
    component.openPanel(member, 'delete');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('can’t be deleted');
    component.result.set({ ...component.result(), items: [member] });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Delete member@kutax.test permanently?');
    await component.remove(member);
    expect(api.deleteUser).toHaveBeenCalledWith('member');
    expect(component.notice()).toBe('Account deleted.');
  });

  it('suggests a strong temporary password for a reset', async () => {
    const { fixture, api } = await setup();
    const component = fixture.componentInstance;
    component.openPanel(member, 'reset');
    expect(component.resetPassword).toMatch(/^(?=.*[A-Z])(?=.*[a-z])(?=.*\d)(?=.*[^A-Za-z0-9]).{16}$/);
    const password = component.resetPassword;
    await component.reset(member);
    expect(api.resetPassword).toHaveBeenCalledWith('member', password);
    expect(component.resetPassword).toBe('');
  });

  it('creates an account and clears the temporary password after saving', async () => {
    const { fixture, api } = await setup();
    const component = fixture.componentInstance;
    component.email = 'new@kutax.test';
    component.displayName = 'New user';
    component.temporaryPassword = 'Synthetic-Temporary-481!';
    await component.save();
    expect(api.createUser).toHaveBeenCalledWith(
      'new@kutax.test',
      'New user',
      'Synthetic-Temporary-481!',
    );
    // A fresh suggestion is ready for the next account; the one just used isn't kept.
    expect(component.temporaryPassword).not.toBe('Synthetic-Temporary-481!');
    expect(component.notice()).toContain('must change it at sign-in');
  });

  it('keeps a private note and makes someone an administrator after confirming', async () => {
    const { fixture, api } = await setup();
    const component = fixture.componentInstance;
    component.openPanel(member, 'note');
    component.note = '  Synthetic: family member  ';
    await component.saveNote(member);
    expect(api.saveNote).toHaveBeenCalledWith('member', 'Synthetic: family member');
    component.openPanel(member, 'admin');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Make member@kutax.test an administrator?');
    await component.setAdministrator(member, true);
    expect(api.setAdministrator).toHaveBeenCalledWith('member', true);
    expect(component.noticeParams()).toEqual({ email: 'member@kutax.test' });
  });

  it('keeps the note and shows the error when saving fails', async () => {
    const { fixture, api } = await setup();
    const component = fixture.componentInstance;
    component.openPanel(member, 'note');
    component.note = 'Synthetic note';
    api.saveNote.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { detail: 'The account changed. Refresh and try again.' } }));
    await component.saveNote(member);
    expect(component.isOpen(member, 'note')).toBe(true);
    expect(component.note).toBe('Synthetic note');
    expect(component.error()).toContain('Refresh and try again');
    expect(component.busy()).toBe(false);
  });
});
