import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, finalize, of, shareReplay, switchMap, tap, throwError } from 'rxjs';
import { CurrentAccount, LoginRequest, RegisterRequest } from '../models/auth.model';
import { AuthStateService } from './auth-state.service';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly state = inject(AuthStateService);
  private restoreRequest$: Observable<CurrentAccount | null> | null = null;

  readonly account = this.state.account;
  readonly error = this.state.error;
  readonly isRestoring = this.state.isRestoring;
  readonly isAuthenticated = this.state.isAuthenticated;
  readonly isAdministrator = this.state.isAdministrator;

  restoreSession(): Observable<CurrentAccount | null> {
    if (!this.state.isRestoring()) {
      return of(this.state.account() ?? null);
    }

    if (this.restoreRequest$) {
      return this.restoreRequest$;
    }

    this.restoreRequest$ = this.getCsrfToken().pipe(
      switchMap(() => this.http.get<CurrentAccount>('/api/auth/me')),
      tap(account => this.state.setAccount(account)),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.state.clear();
          return of(null);
        }

        this.state.markUnavailable('The authentication service is unavailable. Start the backend and try again.');
        return throwError(() => error);
      }),
      finalize(() => {
        this.restoreRequest$ = null;
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.restoreRequest$;
  }

  ensureSession(): Observable<CurrentAccount | null> {
    return this.state.isRestoring() ? this.restoreSession() : of(this.state.account() ?? null);
  }

  register(request: RegisterRequest): Observable<CurrentAccount> {
    return this.getCsrfToken().pipe(
      switchMap(() => this.http.post<CurrentAccount>('/api/auth/register', request)),
      tap(account => this.state.setAccount(account)),
    );
  }

  login(request: LoginRequest): Observable<CurrentAccount> {
    return this.getCsrfToken().pipe(
      switchMap(() => this.http.post<CurrentAccount>('/api/auth/login', request)),
      tap(account => this.state.setAccount(account)),
    );
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', null).pipe(
      tap(() => this.state.clear()),
      catchError(error => {
        this.state.clear();
        return throwError(() => error);
      }),
    );
  }

  private getCsrfToken(): Observable<void> {
    return this.http.get<void>('/api/auth/csrf');
  }
}
