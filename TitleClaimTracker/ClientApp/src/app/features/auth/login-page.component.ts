import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <main class="auth-page">
      <section class="auth-card" aria-labelledby="login-title">
        <span class="eyebrow">SECURE WORKSPACE</span>
        <h1 id="login-title">Welcome back.</h1>
        <p class="intro">Sign in to manage your submitted claims and continue an assisted intake.</p>
        <div class="error" *ngIf="errorMessage()" role="alert">{{ errorMessage() }}</div>
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <label for="email">Email address</label>
          <input id="email" type="email" autocomplete="email" formControlName="email" [attr.aria-invalid]="form.controls.email.invalid && form.controls.email.touched" />
          <small class="field-error" *ngIf="form.controls.email.touched && form.controls.email.invalid">Enter a valid email address.</small>
          <label for="password">Password</label>
          <input id="password" type="password" autocomplete="current-password" formControlName="password" />
          <small class="field-error" *ngIf="form.controls.password.touched && form.controls.password.invalid">Enter your password.</small>
          <label class="checkbox"><input type="checkbox" formControlName="rememberMe" /> <span>Keep me signed in on this device</span></label>
          <button class="submit" type="submit" [disabled]="pending">{{ pending ? 'Signing in…' : 'Sign in' }}</button>
        </form>
        <p class="alternate">New to the workspace? <a routerLink="/register">Create an account</a></p>
      </section>
    </main>
  `,
  styles: [`
    .auth-page { align-items: center; display: flex; justify-content: center; min-height: calc(100vh - 73px); padding: 38px 20px; }
    .auth-card { background: #fff; border: 1px solid #e7ebf2; border-radius: 20px; box-shadow: 0 20px 60px #1720330b; max-width: 440px; padding: 36px; width: 100%; }
    .eyebrow { color: #5367e8; font-size: .7rem; font-weight: 800; letter-spacing: .18em; }
    h1 { font-size: 2.7rem; letter-spacing: -.07em; margin: 13px 0 8px; }
    .intro, .alternate { color: #718096; line-height: 1.55; }
    form { display: grid; gap: 8px; margin-top: 28px; }
    label { font-size: .83rem; font-weight: 800; margin-top: 7px; }
    input:not([type="checkbox"]) { border: 1px solid #dce1ec; border-radius: 9px; box-sizing: border-box; font: inherit; padding: 12px; width: 100%; }
    input:focus { border-color: #5367e8; box-shadow: 0 0 0 3px #5367e826; outline: none; }
    .checkbox { align-items: center; display: flex; font-weight: 600; gap: 7px; margin: 9px 0; }
    .checkbox input { accent-color: #5367e8; }
    .field-error { color: #a33a30; font-size: .76rem; }
    .error { background: #fff0f0; border: 1px solid #f5c2c2; border-radius: 9px; color: #8a1c1c; font-size: .82rem; line-height: 1.45; margin-top: 20px; padding: 11px; }
    .submit { background: #5367e8; border: 0; border-radius: 9px; color: #fff; cursor: pointer; font: inherit; font-weight: 800; margin-top: 12px; padding: 13px; }
    .submit:disabled { cursor: wait; opacity: .55; }
    .alternate { font-size: .84rem; margin: 24px 0 0; text-align: center; }
    a { color: #5367e8; font-weight: 800; }
  `],
})
export class LoginPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly errorMessage = signal<string | null>(null);
  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(256)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    rememberMe: new FormControl(false, { nonNullable: true }),
  });
  pending = false;

  submit(): void {
    if (this.form.invalid || this.pending) {
      this.form.markAllAsTouched();
      return;
    }

    this.pending = true;
    this.errorMessage.set(null);
    this.auth.login(this.form.getRawValue()).subscribe({
      next: () => void this.router.navigateByUrl(this.safeReturnUrl()),
      error: error => {
        this.pending = false;
        this.errorMessage.set(this.apiError(error, 'Sign-in failed. Check your email and password.'));
      },
      complete: () => { this.pending = false; },
    });
  }

  private safeReturnUrl(): string {
    const value = this.route.snapshot.queryParamMap.get('returnUrl');
    return value && value.startsWith('/') && !value.startsWith('//') ? value : '/dashboard';
  }

  private apiError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && typeof error.error?.error === 'string') {
      return error.error.error;
    }

    return fallback;
  }
}
