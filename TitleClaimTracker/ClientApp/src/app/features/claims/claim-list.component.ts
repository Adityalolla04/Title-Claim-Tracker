import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ClaimStatus, ClaimSummary } from '../../core/models/claim.model';
import { ClaimApiService } from '../../core/services/claim-api.service';

@Component({
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `<main><header><div><p>MY CLAIMS</p><h1>Claim workspace</h1></div><a routerLink="/claims/new">Create claim</a></header>
  <section class="filters"><label>Status <select [(ngModel)]="status"><option value="">All statuses</option><option *ngFor="let value of statuses" [value]="value">{{ value }}</option></select></label><label>City <input [(ngModel)]="city" maxlength="100"></label><button (click)="load()" [disabled]="loading()">Apply filters</button></section>
  <p *ngIf="error()" role="alert">{{ error() }}</p><p *ngIf="loading()" role="status">Loading claims…</p><p *ngIf="!loading() && !error() && claims().length === 0">No submitted claims match these filters.</p>
  <table *ngIf="claims().length"><thead><tr><th>Claim</th><th>Property</th><th>Type</th><th>Status</th><th>Filed</th></tr></thead><tbody><tr *ngFor="let claim of claims()"><td><a [routerLink]="['/claims', claim.filingID]">#{{ claim.filingID }}</a></td><td>{{ claim.address }}, {{ claim.city }}, {{ claim.state }}</td><td>{{ claim.filingType }}</td><td>{{ claim.status }}</td><td>{{ claim.dateFiled | date }}</td></tr></tbody></table></main>`,
  styles: [`main{margin:auto;max-width:1100px;padding:42px 24px}header,.filters{align-items:end;display:flex;gap:16px;justify-content:space-between}h1{margin:4px 0}p{color:#62708a}a,button{color:#4255c8;font-weight:700}label{display:grid;gap:5px}input,select,button{padding:9px}table{border-collapse:collapse;margin-top:24px;width:100%}th,td{border-bottom:1px solid #dfe4ee;padding:12px;text-align:left}@media(max-width:650px){header,.filters{align-items:stretch;flex-direction:column}}`],
})
export class ClaimListComponent {
  private readonly api = inject(ClaimApiService); private readonly destroyRef = inject(DestroyRef);
  readonly claims = signal<ClaimSummary[]>([]); readonly loading = signal(false); readonly error = signal<string | null>(null);
  readonly statuses: ClaimStatus[] = ['New', 'Under Review', 'Escalated', 'Resolved', 'Closed', 'Pending Review', 'Active Investigation']; status = ''; city = '';
  constructor() { this.load(); }
  load(): void { this.loading.set(true); this.api.listClaims(this.status as ClaimStatus || undefined, undefined, this.city || undefined).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: page => { this.claims.set(page.items); this.error.set(null); this.loading.set(false); }, error: () => { this.error.set('Unable to load claims.'); this.loading.set(false); } }); }
}
