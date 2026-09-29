import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-forgot-password-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <main class="auth-page">
      <section class="auth-card" aria-labelledby="recovery-title">
        <span class="eyebrow">ACCOUNT RECOVERY</span>
        <h1 id="recovery-title">Forgot your password?</h1>
        <p class="intro" *ngIf="!submitted()">Enter the email address associated with your account. If it matches an active account, we’ll send a reset link.</p>
        <p class="success" *ngIf="submitted()" role="status">If an active account matches that email, password reset instructions will be sent. Check your inbox.</p>
        <div class="error" *ngIf="errorMessage()" role="alert">{{ errorMessage() }}</div>
        <form *ngIf="!submitted()" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <label for="email">Email address</label>
          <input id="email" type="email" autocomplete="email" formControlName="email" [attr.aria-invalid]="form.controls.email.invalid && form.controls.email.touched" />
          <small class="field-error" *ngIf="form.controls.email.touched && form.controls.email.invalid">Enter a valid email address.</small>
          <button class="submit" type="submit" [disabled]="pending">{{ pending ? 'Sending…' : 'Send reset link' }}</button>
        </form>
        <p class="alternate"><a routerLink="/login">Back to sign in</a></p>
      </section>
    </main>
  `,
  styles: [`
    .auth-page { align-items: center; display: flex; justify-content: center; min-height: calc(100vh - 73px); padding: 38px 20px; }
    .auth-card { background: #fff; border: 1px solid #e7ebf2; border-radius: 20px; box-shadow: 0 20px 60px #1720330b; max-width: 440px; padding: 36px; width: 100%; }
    .eyebrow { color: #5367e8; font-size: .7rem; font-weight: 800; letter-spacing: .18em; }
    h1 { font-size: 2.2rem; margin: 13px 0 8px; }
    .intro, .alternate { color: #718096; line-height: 1.55; }
    form { display: grid; gap: 8px; margin-top: 26px; }
    label { font-size: .83rem; font-weight: 800; margin-top: 7px; }
    input { border: 1px solid #dce1ec; border-radius: 9px; box-sizing: border-box; font: inherit; padding: 12px; width: 100%; }
    input:focus { border-color: #5367e8; box-shadow: 0 0 0 3px #5367e826; outline: none; }
    .field-error { color: #a33a30; font-size: .76rem; }
    .error { background: #fff0f0; border: 1px solid #f5c2c2; border-radius: 9px; color: #8a1c1c; font-size: .82rem; line-height: 1.45; margin-top: 20px; padding: 11px; }
    .success { background: #edf7f1; border: 1px solid #cce8d6; border-radius: 9px; color: #245a39; line-height: 1.5; margin-top: 24px; padding: 13px; }
    .submit { background: #5367e8; border: 0; border-radius: 9px; color: #fff; cursor: pointer; font: inherit; font-weight: 800; margin-top: 12px; padding: 13px; }
    .submit:disabled { cursor: wait; opacity: .55; }
    .alternate { font-size: .84rem; margin: 24px 0 0; text-align: center; }
    a { color: #5367e8; font-weight: 800; }
  `],
})
export class ForgotPasswordPageComponent {
  private readonly auth = inject(AuthService);
  readonly submitted = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(256)] }),
  });
  pending = false;

  submit(): void {
    if (this.form.invalid || this.pending) {
      this.form.markAllAsTouched();
      return;
    }

    this.pending = true;
    this.errorMessage.set(null);
    this.auth.requestPasswordReset(this.form.controls.email.value.trim()).subscribe({
      next: () => this.submitted.set(true),
      error: error => {
        this.pending = false;
        this.errorMessage.set(error instanceof HttpErrorResponse && typeof error.error?.error === 'string'
          ? error.error.error
          : 'We could not process the request. Please try again later.');
      },
      complete: () => { this.pending = false; },
    });
  }
}