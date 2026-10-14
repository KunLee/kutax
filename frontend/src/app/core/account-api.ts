import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface Account {
  id: string;
  email: string;
  displayName: string;
  isAdministrator: boolean;
  mustChangePassword: boolean;
  financialYearStart: number;
  financialYearLabel: string;
  financialYearFrom: string;
  financialYearTo: string;
  /** Optional details. */
  phoneNumber?: string | null;
  occupation?: string | null;
  /** The profile photo's address, or null without one. */
  avatarUrl?: string | null;
}

export interface ManagedUser {
  id: string;
  email: string;
  displayName: string;
  isEnabled: boolean;
  isAdministrator: boolean;
  mustChangePassword: boolean;
  isLockedOut: boolean;
  /** Has receipts or tax packs: can be disabled but not deleted. */
  hasRecords?: boolean;
  /** Administrators' private note about the account. */
  note?: string | null;
  createdAt?: string | null;
}
export interface UserPage {
  items: ManagedUser[];
  total: number;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly http = inject(HttpClient);

  csrf() {
    return firstValueFrom(this.http.get<void>('/api/auth/csrf'));
  }
  session() {
    return firstValueFrom(this.http.get<Account>('/api/auth/session'));
  }
  ready() {
    return firstValueFrom(this.http.get('/api/health/ready'));
  }

  async login(email: string, password: string) {
    await this.csrf();
    await firstValueFrom(this.http.post<void>('/api/auth/login', { email, password }));
    await this.csrf();
    return this.session();
  }

  async registrationEnabled() {
    return (await firstValueFrom(this.http.get<{ enabled: boolean }>('/api/auth/registration'))).enabled;
  }

  async register(email: string, displayName: string, password: string, inviteToken: string | null = null) {
    await this.csrf();
    await firstValueFrom(this.http.post<Account>('/api/auth/register', { email, displayName, password, inviteToken }));
    // The identity changed, so the anonymous antiforgery token must be replaced.
    await this.csrf();
    return this.session();
  }

  async logout() {
    await firstValueFrom(this.http.post<void>('/api/auth/logout', {}));
    await this.csrf();
  }

  selectYear(id: string, startYear: number) {
    return firstValueFrom(
      this.http.put<Account>(`/api/accounts/${encodeURIComponent(id)}/financial-year`, {
        startYear,
      }),
    );
  }

  saveProfile(displayName: string, phoneNumber = '', occupation = '') {
    return firstValueFrom(this.http.put<Account>('/api/accounts/me/profile', { displayName, phoneNumber, occupation }));
  }
  /** A picture already shrunk on the device, as base64. */
  saveAvatar(image: string) {
    return firstValueFrom(this.http.put<Account>('/api/accounts/me/avatar', { image }));
  }
  removeAvatar() {
    return firstValueFrom(this.http.delete<Account>('/api/accounts/me/avatar'));
  }

  async changePassword(currentPassword: string, newPassword: string) {
    const account = await firstValueFrom(
      this.http.post<Account>('/api/accounts/me/password', { currentPassword, newPassword }),
    );
    await this.csrf();
    return account;
  }

  users(search = '', page = 1) {
    return firstValueFrom(this.http.get<UserPage>('/api/users', { params: { search, page } }));
  }
  createUser(email: string, displayName: string, temporaryPassword: string) {
    return firstValueFrom(
      this.http.post<ManagedUser>('/api/users', { email, displayName, temporaryPassword }),
    );
  }
  setEnabled(id: string, isEnabled: boolean) {
    return firstValueFrom(this.http.put<ManagedUser>(`/api/users/${encodeURIComponent(id)}`, { isEnabled }));
  }
  setAdministrator(id: string, isAdministrator: boolean) {
    return firstValueFrom(this.http.put<ManagedUser>(`/api/users/${encodeURIComponent(id)}/administrator`, { isAdministrator }));
  }
  saveNote(id: string, note: string) {
    return firstValueFrom(this.http.put<ManagedUser>(`/api/users/${encodeURIComponent(id)}/note`, { note }));
  }
  unlockUser(id: string) {
    return firstValueFrom(
      this.http.post<ManagedUser>(`/api/users/${encodeURIComponent(id)}/unlock`, {}),
    );
  }
  deleteUser(id: string) {
    return firstValueFrom(this.http.post<void>(`/api/users/${encodeURIComponent(id)}/delete`, { confirm: true }));
  }
  resetPassword(id: string, temporaryPassword: string) {
    return firstValueFrom(
      this.http.post<ManagedUser>(`/api/users/${encodeURIComponent(id)}/reset-password`, {
        temporaryPassword,
      }),
    );
  }
}
