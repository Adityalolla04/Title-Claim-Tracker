import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { switchMap } from 'rxjs';
import { ClaimDetail, ClaimDocument, ClaimDocumentAnalysis, ClaimStatus, FilingType } from '../../core/models/claim.model';
import { ClaimApiService } from '../../core/services/claim-api.service';
import { AuthService } from '../../core/services/auth.service';

@Component({
	standalone: true,
	imports: [CommonModule, ReactiveFormsModule, RouterLink],
	template: `<main class="claim-detail"><a class="back-link no-print" routerLink="/claims">Back to claims</a><p *ngIf="loading()" role="status">Loading claim…</p><p *ngIf="error()" class="error" role="alert">{{ error() }}</p><p *ngIf="message()" class="success" role="status">{{ message() }}</p><article *ngIf="claim() as item"><header class="detail-heading"><div><span class="eyebrow">CLAIM #{{ item.filingID }}</span><h1>{{ item.filingType }}</h1><span class="status">{{ item.status }}</span></div><div class="actions no-print"><button type="button" *ngIf="canManage(item) && !editing()" (click)="startEditing(item)">Edit claim</button><button type="button" class="danger" *ngIf="canManage(item) && !editing()" (click)="deleteClaim(item)">Delete claim</button><button type="button" (click)="printRecord()">Print / Save as PDF</button></div></header>
	<p class="disclaimer">Claim record copy. This is not an official court or government filing.</p>
	<section *ngIf="auth.isAdministrator()" class="admin-status no-print"><h2>Update claim status</h2><form [formGroup]="statusForm" (ngSubmit)="updateStatus(item)"><div class="field-grid"><label>New status<select formControlName="status"><option *ngFor="let status of statuses" [value]="status">{{ status }}</option></select></label><label class="wide">Reason for change<textarea formControlName="reason" maxlength="500" rows="2"></textarea></label></div><div class="actions"><button type="submit" [disabled]="saving() || statusForm.invalid">{{ saving() ? 'Saving…' : 'Save status' }}</button></div></form></section>
	<form *ngIf="editing()" [formGroup]="form" (ngSubmit)="save(item)" class="edit-form no-print"><h2>Edit claim details</h2><div class="field-grid"><label>Claimant name<input formControlName="claimantName" maxlength="255"></label><label>Claim category<select formControlName="filingType"><option *ngFor="let type of filingTypes" [value]="type">{{ type }}</option></select></label><label>Property address<input formControlName="address" maxlength="255"></label><label>City<input formControlName="city" maxlength="100"></label><label>State<input formControlName="state" maxlength="2"></label><label class="wide">Legal description<textarea formControlName="legalDescription" maxlength="2000"></textarea></label><label class="wide">Issue description<textarea formControlName="notes" maxlength="4000"></textarea></label></div><div class="actions"><button type="button" class="secondary" (click)="editing.set(false)">Cancel</button><button type="submit" [disabled]="saving() || form.invalid">{{ saving() ? 'Saving…' : 'Save changes' }}</button></div></form>
	<section class="record-copy"><h2>Claim details</h2><dl><dt>Claimant</dt><dd>{{ item.claimantName || 'Not supplied' }}</dd><dt>Property</dt><dd>{{ item.address }}, {{ item.city }}, {{ item.state }}</dd><dt>Category</dt><dd>{{ item.filingType }}</dd><dt>Date filed</dt><dd>{{ item.dateFiled | date:'longDate' }}</dd><dt>Issue description</dt><dd>{{ item.notes || 'Not supplied' }}</dd><dt *ngIf="item.legalDescription">Legal description</dt><dd *ngIf="item.legalDescription">{{ item.legalDescription }}</dd><dt>Intake type</dt><dd>{{ item.creationSource }}</dd></dl></section>
	<section class="record-copy" *ngIf="auth.isAdministrator()"><h2>Internal triage assessment</h2><p class="internal-note">Administrator-only. Classifier scores are uncalibrated model outputs and not probabilities of legal correctness.</p><dl><dt>Classification score</dt><dd>{{ item.classificationScore === null ? 'Not available' : (item.classificationScore | percent:'1.0-1') }}</dd><dt>Operational risk heuristic</dt><dd>{{ item.riskScore === null ? 'Not available' : (item.riskScore | percent:'1.0-1') }}</dd><dt>Triage priority</dt><dd>{{ item.triagePriority || 'Not available' }}</dd></dl></section>
	<section class="record-copy documents"><h2>Supporting documents</h2><p *ngIf="auth.isAdministrator()" class="internal-note">Analysis uses the configured intake provider. The default is local deterministic extraction; only configure providers approved for claim data. Scanned PDFs are not OCR'd.</p><p *ngIf="documentsError()" class="error" role="alert">{{ documentsError() }}</p><ul *ngIf="documents().length"><li *ngFor="let document of documents()"><div class="document-row"><div><a [href]="documentUrl(item.filingID, document.claimDocumentID)" target="_blank" rel="noopener">{{ document.fileName }}</a><span>{{ document.fileSizeBytes / 1024 | number:'1.0-0' }} KB · {{ document.uploadedUtc | date:'mediumDate' }}</span></div><button *ngIf="auth.isAdministrator()" type="button" (click)="analyzeDocument(item.filingID, document.claimDocumentID)" [disabled]="analyzingDocument() === document.claimDocumentID">{{ analyzingDocument() === document.claimDocumentID ? 'Analyzing…' : 'Analyze document' }}</button></div><p *ngIf="analysisError() && analyzingDocument() === null" class="error" role="alert">{{ analysisError() }}</p><section *ngIf="documentAnalyses()[document.claimDocumentID] as analysis" class="analysis-result"><strong>Administrator review · {{ analysis.extractionMode }} · {{ analysis.analyzedPages }} of {{ analysis.totalPages }} pages</strong><p><b>{{ analysis.extractionMode === 'deterministic' ? 'Text preview' : 'Summary' }}:</b> {{ analysis.summary }}</p><dl><ng-container *ngIf="analysis.claimantName"><dt>Claimant extracted</dt><dd>{{ analysis.claimantName }}</dd></ng-container><ng-container *ngIf="analysis.address || analysis.city || analysis.state"><dt>Property extracted</dt><dd>{{ analysis.address }}{{ analysis.city ? ', ' + analysis.city : '' }}{{ analysis.state ? ', ' + analysis.state : '' }}</dd></ng-container><ng-container *ngIf="analysis.issueDescription"><dt>Issue text</dt><dd>{{ analysis.issueDescription }}</dd></ng-container><ng-container *ngIf="analysis.relevantDates.length"><dt>Dates found</dt><dd>{{ analysis.relevantDates.join(', ') }}</dd></ng-container><ng-container *ngIf="analysis.missingFields.length"><dt>Fields not found</dt><dd>{{ analysis.missingFields.join(', ') }}</dd></ng-container></dl><small>Assistive extraction only. Verify every fact against the original PDF; this is not a legal determination.</small></section></li></ul><p *ngIf="!documentsError() && !documents().length">No supporting PDFs attached.</p></section>
	<section class="timeline"><h2>Status history</h2><ol><li *ngFor="let event of item.statusTimeline"><div><strong>{{ event.toStatus }}</strong><span>{{ event.transitionedUtc | date:'medium' }}</span></div><p *ngIf="event.reason">{{ event.reason }}</p></li></ol></section></article></main>`,
	styles: [`:host{display:block;min-height:100vh;background:#f4f7f8;color:#192b34}.claim-detail{max-width:960px;margin:auto;padding:30px 24px 64px}.back-link{color:#117d77;font-weight:700;text-decoration:none}.detail-heading{display:flex;justify-content:space-between;align-items:center;gap:20px;margin-top:24px}.eyebrow{color:#117d77;font-size:.72rem;font-weight:800;letter-spacing:.12em}.detail-heading h1{font-size:2rem;margin:8px 0}.status{display:inline-block;background:#e4f0ee;color:#315f5b;padding:5px 9px;font-size:.82rem}.actions{display:flex;align-items:center;gap:8px;flex-wrap:wrap}.actions button,.document-row button{border:1px solid #117d77;border-radius:4px;background:#117d77;color:white;padding:10px 13px;font:inherit;font-weight:700;cursor:pointer}.actions button.secondary{background:#fff;color:#117d77}.actions button.danger{background:#fff;border-color:#c27668;color:#9b4033}.disclaimer{padding:12px 14px;margin:20px 0;background:#f7f5ee;border-left:3px solid #d1843c;color:#68583d;font-size:.85rem}.record-copy,.timeline,.edit-form,.admin-status{background:white;border:1px solid #dce6e5;padding:22px;margin-top:16px}.record-copy h2,.timeline h2,.edit-form h2,.admin-status h2{font-size:1.1rem;margin:0 0 16px}.record-copy dl{display:grid;grid-template-columns:170px 1fr;gap:12px 18px;margin:0}.record-copy dt{font-weight:800;color:#52666b}.record-copy dd{margin:0;white-space:pre-wrap;overflow-wrap:anywhere}.internal-note{color:#65777b;font-size:.84rem;line-height:1.5}.documents ul{list-style:none;padding:0;margin:0}.documents li{display:block;padding:10px 0;border-bottom:1px solid #e8efee}.documents li a{color:#117d77;font-weight:700}.documents li span{color:#718388;font-size:.82rem}.document-row{display:flex;justify-content:space-between;align-items:center;gap:12px}.document-row>div{display:flex;flex-direction:column;gap:4px}.document-row button{padding:7px 10px;font-size:.8rem}.analysis-result{margin:12px 0 2px;padding:14px;background:#f4f8f7;border-left:3px solid #6a9c97;color:#43585d}.analysis-result>strong{font-size:.82rem}.analysis-result>p{line-height:1.5}.analysis-result dl{display:grid;grid-template-columns:130px 1fr;gap:7px 12px}.analysis-result dt{font-weight:700}.analysis-result dd{margin:0;overflow-wrap:anywhere}.analysis-result small{display:block;color:#65777b;line-height:1.45}.timeline ol{list-style:none;padding:0;margin:0}.timeline li{border-left:3px solid #6a9c97;margin:14px 0;padding:4px 12px}.timeline li div{display:flex;justify-content:space-between;gap:12px}.timeline li span{color:#718388;font-size:.82rem}.timeline li p{color:#64777b;margin:6px 0}.field-grid{display:grid;grid-template-columns:1fr 1fr;gap:14px}.field-grid label{display:grid;gap:6px;color:#52666b;font-size:.83rem;font-weight:700}.field-grid .wide{grid-column:1/-1}.field-grid input,.field-grid select,.field-grid textarea{width:100%;box-sizing:border-box;border:1px solid #cbd8d6;border-radius:4px;padding:10px;font:inherit}.field-grid textarea{min-height:88px}.edit-form>.actions,.admin-status .actions{justify-content:flex-end;margin-top:16px}.error{color:#9b3028}.success{color:#176951}@media print{.no-print,.back-link{display:none!important}.claim-detail{max-width:none;padding:0}.record-copy,.timeline{border:0;padding:0}.claim-detail{background:white}.disclaimer{border:1px solid #d1843c}.detail-heading{margin-top:0}}@media(max-width:640px){.claim-detail{padding:24px 14px}.detail-heading{align-items:flex-start;flex-direction:column}.record-copy,.timeline,.edit-form,.admin-status{padding:16px}.record-copy dl{grid-template-columns:1fr;gap:4px}.record-copy dd{margin-bottom:10px}.document-row{align-items:flex-start;flex-direction:column}.analysis-result dl{grid-template-columns:1fr;gap:3px}.analysis-result dd{margin-bottom:8px}.field-grid{grid-template-columns:1fr}.field-grid .wide{grid-column:auto}.timeline li div{flex-direction:column;gap:3px}}`],
})
export class ClaimDetailComponent {
	private readonly api = inject(ClaimApiService);
	private readonly route = inject(ActivatedRoute);
	private readonly router = inject(Router);
	private readonly fb = inject(FormBuilder);
	private readonly destroyRef = inject(DestroyRef);
	readonly auth = inject(AuthService);
	readonly claim = signal<ClaimDetail | null>(null);
	readonly loading = signal(true);
	readonly saving = signal(false);
	readonly editing = signal(false);
	readonly error = signal<string | null>(null);
	readonly message = signal<string | null>(null);
	readonly documents = signal<ClaimDocument[]>([]);
	readonly documentsError = signal<string | null>(null);
	readonly documentAnalyses = signal<Record<number, ClaimDocumentAnalysis>>({});
	readonly analyzingDocument = signal<number | null>(null);
	readonly analysisError = signal<string | null>(null);
	readonly filingTypes: FilingType[] = ['Title Claim', 'Lien', 'Easement', 'Deed Dispute'];
	readonly form = this.fb.nonNullable.group({
		claimantName: ['', Validators.maxLength(255)], address: ['', [Validators.required, Validators.maxLength(255)]],
		city: ['', [Validators.required, Validators.maxLength(100)]], state: ['', [Validators.required, Validators.pattern(/^[a-zA-Z]{2}$/)]],
		filingType: ['Title Claim' as FilingType, Validators.required], legalDescription: ['', Validators.maxLength(2000)], notes: ['', Validators.maxLength(4000)],
	});
	readonly statuses: ClaimStatus[] = ['New', 'Under Review', 'Escalated', 'Resolved', 'Closed', 'Pending Review', 'Active Investigation'];
	readonly statusForm = this.fb.nonNullable.group({ status: ['New' as ClaimStatus, Validators.required], reason: ['', Validators.maxLength(500)] });

	constructor() {
		const id = Number(this.route.snapshot.paramMap.get('id'));
		if (!Number.isInteger(id)) { this.error.set('Claim not found.'); this.loading.set(false); return; }
		this.api.getClaim(id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
			next: claim => { this.claim.set(claim); this.statusForm.patchValue({ status: claim.status }); this.loading.set(false); this.loadDocuments(id); },
			error: failure => { this.error.set(failure.status === 404 ? 'Claim not found.' : 'Unable to load claim.'); this.loading.set(false); },
		});
	}

	documentUrl(filingID: number, documentID: number): string { return `/api/filings/${filingID}/documents/${documentID}`; }

	analyzeDocument(filingID: number, documentID: number): void {
		this.analyzingDocument.set(documentID);
		this.analysisError.set(null);
		this.api.analyzeClaimDocument(filingID, documentID).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
			next: analysis => { this.documentAnalyses.update(results => ({ ...results, [documentID]: analysis })); this.analyzingDocument.set(null); },
			error: failure => { this.analysisError.set(failure.status === 422 ? (failure.error?.error ?? 'No selectable PDF text was found.') : 'Document analysis failed. Confirm the attachment schema is installed and try again.'); this.analyzingDocument.set(null); },
		});
	}

	private loadDocuments(filingID: number): void {
		this.api.getClaimDocuments(filingID).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
			next: documents => { this.documents.set(documents); this.documentsError.set(null); },
			error: () => this.documentsError.set('Supporting documents could not be loaded. Verify the database has the 2.0.4 attachment upgrade.'),
		});
	}

	canManage(item: ClaimDetail): boolean { return this.auth.isAdministrator() || item.status === 'New'; }

	startEditing(item: ClaimDetail): void {
		this.message.set(null);
		this.error.set(null);
		this.form.patchValue({ claimantName: item.claimantName ?? '', address: item.address, city: item.city, state: item.state, filingType: item.filingType as FilingType, legalDescription: item.legalDescription ?? '', notes: item.notes ?? '' });
		this.editing.set(true);
	}

	save(item: ClaimDetail): void {
		if (this.form.invalid || this.saving()) { this.form.markAllAsTouched(); return; }
		const value = this.form.getRawValue();
		this.saving.set(true);
		this.api.updateClaim(item.filingID, { ...value, state: value.state.toUpperCase(), dateFiled: item.dateFiled, claimantName: value.claimantName || null, legalDescription: value.legalDescription || null, notes: value.notes || null, rowVersion: item.rowVersion }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
			next: claim => { this.claim.set(claim); this.editing.set(false); this.message.set('Claim details updated.'); this.saving.set(false); },
			error: failure => { this.error.set(failure.status === 409 ? 'This claim changed elsewhere. Reload it and retry your edit.' : 'Claim details could not be saved.'); this.saving.set(false); },
		});
	}

	updateStatus(item: ClaimDetail): void {
		if (this.statusForm.invalid || this.saving()) return;
		const value = this.statusForm.getRawValue();
		this.saving.set(true);
		this.api.updateStatus(item.filingID, { status: value.status, rowVersion: item.rowVersion, reason: value.reason || null }).pipe(
			switchMap(() => this.api.getClaim(item.filingID)),
			takeUntilDestroyed(this.destroyRef),
		).subscribe({
			next: claim => { this.claim.set(claim); this.statusForm.patchValue({ status: claim.status, reason: '' }); this.message.set('Claim status updated.'); this.saving.set(false); },
			error: failure => { this.error.set(failure.status === 409 ? 'This claim changed elsewhere. Reload it before changing the status.' : 'The status transition could not be saved.'); this.saving.set(false); },
		});
	}

	deleteClaim(item: ClaimDetail): void {
		if (!window.confirm(`Delete claim #${item.filingID}? This removes it from active claim lists.`)) return;
		this.api.deleteClaim(item.filingID, item.rowVersion).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
			next: () => void this.router.navigateByUrl('/claims'),
			error: failure => this.error.set(failure.status === 409 ? 'This claim changed elsewhere. Reload it before deleting.' : 'Claim could not be deleted.'),
		});
	}

	printRecord(): void { window.print(); }
}
