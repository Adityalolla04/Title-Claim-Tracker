import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthStateService } from '../services/auth-state.service';

export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const state = inject(AuthStateService);
  const router = inject(Router);
  const withCredentials = request.url.startsWith('/api')
    ? request.clone({ withCredentials: true })
    : request;

  return next(withCredentials).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && isProtectedRequest(request.url)) {
        state.clear();
        if (!router.url.startsWith('/login') && !router.url.startsWith('/register')) {
          void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
        }
      }

      return throwError(() => error);
    }),
  );
};

function isProtectedRequest(url: string): boolean {
  return !url.includes('/api/auth/csrf') && !url.includes('/api/auth/login') && !url.includes('/api/auth/register') && !url.includes('/api/auth/me');
}
