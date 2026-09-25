import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from './core/services/auth.service';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <div class="app-frame">
      <header class="topbar">
        <a class="brand" routerLink="/" aria-label="Title Claim Intelligence home">
          <span class="brand-mark">TC</span>
          <span><strong>Title Claim</strong><small>INTELLIGENCE</small></span>
        </a>
        <nav aria-label="Primary navigation">
          <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">Home</a>
          <a *ngIf="auth.isAuthenticated()" routerLink="/dashboard" routerLinkActive="active">My workspace</a>
          <a *ngIf="auth.isAdministrator()" routerLink="/admin" routerLinkActive="active">Admin</a>
        </nav>
        <div class="account-actions">
          <ng-container *ngIf="auth.account() as account; else signedOut">
            <span class="account-name">{{ account.displayName || account.email }}</span>
            <button type="button" class="button button-quiet" [disabled]="loggingOut" (click)="logout()">
              {{ loggingOut ? 'Signing out…' : 'Sign out' }}
            </button>
          </ng-container>
          <ng-template #signedOut>
            <a class="button button-quiet" routerLink="/login">Sign in</a>
            <a class="button button-primary" routerLink="/register">Create account</a>
          </ng-template>
        </div>
      </header>

      <div class="session-alert" *ngIf="auth.error()" role="alert">{{ auth.error() }}</div>
      <div class="session-loading" *ngIf="auth.isRestoring()" role="status" aria-live="polite">Restoring your secure session…</div>
      <router-outlet></router-outlet>
    </div>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; background: #f5f7fb; color: #172033; font-family: Inter, ui-sans-serif, system-ui, sans-serif; }
    .app-frame { min-height: 100vh; }
    .topbar { align-items: center; background: rgba(255,255,255,.96); border-bottom: 1px solid #e7ebf2; display: flex; gap: 28px; min-height: 72px; padding: 0 clamp(18px, 5vw, 72px); position: sticky; top: 0; z-index: 10; }
    .brand { align-items: center; color: #172033; display: flex; gap: 10px; text-decoration: none; white-space: nowrap; }
    .brand-mark { align-items: center; background: #5367e8; border-radius: 11px; color: #fff; display: inline-flex; font-size: .75rem; font-weight: 800; height: 34px; justify-content: center; width: 34px; }
    .brand strong, .brand small { display: block; }
    .brand strong { font-size: .9rem; letter-spacing: -.02em; }
    .brand small { color: #5367e8; font-size: .56rem; font-weight: 800; letter-spacing: .18em; margin-top: 2px; }
    nav { display: flex; flex: 1; gap: 20px; }
    nav a { color: #718096; font-size: .88rem; font-weight: 700; padding: 26px 0 23px; text-decoration: none; }
    nav a:hover, nav a.active { color: #5367e8; }
    nav a.active { border-bottom: 3px solid #5367e8; }
    .account-actions { align-items: center; display: flex; gap: 10px; }
    .account-name { color: #718096; font-size: .78rem; max-width: 190px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .button { border: 1px solid #dce1ec; border-radius: 9px; cursor: pointer; display: inline-block; font: inherit; font-size: .82rem; font-weight: 800; padding: 9px 13px; text-decoration: none; }
    .button-primary { background: #5367e8; border-color: #5367e8; color: #fff; }
    .button-quiet { background: #fff; color: #5367e8; }
    .button:disabled { cursor: wait; opacity: .55; }
    .session-alert { background: #fff3f0; border-bottom: 1px solid #f0c2b8; color: #8a1c1c; padding: 11px 24px; text-align: center; }
    .session-loading { color: #718096; font-size: .85rem; padding: 12px; text-align: center; }
    @media (max-width: 760px) { .topbar { flex-wrap: wrap; gap: 10px 18px; padding: 12px 16px; } nav { order: 3; width: 100%; } nav a { padding: 4px 0 8px; } .account-actions { margin-left: auto; } .account-name { display: none; } }
  `],
})
export class AppShellComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  loggingOut = false;

  logout(): void {
    if (this.loggingOut) {
      return;
    }

    this.loggingOut = true;
    this.auth.logout().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.router.navigateByUrl('/'),
      error: () => this.router.navigateByUrl('/'),
      complete: () => { this.loggingOut = false; },
    });
  }
}
