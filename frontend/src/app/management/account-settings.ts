import { Component, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Account, AccountApi } from '../core/account-api';
import { apiError } from './api-error';
import { TranslatePipe } from '../core/i18n';

/** A centred square crop of the picture, 256 pixels across, as base64 JPEG. */
async function shrink(file: File): Promise<string> {
  const bitmap = await createImageBitmap(file);
  const side = Math.min(bitmap.width, bitmap.height);
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 256;
  const context = canvas.getContext('2d')!;
  context.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, 256, 256);
  bitmap.close();
  const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('encode'))), 'image/jpeg', 0.85));
  const bytes = new Uint8Array(await blob.arrayBuffer());
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

@Component({
  selector: 'app-account-settings',
  imports: [TranslatePipe, FormsModule],
  templateUrl: './account-settings.html',
  styleUrl: './management.scss',
})
export class AccountSettings {
  /** Inside Settings' "You" tab, without its own page heading. */
  readonly embedded = input(false);
  readonly account = input.required<Account>();
  readonly saved = output<Account>();
  private readonly api = inject(AccountApi);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  displayName = '';
  phoneNumber = '';
  occupation = '';
  readonly occupations = ['Nurse', 'Teacher', 'Software engineer', 'Accountant', 'Retail worker', 'Tradesperson', 'Doctor', 'Sales representative', 'Office administrator', 'Driver'];
  currentPassword = '';
  newPassword = '';
  confirmPassword = '';

  constructor() {
    effect(() => {
      const account = this.account();
      this.displayName = account.displayName;
      this.phoneNumber = account.phoneNumber ?? '';
      this.occupation = account.occupation ?? '';
    });
  }

  async saveProfile() {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      this.saved.emit(await this.api.saveProfile(this.displayName.trim(), this.phoneNumber.trim(), this.occupation.trim()));
      this.notice.set('Personal details saved.');
    } catch (error) {
      this.error.set(apiError(error, 'We couldn’t save your details. Try again.'));
    } finally {
      this.busy.set(false);
    }
  }

  initials() {
    const source = this.account().displayName.trim() || this.account().email;
    return source.split(/\s+/).slice(0, 2).map((part) => part[0]?.toUpperCase() ?? '').join('');
  }

  /** Shrinks the picture on the device (a 256-pixel square JPEG) before it's sent, so any photo works. */
  async pickPhoto(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      const image = await shrink(file);
      this.saved.emit(await this.api.saveAvatar(image));
      this.notice.set('Photo saved.');
    } catch (error) {
      this.error.set(apiError(error, 'That picture couldn’t be used. Try a JPEG or PNG photo.'));
    } finally {
      this.busy.set(false);
    }
  }

  async removePhoto() {
    if (this.busy()) return;
    this.busy.set(true);
    try {
      this.saved.emit(await this.api.removeAvatar());
      this.notice.set('Photo removed.');
    } catch (error) {
      this.error.set(apiError(error, 'We couldn’t save your details. Try again.'));
    } finally {
      this.busy.set(false);
    }
  }

  async changePassword() {
    if (this.busy()) return;
    this.error.set('');
    this.notice.set('');
    if (this.newPassword !== this.confirmPassword) {
      this.error.set('The new passwords do not match.');
      return;
    }
    this.busy.set(true);
    try {
      this.saved.emit(await this.api.changePassword(this.currentPassword, this.newPassword));
      this.currentPassword = '';
      this.newPassword = '';
      this.confirmPassword = '';
      this.notice.set('Password changed. Your other sessions have been signed out.');
    } catch (error) {
      this.error.set(apiError(error, 'We couldn’t change your password. Try again.'));
    } finally {
      this.busy.set(false);
    }
  }
}
