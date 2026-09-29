import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-reset-password-page',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  template: `
    <main class="auth-page">
      <section class="auth-card" aria-labelledby="reset-title">
        <span class="eyebrow">ACCOUNT RECOVERY</span>
        <h1 id="reset-title">Choose a new password.</h1>
        <p class="intro" *ngIf="!complete()">Use at least 12 characters, including uppercase and lowercase letters, a number, and a symbol.</p>
        <div class="error" *ngIf="errorMessage()" role="alert">{{ errorMessage() }}</div>
        <form *ngIf="!complete() && token" [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <label for="email">Email address</label>
          <input id="email" type="email" autocomplete="email" formControlName="email" [attr.aria-invalid]="form.controls.email.invalid && form.controls.email.touched" />
          <small class="field-error" *ngIf="form.controls.email.touched && form.controls.email.invalid">Enter a valid email address.</small>
          <label for="password">New password</label>
          <input id="password" type="password" autocomplete="new-password" formControlName="password" />
          <small class="field-error" *ngIf="form.controls.password.touched && form.controls.password.invalid">Use 12–128 characters with uppercase, lowercase, a number, and a symbol.</small>
          <label for="confirmPassword">Confirm new password</label>
          <input id="confirmPassword" type="password" autocomplete="new-password" formControlName="confirmPassword" />
          <small class="field-error" *ngIf="form.controls.confirmPassword.touched && form.controls.confirmPassword.value !== form.controls.password.value">Passwords must match.</small>
          <button class="submit" type="submit" [disabled]="pending">{{ pending ? 'Updating…' : 'Reset password' }}</button>
        </form>
        <p class="success" *ngIf="complete()" role="status">Your password has been changed. You can sign in with the new password.</p>
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
    form { display: grid; gap: 8px; margin-top: 22px; }
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
export class ResetPasswordPageComponent {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  readonly token = this.route.snapshot.queryParamMap.get('token');
  readonly complete = signal(false);
  readonly errorMessage = signal(this.token ? null : 'This reset link is invalid or incomplete. Request a new link to continue.');
  readonly form = new FormGroup({
    email: new FormControl(this.route.snapshot.queryParamMap.get('email') ?? '', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(256)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(12), Validators.maxLength(128), Validators.pattern(/(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9])/)] }),
    confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  pending = false;

  submit(): void {
    if (this.form.invalid || this.form.controls.password.value !== this.form.controls.confirmPassword.value || !this.token || this.pending) {
      this.form.markAllAsTouched();
      return;
    }

    this.pending = true;
    this.errorMessage.set(null);
    this.auth.resetPassword({
      email: this.form.controls.email.value.trim(),
      token: this.token,
      newPassword: this.form.controls.password.value,
    }).subscribe({
      next: () => this.complete.set(true),
      error: error => {
        this.pending = false;
        this.errorMessage.set(error instanceof HttpErrorResponse && typeof error.error?.error === 'string'
          ? error.error.error
          : 'The reset link may have expired. Request a new link and try again.');
      },
      complete: () => { this.pending = false; },
    });
  }
}