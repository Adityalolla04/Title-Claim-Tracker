import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ClaimApiService } from '../../core/services/claim-api.service';
import { AuthService } from '../../core/services/auth.service';
import { ClaimSummary, FilingType } from '../../core/models/claim.model';

const filingTypes: FilingType[] = ['Title Claim', 'Lien', 'Easement', 'Deed Dispute'];
interface PendingDocument { file: File; uploaded: boolean; }

@Component({ selector: 'app-chatbot', standalone: true, imports: [CommonModule, ReactiveFormsModule], templateUrl: './chatbot.component.html', styleUrl: './chatbot.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class ChatbotComponent {
  private readonly api = inject(ClaimApiService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly form = this.fb.nonNullable.group({
    claimantName: [this.auth.account()?.displayName ?? '', [Validators.required, Validators.maxLength(255)]],
    address: ['', [Validators.required, Validators.maxLength(255)]],
    city: ['', [Validators.required, Validators.maxLength(100)]],
    state: ['', [Validators.required, Validators.pattern(/^[a-zA-Z]{2}$/)]],
    filingType: ['', Validators.required],
    issueDescription: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(4000)]],
    legalDescription: ['', Validators.maxLength(2000)],
  });
  readonly confirmation = this.fb.control(false, { validators: Validators.requiredTrue });
  readonly analyzing = signal(false);
  readonly submitting = signal(false);
  readonly reviewing = signal(false);
  readonly error = signal<string | null>(null);
  readonly aiMessage = signal<string | null>(null);
  readonly suggestion = signal<string | null>(null);
  readonly draftAttemptId = signal<number | null>(null);
  readonly missingFields = signal<string[]>([]);
  readonly attachments = signal<PendingDocument[]>([]);
  readonly createdClaim = signal<ClaimSummary | null>(null);
  readonly attachmentError = signal<string | null>(null);
  readonly filingTypes = filingTypes;

  analyze(): void {
    const description = this.form.controls.issueDescription;
    if (description.invalid || this.analyzing()) {
      description.markAsTouched();
      return;
    }
    this.error.set(null);
    this.analyzing.set(true);
    this.reviewing.set(false);
    this.confirmation.setValue(false);
    const values = this.form.getRawValue();
    const message = [
      values.claimantName.trim() ? `Claimant name: ${values.claimantName.trim()}` : '',
      values.address.trim() ? `Property address: ${values.address.trim()}` : '',
      values.city.trim() ? `City: ${values.city.trim()}` : '',
      values.state.trim() ? `State: ${values.state.trim()}` : '',
      values.legalDescription.trim() ? `Legal description: ${values.legalDescription.trim()}` : '',
      values.issueDescription.trim(),
    ].filter(Boolean).join('; ');

    this.api.triage({ message }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: response => {
        const extracted = response.extractedClaim;
        if (extracted) {
          const updates: Record<string, string> = {};
          if (!values.claimantName.trim() && extracted.claimantName) updates['claimantName'] = extracted.claimantName;
          if (!values.address.trim() && extracted.address) updates['address'] = extracted.address;
          if (!values.city.trim() && extracted.city) updates['city'] = extracted.city;
          if (!values.state.trim() && extracted.state) updates['state'] = extracted.state.toUpperCase();
          if (!values.filingType && filingTypes.includes(extracted.filingType as FilingType)) updates['filingType'] = extracted.filingType;
          if (!values.issueDescription.trim() && extracted.legalNotes) updates['issueDescription'] = extracted.legalNotes;
          this.form.patchValue(updates);
          this.suggestion.set(extracted.filingType);
        }
        this.aiMessage.set(response.message);
        this.draftAttemptId.set(response.triageAttemptID);
        if (response.outcome === 'outOfScope') {
          this.error.set(response.message);
          this.analyzing.set(false);
          return;
        }
        const missing = this.requiredMissing();
        this.missingFields.set(missing);
        if (response.requiresConfirmation && response.triageAttemptID && missing.length === 0) {
          this.reviewing.set(true);
        } else if (!missing.length && !response.requiresConfirmation) {
          this.error.set(response.message);
        }
        this.analyzing.set(false);
      },
      error: failure => {
        this.error.set(this.describeApiFailure(failure, 'Triage'));
        this.analyzing.set(false);
      },
    });
  }

  editDetails(): void {
    this.reviewing.set(false);
    this.confirmation.setValue(false);
    this.missingFields.set(this.requiredMissing());
  }

  printDraft(): void { window.print(); }

  selectAttachments(event: Event): void {
    const input = event.target as HTMLInputElement;
    const incoming = Array.from(input.files ?? []);
    const existing = this.attachments();
    const valid: PendingDocument[] = [];
    let issue: string | null = null;
    for (const file of incoming) {
      if (!file.name.toLowerCase().endsWith('.pdf')) { issue = `${file.name}: only PDF files are accepted.`; continue; }
      if (file.size <= 0 || file.size > 10 * 1024 * 1024) { issue = `${file.name}: each PDF must be 10 MB or smaller.`; continue; }
      if (existing.length + valid.length >= 5) { issue = 'A claim can have at most five supporting PDFs.'; break; }
      valid.push({ file, uploaded: false });
    }
    this.attachments.set([...existing, ...valid]);
    this.attachmentError.set(issue);
    input.value = '';
  }

  removeAttachment(index: number): void {
    if (this.createdClaim()) return;
    this.attachments.update(items => items.filter((_, itemIndex) => itemIndex !== index));
  }

  hasPendingDocuments(): boolean { return this.attachments().some(item => !item.uploaded); }

  openClaim(id: number): void { void this.router.navigate(['/claims', id]); }

  submit(): void {
    if (this.form.invalid || this.confirmation.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      this.confirmation.markAsTouched();
      return;
    }
    const triageAttemptID = this.draftAttemptId();
    if (!triageAttemptID) {
      this.error.set('Analyze the intake before submitting your claim.');
      return;
    }
    const value = this.form.getRawValue();
    this.submitting.set(true);
    this.error.set(null);
    this.api.submitDraft({
      triageAttemptID,
      address: value.address.trim(),
      city: value.city.trim(),
      state: value.state.trim().toUpperCase(),
      legalDescription: value.legalDescription.trim() || null,
      filingType: value.filingType as FilingType,
      claimantName: value.claimantName.trim(),
      notes: value.issueDescription.trim(),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: claim => {
        this.createdClaim.set(claim);
        this.submitting.set(false);
        this.uploadPendingDocuments();
      },
      error: failure => {
        this.error.set(this.describeApiFailure(failure, 'Claim submission'));
        this.submitting.set(false);
      },
    });
  }

  uploadPendingDocuments(): void {
    const claim = this.createdClaim();
    if (!claim) return;
    const nextIndex = this.attachments().findIndex(item => !item.uploaded);
    if (nextIndex < 0) {
      void this.router.navigate(['/claims', claim.filingID]);
      return;
    }
    this.submitting.set(true);
    this.attachmentError.set(null);
    const pending = this.attachments()[nextIndex];
    this.api.uploadClaimDocument(claim.filingID, pending.file).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.attachments.update(items => items.map((item, index) => index === nextIndex ? { ...item, uploaded: true } : item));
        this.uploadPendingDocuments();
      },
      error: failure => {
        this.attachmentError.set(failure.status === 0 ? 'The claim was submitted, but its PDF upload could not connect. Retry the upload before leaving this page.' : `The claim was submitted, but ${pending.file.name} could not be uploaded. Retry or open the claim record.`);
        this.submitting.set(false);
      },
    });
  }

  private requiredMissing(): string[] {
    const fields: [string, keyof typeof this.form.controls][] = [
      ['Claimant name', 'claimantName'], ['Property address', 'address'], ['City', 'city'],
      ['Two-letter state', 'state'], ['Claim category', 'filingType'], ['Issue description', 'issueDescription'],
    ];
    return fields.filter(([, key]) => this.form.controls[key].invalid).map(([label]) => label);
  }

  private describeApiFailure(error: unknown, action: string): string {
    if (!(error instanceof HttpErrorResponse)) return `${action} failed unexpectedly. Try again.`;
    if (error.status === 0) return `${action} could not connect to the API. Check that the backend and proxy are running.`;
    if (error.status === 400) return `${action} was rejected by request or antiforgery validation (HTTP 400). Refresh the page and retry.`;
    if (error.status === 401) return 'Your session has expired. Sign in again.';
    if (error.status === 403) return `You do not have permission to complete ${action.toLowerCase()}.`;
    if (error.status >= 500) return `${action} reached the API, but the server failed (HTTP ${error.status}).`;
    return `${action} failed with HTTP ${error.status}.`;
  }
}
