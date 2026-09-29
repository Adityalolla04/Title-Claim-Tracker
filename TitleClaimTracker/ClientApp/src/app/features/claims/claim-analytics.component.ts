import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ClaimAnalytics } from '../../core/models/claim.model';
import { AuthService } from '../../core/services/auth.service';
import { ClaimApiService } from '../../core/services/claim-api.service';

@Component({
  standalone: true,
  imports: [CommonModule],
  template: `
    <main class="analytics-page">
      <header class="page-heading">
        <div>
          <p class="eyebrow">{{ auth.isAdministrator() ? 'ADMINISTRATOR VIEW' : 'PERSONAL WORKSPACE' }}</p>
          <h1>{{ auth.isAdministrator() ? 'All-account analytics' : 'My claim analytics' }}</h1>
          <p class="muted">Filed claims and first recorded resolutions for the last six months.</p>
        </div>
      </header>

      <p class="feedback" *ngIf="error()" role="alert">{{ error() }}</p>
      <p class="muted" *ngIf="loading()" role="status">Loading account-scoped analytics…</p>

      <ng-container *ngIf="summary() as data">
        <section class="metrics" aria-label="Claim analytics summary">
          <article><span>Total active claims</span><strong>{{ data.totalClaims }}</strong></article>
          <article><span>Open backlog</span><strong>{{ data.openClaims }}</strong></article>
          <article><span>Resolved or closed</span><strong>{{ data.resolvedClaims }}</strong></article>
          <article><span>Filed in last six months</span><strong>{{ filedTotal(data) }}</strong></article>
        </section>

        <section class="comparison" aria-labelledby="comparison-title">
          <div class="section-heading">
            <div>
              <h2 id="comparison-title">Monthly filed vs solved</h2>
              <p class="muted">Filed uses filing date. Solved uses the first recorded transition to Resolved or Closed.</p>
            </div>
            <div class="legend"><span><i class="filed-key"></i>Filed</span><span><i class="solved-key"></i>Solved</span></div>
          </div>

          <div class="month-row" *ngFor="let month of data.monthlySubmissions">
            <strong class="month-label">{{ monthLabel(month.year, month.month) }}</strong>
            <div class="series">
              <span class="series-label">Filed</span>
              <div class="track"><i class="filed-bar" [style.width.%]="barWidth(month.count, maximum(data))"></i></div>
              <b>{{ month.count }}</b>
            </div>
            <div class="series">
              <span class="series-label">Solved</span>
              <div class="track"><i class="solved-bar" [style.width.%]="barWidth(month.solvedCount, maximum(data))"></i></div>
              <b>{{ month.solvedCount }}</b>
            </div>
          </div>

          <p class="empty" *ngIf="!data.totalClaims">No active claim activity is available for this account.</p>
          <p class="note">The API limits claimant accounts to their own active records. Administrator totals include active claims across accounts. Solved-month counts depend on recorded status history.</p>
        </section>

        <section class="breakdowns">
          <div>
            <h2>Claims by status</h2>
            <div class="breakdown-row" *ngFor="let item of data.statusBreakdown"><span>{{ item.label }}</span><b>{{ item.count }}</b></div>
          </div>
          <div>
            <h2>Claims by category</h2>
            <div class="breakdown-row" *ngFor="let item of data.typeBreakdown"><span>{{ item.label }}</span><b>{{ item.count }}</b></div>
          </div>
        </section>
      </ng-container>
    </main>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; background: #f4f7f8; color: #192b34; }
    .analytics-page { max-width: 1120px; margin: auto; padding: 36px 24px 64px; }
    .page-heading { margin-bottom: 24px; }
    .eyebrow { color: #147b76; font-size: .72rem; font-weight: 800; }
    h1 { margin: 7px 0; font-size: 2rem; }
    h2 { margin: 0 0 14px; font-size: 1.05rem; }
    .muted, .note { color: #65767b; line-height: 1.5; }
    .metrics { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; margin-bottom: 22px; }
    .metrics article, .comparison, .breakdowns > div { background: #fff; border: 1px solid #dce6e5; }
    .metrics article { padding: 16px 18px; }
    .metrics span, .metrics strong { display: block; }
    .metrics span { color: #627579; font-size: .82rem; }
    .metrics strong { margin-top: 8px; font-size: 1.65rem; }
    .comparison { padding: 22px; }
    .section-heading { display: flex; align-items: start; justify-content: space-between; gap: 16px; margin-bottom: 18px; }
    .section-heading p { margin: 0; font-size: .84rem; }
    .legend { display: flex; gap: 14px; white-space: nowrap; color: #65767b; font-size: .78rem; }
    .legend span { display: flex; align-items: center; gap: 6px; }
    .legend i { width: 10px; height: 10px; }
    .filed-key, .filed-bar { background: #147b76; }
    .solved-key, .solved-bar { background: #d1843c; }
    .month-row { display: grid; grid-template-columns: 92px minmax(0, 1fr); gap: 8px 14px; align-items: center; padding: 12px 0; border-top: 1px solid #edf1f0; }
    .month-label { grid-row: span 2; font-size: .84rem; }
    .series { display: grid; grid-template-columns: 48px minmax(80px, 1fr) 30px; align-items: center; gap: 10px; font-size: .78rem; }
    .series-label { color: #65767b; }
    .series b { text-align: right; }
    .track { height: 10px; overflow: hidden; background: #edf2f1; }
    .track i { display: block; height: 100%; min-width: 0; }
    .note { margin: 18px 0 0; font-size: .78rem; }
    .breakdowns { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-top: 20px; }
    .breakdowns > div { padding: 18px 20px; }
    .breakdown-row { display: flex; justify-content: space-between; gap: 16px; padding: 8px 0; border-top: 1px solid #edf1f0; font-size: .84rem; }
    .empty { color: #65767b; padding: 12px 0; }
    .feedback { color: #9b392e; }
    @media (max-width: 700px) {
      .analytics-page { padding: 24px 15px 44px; }
      .metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .section-heading { flex-direction: column; }
      .month-row { grid-template-columns: 66px minmax(0, 1fr); gap-inline: 8px; }
      .series { grid-template-columns: 42px minmax(48px, 1fr) 24px; gap: 6px; }
      .breakdowns { grid-template-columns: 1fr; }
    }
  `],
})
export class ClaimAnalyticsComponent {
  readonly auth = inject(AuthService);
  private readonly api = inject(ClaimApiService);
  private readonly destroyRef = inject(DestroyRef);
  readonly summary = signal<ClaimAnalytics | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  constructor() {
    this.api.getClaimAnalytics().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: value => {
        this.summary.set(value);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Analytics could not be loaded.');
        this.loading.set(false);
      },
    });
  }

  maximum(summary: ClaimAnalytics): number {
    return Math.max(1, ...summary.monthlySubmissions.flatMap(month => [month.count, month.solvedCount]));
  }

  barWidth(value: number, maximum: number): number {
    return maximum ? (value ? Math.max(2, value / maximum * 100) : 0) : 0;
  }

  filedTotal(summary: ClaimAnalytics): number {
    return summary.monthlySubmissions.reduce((total, month) => total + month.count, 0);
  }

  monthLabel(year: number, month: number): string {
    return new Date(year, month - 1, 1).toLocaleString(undefined, { month: 'short', year: '2-digit' });
  }
}