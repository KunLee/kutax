import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { retry, throwError, timer } from 'rxjs';

/** No answer at all, or the proxy saying the app isn't there: what a restart or deploy looks like for a few seconds. */
export const transient = (error: unknown) => error instanceof HttpErrorResponse && [0, 502, 503, 504].includes(error.status);

/**
 * Reads ride out a short restart (about 25 seconds) instead of showing "couldn't load". Only GETs are retried:
 * repeating a write could apply it twice.
 */
export const retryReads: HttpInterceptorFn = (request, next) =>
  request.method !== 'GET'
    ? next(request)
    : next(request).pipe(retry({ count: 6, delay: (error, attempt) => (transient(error) ? timer(Math.min(2000 * attempt, 5000)) : throwError(() => error)) }));
