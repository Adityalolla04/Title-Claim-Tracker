// File: TitleClaimTracker/ClientApp/src/app/features/chatbot/chatbot.component.ts
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ClaimApiService } from '../../core/services/claim-api.service';
import { ChatbotResponse, ClaimSummary } from '../../core/models/claim.model';

interface ChatMessage { role: 'assistant' | 'user'; text: string; claim?: ClaimSummary | null; }

@Component({ selector: 'app-chatbot', standalone: true, imports: [CommonModule, ReactiveFormsModule], templateUrl: './chatbot.component.html', styleUrl: './chatbot.component.scss', changeDetection: ChangeDetectionStrategy.OnPush })
export class ChatbotComponent {
  private readonly api = inject(ClaimApiService);
  private readonly destroyRef = inject(DestroyRef);
  readonly messageControl = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(10)] });
  readonly messages = signal<ChatMessage[]>([{ role: 'assistant', text: 'Describe a title issue and include property: address, city: city, and state: ST when available.' }]);
  readonly claims = signal<ClaimSummary[]>([]);
  readonly loading = signal(false);
  readonly apiError = signal<string | null>(null);
  readonly suggestions = ['Review a possible lien', 'Classify a deed dispute', 'Check an ownership claim'];

  constructor() { this.refresh(); }

  send(text: string = this.messageControl.value): void {
    const message = text.trim();
    if (message.length < 10 || this.loading()) return;
    this.messages.update(items => [...items, { role: 'user', text: message }]);
    this.messageControl.reset();
    this.loading.set(true);
    this.api.triage({ message }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: response => { this.apiError.set(null); this.receive(response); }, error: () => { this.apiError.set('The API is unavailable. Start the ASP.NET Core project and verify the configured proxy port.'); this.receive({ message: 'I cannot reach the title claims API right now. Please start the backend and try again.', requiresConfirmation: false, extractedClaim: null, savedFiling: null, triageAttemptID: null, outcome: 'failed', extractionMode: 'unavailable' }); }, complete: () => this.loading.set(false) });
  }

  refresh(): void {
    this.api.listClaims().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: result => { this.apiError.set(null); this.claims.set(result.items); }, error: () => this.apiError.set('Unable to load claims. Confirm that the backend is running on https://localhost:58011.') });
  }

  riskClass(risk: number | null): string { return risk === null ? 'risk-neutral' : risk >= .75 ? 'risk-high' : risk >= .5 ? 'risk-medium' : 'risk-low'; }
  private receive(response: ChatbotResponse): void { this.messages.update(items => [...items, { role: 'assistant', text: response.message, claim: response.savedFiling }]); if (response.savedFiling) this.claims.update(items => [response.savedFiling as ClaimSummary, ...items]); }
}
