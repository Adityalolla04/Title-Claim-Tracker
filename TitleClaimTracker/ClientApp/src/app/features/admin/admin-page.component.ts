import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-admin-page',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <main class="admin-page">
      <section class="admin-card">
        <span class="eyebrow">ADMIN WORKSPACE</span>
        <h1>Operations control room.</h1>
        <p>This route is protected by the server-backed Admin role. Claims, triage review, audit history, reports, and analytics are being added to this protected workspace in the next application phases.</p>
        <div class="boundary"><strong>Security boundary</strong><span>Client route guards improve navigation, but every admin API remains protected by ASP.NET Core authorization.</span></div>
        <a routerLink="/dashboard">Open my workspace</a>
      </section>
    </main>
  `,
  styles: [`
    .admin-page { margin: 0 auto; max-width: 850px; padding: 70px 24px; }
    .admin-card { background: #fff; border: 1px solid #e7ebf2; border-radius: 20px; padding: clamp(27px, 5vw, 52px); }
    .eyebrow { color: #5367e8; font-size: .7rem; font-weight: 800; letter-spacing: .18em; }
    h1 { font-size: clamp(2.3rem, 6vw, 4.6rem); letter-spacing: -.08em; margin: 15px 0; }
    p { color: #718096; line-height: 1.7; max-width: 650px; }
    .boundary { background: #f5f7fb; border-left: 3px solid #5367e8; display: grid; gap: 5px; margin: 28px 0; padding: 15px 17px; }
    .boundary span { color: #718096; font-size: .9rem; line-height: 1.5; }
    a { color: #5367e8; font-weight: 800; }
  `],
})
export class AdminPageComponent {
  readonly auth = inject(AuthService);
}
