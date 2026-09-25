import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-home-page',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <main class="home-page">
      <section class="hero">
        <div class="hero-copy">
          <span class="eyebrow">PROPERTY-DISPUTE INTAKE</span>
          <h1>Turn title uncertainty into an organized next step.</h1>
          <p>Title Claim Intelligence helps teams capture property disputes, triage intake, and manage secure claim workflows without confusing a property owner with the account that submitted a claim.</p>
          <div class="hero-actions">
            <a class="primary" routerLink="/register">Start a secure intake</a>
            <a class="secondary" routerLink="/login">Sign in</a>
          </div>
        </div>
        <div class="hero-panel" aria-label="Platform capabilities">
          <span class="panel-label">INTELLIGENCE DESK</span>
          <div class="signal"><b>01</b><span><strong>Structured intake</strong><small>Capture address, owner, dates, and issue context.</small></span></div>
          <div class="signal"><b>02</b><span><strong>Explainable triage</strong><small>Separate workflow priority from model confidence.</small></span></div>
          <div class="signal"><b>03</b><span><strong>Auditable workflow</strong><small>Keep status history and ownership boundaries visible.</small></span></div>
        </div>
      </section>
      <section class="capabilities" aria-labelledby="capability-title">
        <span class="eyebrow">BUILT FOR CLARITY</span>
        <h2 id="capability-title">One workspace for the first mile of a property dispute.</h2>
        <div class="capability-grid">
          <article><span>◈</span><h3>Assisted intake</h3><p>Use classification and extraction as decision support, with an editable manual path when information is missing or uncertain.</p></article>
          <article><span>◌</span><h3>Secure claims</h3><p>Authenticated accounts see only their own active claims. Admin actions remain protected on the server.</p></article>
          <article><span>◫</span><h3>Operational insight</h3><p>Prepare traceable workflow data for review, reporting, and future Tableau analytics.</p></article>
        </div>
      </section>
    </main>
  `,
  styles: [`
    .home-page { margin: 0 auto; max-width: 1240px; padding: clamp(42px, 7vw, 96px) 24px 70px; }
    .hero { align-items: center; display: grid; gap: clamp(35px, 7vw, 100px); grid-template-columns: minmax(0, 1.1fr) minmax(300px, .8fr); min-height: 510px; }
    .eyebrow { color: #5367e8; font-size: .7rem; font-weight: 800; letter-spacing: .18em; }
    h1 { font-size: clamp(2.6rem, 6vw, 5.4rem); letter-spacing: -.075em; line-height: .97; margin: 17px 0 25px; max-width: 760px; }
    .hero-copy p { color: #718096; font-size: 1.08rem; line-height: 1.75; max-width: 650px; }
    .hero-actions { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 32px; }
    .hero-actions a { border-radius: 10px; font-weight: 800; padding: 13px 18px; text-decoration: none; }
    .primary { background: #5367e8; color: #fff; }
    .secondary { border: 1px solid #dce1ec; color: #172033; }
    .hero-panel { background: #172033; border-radius: 20px; box-shadow: 0 25px 70px #17203326; color: #fff; padding: 28px; }
    .panel-label { color: #aeb8ff; font-size: .68rem; font-weight: 800; letter-spacing: .17em; }
    .signal { align-items: flex-start; border-bottom: 1px solid #ffffff1c; display: flex; gap: 16px; padding: 27px 0; }
    .signal:last-child { border-bottom: 0; padding-bottom: 0; }
    .signal b { color: #aeb8ff; font-size: .75rem; }
    .signal strong, .signal small { display: block; }
    .signal strong { font-size: 1rem; }
    .signal small { color: #aeb5c5; line-height: 1.55; margin-top: 6px; }
    .capabilities { border-top: 1px solid #e7ebf2; padding-top: 48px; }
    h2 { font-size: clamp(1.8rem, 4vw, 3rem); letter-spacing: -.05em; margin: 13px 0 32px; max-width: 650px; }
    .capability-grid { display: grid; gap: 17px; grid-template-columns: repeat(3, 1fr); }
    article { background: #fff; border: 1px solid #e7ebf2; border-radius: 16px; padding: 24px; }
    article > span { color: #5367e8; font-size: 1.6rem; }
    h3 { margin: 17px 0 9px; }
    article p { color: #718096; line-height: 1.6; margin: 0; }
    @media (max-width: 800px) { .hero { grid-template-columns: 1fr; min-height: 0; } .capability-grid { grid-template-columns: 1fr; } }
  `],
})
export class HomePageComponent {}
