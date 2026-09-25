import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-forbidden-page',
  standalone: true,
  imports: [RouterLink],
  template: `
    <main class="message-page">
      <span class="eyebrow">403 · RESTRICTED</span>
      <h1>That workspace is for administrators.</h1>
      <p>Your account remains signed in, but it does not have the Admin role required for this area.</p>
      <a routerLink="/dashboard">Return to my workspace</a>
    </main>
  `,
  styles: [`
    .message-page { margin: 0 auto; max-width: 650px; padding: 110px 24px; text-align: center; }
    .eyebrow { color: #5367e8; font-size: .7rem; font-weight: 800; letter-spacing: .18em; }
    h1 { font-size: clamp(2rem, 5vw, 3.8rem); letter-spacing: -.07em; margin: 16px 0; }
    p { color: #718096; line-height: 1.7; }
    a { color: #5367e8; display: inline-block; font-weight: 800; margin-top: 25px; }
  `],
})
export class ForbiddenPageComponent {}
