import { Routes } from '@angular/router';
import { adminGuard } from './core/guards/admin.guard';
import { authGuard } from './core/guards/auth.guard';
import { ChatbotComponent } from './features/chatbot/chatbot.component';
import { AdminPageComponent } from './features/admin/admin-page.component';
import { ForbiddenPageComponent } from './features/auth/forbidden-page.component';
import { LoginPageComponent } from './features/auth/login-page.component';
import { RegisterPageComponent } from './features/auth/register-page.component';
import { HomePageComponent } from './features/home/home-page.component';
import { ClaimCreateComponent } from './features/claims/claim-create.component';
import { ClaimDetailComponent } from './features/claims/claim-detail.component';
import { ClaimListComponent } from './features/claims/claim-list.component';

export const routes: Routes = [
  { path: '', component: HomePageComponent, title: 'Title Claim Intelligence' },
  { path: 'login', component: LoginPageComponent, title: 'Sign in | Title Claim Intelligence' },
  { path: 'register', component: RegisterPageComponent, title: 'Register | Title Claim Intelligence' },
  { path: 'forbidden', component: ForbiddenPageComponent, title: 'Access denied | Title Claim Intelligence' },
  { path: 'dashboard', canActivate: [authGuard], component: ChatbotComponent, title: 'Dashboard | Title Claim Intelligence' },
  { path: 'claims', canActivate: [authGuard], component: ClaimListComponent, title: 'My Claims | Title Claim Intelligence' },
  { path: 'claims/new', canActivate: [authGuard], component: ClaimCreateComponent, title: 'Create Claim | Title Claim Intelligence' },
  { path: 'claims/:id', canActivate: [authGuard], component: ClaimDetailComponent, title: 'Claim Details | Title Claim Intelligence' },
  { path: 'admin', canActivate: [adminGuard], component: AdminPageComponent, title: 'Admin | Title Claim Intelligence' },
  { path: '**', redirectTo: '' },
];
