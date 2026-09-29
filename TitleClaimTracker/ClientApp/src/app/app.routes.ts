import { CanActivateFn, Routes, Router } from '@angular/router';
import { inject } from '@angular/core';
import { catchError, map, of } from 'rxjs';
import { adminGuard } from './core/guards/admin.guard';
import { authGuard } from './core/guards/auth.guard';
import { AuthService } from './core/services/auth.service';
import { ChatbotComponent } from './features/chatbot/chatbot.component';
import { AdminPageComponent } from './features/admin/admin-page.component';
import { ForbiddenPageComponent } from './features/auth/forbidden-page.component';
import { LoginPageComponent } from './features/auth/login-page.component';
import { RegisterPageComponent } from './features/auth/register-page.component';
import { ForgotPasswordPageComponent } from './features/auth/forgot-password-page.component';
import { ResetPasswordPageComponent } from './features/auth/reset-password-page.component';
import { HomePageComponent } from './features/home/home-page.component';
import { ClaimDetailComponent } from './features/claims/claim-detail.component';
import { ClaimListComponent } from './features/claims/claim-list.component';
import { ClaimAnalyticsComponent } from './features/claims/claim-analytics.component';

const publicHomeGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.ensureSession().pipe(
    map(account => account ? router.createUrlTree(['/claims']) : true),
    catchError(() => of(true)),
  );
};

const sessionRestoreGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.ensureSession().pipe(
    map(() => true),
    catchError(() => of(true)),
  );
};

export const routes: Routes = [
  { path: '', canActivate: [publicHomeGuard], component: HomePageComponent, title: 'Title Claim Intelligence' },
  { path: 'login', canActivate: [sessionRestoreGuard], component: LoginPageComponent, title: 'Sign in | Title Claim Intelligence' },
  { path: 'admin/login', canActivate: [sessionRestoreGuard], component: LoginPageComponent, data: { adminLogin: true }, title: 'Administrator sign in | Title Claim Intelligence' },
  { path: 'register', component: RegisterPageComponent, title: 'Register | Title Claim Intelligence' },
  { path: 'forgot-password', component: ForgotPasswordPageComponent, title: 'Forgot password | Title Claim Intelligence' },
  { path: 'reset-password', component: ResetPasswordPageComponent, title: 'Reset password | Title Claim Intelligence' },
  { path: 'forbidden', component: ForbiddenPageComponent, title: 'Access denied | Title Claim Intelligence' },
  { path: 'dashboard', canActivate: [authGuard], component: ChatbotComponent, title: 'Triage | Title Claim Intelligence' },
  { path: 'claims', canActivate: [authGuard], component: ClaimListComponent, title: 'My Claims | Title Claim Intelligence' },
  { path: 'analytics', canActivate: [authGuard], component: ClaimAnalyticsComponent, title: 'Analytics | Title Claim Intelligence' },
  { path: 'claims/new', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'claims/:id', canActivate: [authGuard], component: ClaimDetailComponent, title: 'Claim Details | Title Claim Intelligence' },
  { path: 'admin', canActivate: [adminGuard], component: AdminPageComponent, title: 'Admin | Title Claim Intelligence' },
  { path: '**', redirectTo: '' },
];
