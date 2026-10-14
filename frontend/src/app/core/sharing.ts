import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, firstValueFrom, throwError } from 'rxjs';

export interface Person { name: string; email: string; }
export interface Outgoing { id: string; email: string; status: 'Pending' | 'Active' | 'Expired'; createdAt: string; expiresAt: string; acceptedAt: string | null; person: Person | null; }
export interface Incoming { id: string; ownerId: string; person: Person | null; acceptedAt: string; }
export interface Invitation { id: string; ownerId: string; person: Person | null; expiresAt: string; canAcceptHere: boolean; }
export interface SharingState { emailEnabled: boolean; outgoing: Outgoing[]; incoming: Incoming[]; invitations: Invitation[]; }
export interface InviteResult { id: string; link: string; emailed: boolean; }
export interface Activity { person: Person | null; action: string; createdAt: string; }
/** Whose records this browser is working on, when not your own. */
export interface ActingFor { ownerId: string; name: string; email: string; }

const actingKey = 'kutax.actingFor';
const inviteKey = 'kutax.invite';
/** Someone to share with: people you've shared with before (known) come first, then other accounts here. */
export interface Suggestion { name: string; email: string; known: boolean; }
const header = 'X-KuTax-Act-As';
// Your own account, sign-in and sharing always act as you.
const ownPaths = ['/api/auth', '/api/accounts', '/api/sharing', '/api/users', '/api/invitations'];

function read<T>(storage: () => Storage, key: string): T | null {
  try {
    const value = storage().getItem(key);
    return value ? (JSON.parse(value) as T) : null;
  } catch {
    return null;
  }
}
function write(storage: () => Storage, key: string, value: unknown) {
  try {
    if (value === null) storage().removeItem(key);
    else storage().setItem(key, JSON.stringify(value));
  } catch {
    // Storage unavailable: this visit only.
  }
}

/** Full reload, so nothing from the other person's records stays in memory (replaceable in tests). */
export const navigation = { go: (url: string) => location.assign(url) };

export const actingFor = () => read<ActingFor>(() => localStorage, actingKey);
/** An invitation link opened before signing in, kept until the person has an account to accept it with. */
export const pendingInvite = () => read<string>(() => sessionStorage, inviteKey);
export const rememberInvite = (token: string | null) => write(() => sessionStorage, inviteKey, token);

/**
 * Sends "act as" on data requests while working for someone. If that access has ended, the server refuses before
 * reading anything; the browser then goes back to the person's own records.
 */
export const actAs: HttpInterceptorFn = (request, next) => {
  const acting = actingFor();
  if (!acting || !request.url.startsWith('/api/') || ownPaths.some((path) => request.url.startsWith(path))) return next(request);
  return next(request.clone({ setHeaders: { [header]: acting.ownerId } })).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 403 && error.error?.code === 'DelegationEnded') {
        write(() => localStorage, actingKey, null);
        navigation.go('/');
      }
      return throwError(() => error);
    }),
  );
};

@Injectable({ providedIn: 'root' })
export class Sharing {
  private readonly http = inject(HttpClient);
  readonly state = signal<SharingState | null>(null);
  readonly acting = signal<ActingFor | null>(actingFor());

  async load() {
    try {
      this.state.set(await firstValueFrom(this.http.get<SharingState>('/api/sharing')));
    } catch {
      // The banner and menu simply don't show; Settings reports errors itself.
    }
    // Access ended while away: back to your own records.
    const acting = this.acting();
    if (acting && this.state() && !this.state()!.incoming.some((i) => i.ownerId === acting.ownerId)) this.switchTo(null);
    return this.state();
  }

  /** Changes whose records the whole app shows; a fresh start so nothing from the other person stays on screen. */
  switchTo(person: ActingFor | null, to = '/') {
    write(() => localStorage, actingKey, person);
    navigation.go(to);
  }

  invite(email: string) { return firstValueFrom(this.http.post<InviteResult>('/api/sharing/invite', { email })); }
  resend(id: string) { return firstValueFrom(this.http.post<InviteResult>(`/api/sharing/${id}/resend`, {})); }
  end(id: string) { return firstValueFrom(this.http.post<void>(`/api/sharing/${id}/end`, {})); }
  accept(id: string) { return firstValueFrom(this.http.post<{ ownerId: string }>(`/api/sharing/${id}/accept`, {})); }
  decline(id: string) { return firstValueFrom(this.http.post<void>(`/api/sharing/${id}/decline`, {})); }
  acceptLink(token: string) { return firstValueFrom(this.http.post<{ ownerId: string }>('/api/sharing/accept-link', { token })); }
  people(q: string) { return firstValueFrom(this.http.get<Suggestion[]>('/api/sharing/people', { params: { q } })); }
  activity() { return firstValueFrom(this.http.get<Activity[]>('/api/sharing/activity')); }
  invitation(token: string) { return firstValueFrom(this.http.get<{ from: Person | null; email: string; state: string }>(`/api/invitations/${encodeURIComponent(token)}`)); }
}
