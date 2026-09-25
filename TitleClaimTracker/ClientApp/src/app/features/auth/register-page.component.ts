import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-register-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <main class="auth-page">
      <section class="auth-card" aria-labelledby="register-title">
        <span class="eyebrow">NEW ACCOUNT</span>
        <h1 id="register-title">Start with clarity.</h1>
        <p class="intro">Create a User account to submit and track your own claims. Administrator access is never available through public registration.</p>
        <div class="error" *ngIf="errorMessage()" role="alert">{{ errorMessage() }}</div>
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <label for="displayName">Display name <span>(optional)</span></label>
          <input id="displayName" type="text" autocomplete="name" formControlName="displayName" />
          <small class="field-error" *ngIf="form.controls.displayName.touched && form.controls.displayName.invalid">Use 200 characters or fewer.</small>
          <label for="email">Email address</label>
          <input id="email" type="email" autocomplete="email" formControlName="email" [attr.aria-invalid]="form.controls.email.invalid && form.controls.email.touched" />
          <small class="field-error" *ngIf="form.controls.email.touched && form.controls.email.invalid">Enter a valid email address.</small>
          <label for="password">Password</label>
          <input id="password" type="password" autocomplete="new-password" formControlName="password" />
          <small class="field-error" *ngIf="form.controls.password.touched && form.controls.password.invalid">Use 12–128 characters.</small>
          <label for="confirmPassword">Confirm password</label>
          <input id="confirmPassword" type="password" autocomplete="new-password" formControlName="confirmPassword" />
          <small class="field-error" *ngIf="form.controls.confirmPassword.touched && form.controls.confirmPassword.value !== form.controls.password.value">Passwords must match.</small>
          <button class="submit" type="submit" [disabled]="pending">{{ pending ? 'Creating account…' : 'Create User account' }}</button>
        </form>
        <p class="alternate">Already registered? <a routerLink="/login">Sign in</a></p>
      </section>
    </main>
  `,
  styles: [`
    .auth-page { align-items: center; display: flex; justify-content: center; min-height: calc(100vh - 73px); padding: 38px 20px; }
    .auth-card { background: #fff; border: 1px solid #e7ebf2; border-radius: 20px; box-shadow: 0 20px 60px #1720330b; max-width: 440px; padding: 36px; width: 100%; }
    .eyebrow { color: #5367e8; font-size: .7rem; font-weight: 800; letter-spacing: .18em; }
    h1 { font-size: 2.7rem; letter-spacing: -.07em; margin: 13px 0 8px; }
    .intro, .alternate { color: #718096; line-height: 1.55; }
    form { display: grid; gap: 8px; margin-top: 23px; }
    label { font-size: .83rem; font-weight: 800; margin-top: 7px; }
    label span { color: #718096; font-weight: 500; }
    input { border: 1px solid #dce1ec; border-radius: 9px; box-sizing: border-box; font: inherit; padding: 12px; width: 100%; }
    input:focus { border-color: #5367e8; box-shadow: 0 0 0 3px #5367e826; outline: none; }
    .field-error { color: #a33a30; font-size: .76rem; }
    .error { background: #fff0f0; border: 1px solid #f5c2c2; border-radius: 9px; color: #8a1c1c; font-size: .82rem; line-height: 1.45; margin-top: 20px; padding: 11px; }
    .submit { background: #5367e8; border: 0; border-radius: 9px; color: #fff; cursor: pointer; font: inherit; font-weight: 800; margin-top: 12px; padding: 13px; }
    .submit:disabled { cursor: wait; opacity: .55; }
    .alternate { font-size: .84rem; margin: 24px 0 0; text-align: center; }
    a { color: #5367e8; font-weight: 800; }
  `],
})
export class RegisterPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly errorMessage = signal<string | null>(null);
  readonly form = new FormGroup({
    displayName: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(256)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(12), Validators.maxLength(128)] }),
    confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  pending = false;

  submit(): void {
    if (this.form.invalid || this.form.controls.password.value !== this.form.controls.confirmPassword.value || this.pending) {
      this.form.markAllAsTouched();
      return;
    }

    this.pending = true;
    this.errorMessage.set(null);
    this.auth.register({
      email: this.form.controls.email.value,
      password: this.form.controls.password.value,
      displayName: this.form.controls.displayName.value.trim() || null,
    }).subscribe({
      next: () => void this.router.navigateByUrl('/dashboard'),
      error: error => {
        this.pending = false;
        this.errorMessage.set(this.apiError(error, 'Registration failed. Check the form and try again.'));
      },
      complete: () => { this.pending = false; },
    });
  }

  private apiError(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse && typeof error.error?.error === 'string') {
      return error.error.error;
    }

    if (error instanceof HttpErrorResponse && Array.isArray(error.error?.errors)) {
      return error.error.errors.join(' ');
    }

    return fallback;
  }
}
