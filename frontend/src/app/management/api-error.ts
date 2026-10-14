import { HttpErrorResponse } from '@angular/common/http';

export function apiError(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 401) return 'Your session has ended. Refresh the page and sign in again.';
    if (error.status === 403) return 'You do not have permission to do this.';
    if (error.status === 429) return 'Too many attempts. Wait a minute and try again.';
    const errors = error.error?.errors;
    if (errors && typeof errors === 'object') {
      const messages = Object.values(errors)
        .flat()
        .filter((value): value is string => typeof value === 'string');
      if (messages.length) return messages.join(' ');
    }
    if (typeof error.error?.detail === 'string') return error.error.detail;
  }
  return fallback;
}
