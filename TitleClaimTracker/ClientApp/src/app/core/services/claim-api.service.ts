// File: TitleClaimTracker/ClientApp/src/app/core/services/claim-api.service.ts
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuditLogEntry, ChatbotResponse, ChatTriageRequest, ClaimAnalytics, ClaimDetail, ClaimDocument, ClaimDocumentAnalysis, ClaimStatus, ClaimSummary, CreateClaim, PagedClaims, RiskReport, SubmitTriageDraft, TriageAttempt, TriageFeedback, UpdateClaim, UpdateClaimStatus } from '../models/claim.model';

@Injectable({ providedIn: 'root' })
export class ClaimApiService {
  private readonly http = inject(HttpClient);
  private readonly api = '/api';

  listClaims(status?: ClaimStatus, filingType?: string, city?: string, page = 1, pageSize = 25, search?: string): Observable<PagedClaims> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (status) params = params.set('status', status);
    if (filingType) params = params.set('filingType', filingType);
    if (city) params = params.set('city', city);
    if (search) params = params.set('search', search);
    return this.http.get<PagedClaims>(`${this.api}/filings`, { params });
  }

  getClaimAnalytics(): Observable<ClaimAnalytics> { return this.http.get<ClaimAnalytics>(`${this.api}/filings/analytics`); }

  getClaim(id: number): Observable<ClaimDetail> { return this.http.get<ClaimDetail>(`${this.api}/filings/${id}`); }

  getClaimDocuments(id: number): Observable<ClaimDocument[]> { return this.http.get<ClaimDocument[]>(`${this.api}/filings/${id}/documents`); }

  uploadClaimDocument(id: number, file: File): Observable<ClaimDocument> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ClaimDocument>(`${this.api}/filings/${id}/documents`, form);
  }

  analyzeClaimDocument(id: number, documentId: number): Observable<ClaimDocumentAnalysis> {
    return this.http.post<ClaimDocumentAnalysis>(`${this.api}/filings/${id}/documents/${documentId}/analysis`, {});
  }

  getAuditHistory(id: number): Observable<AuditLogEntry[]> { return this.http.get<AuditLogEntry[]>(`${this.api}/filings/${id}/audit`); }

  createClaim(request: CreateClaim): Observable<ClaimSummary> {
    return this.http.post<ClaimSummary>(`${this.api}/filings`, request, { headers: this.idempotencyHeaders() });
  }

  triage(request: ChatTriageRequest): Observable<ChatbotResponse> {
    return this.http.post<ChatbotResponse>(`${this.api}/chatbot/triage`, request);
  }

  updateClaim(id: number, request: UpdateClaim): Observable<ClaimDetail> {
    return this.http.put<ClaimDetail>(`${this.api}/filings/${id}`, request);
  }

  updateStatus(id: number, request: UpdateClaimStatus): Observable<ClaimSummary> {
    return this.http.patch<ClaimSummary>(`${this.api}/filings/${id}/status`, request);
  }

  deleteClaim(id: number, rowVersion: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/filings/${id}`, { headers: new HttpHeaders({ 'If-Match': rowVersion }) });
  }

  getRiskReport(propertyId?: number): Observable<RiskReport[]> {
    let params = new HttpParams();
    if (propertyId !== undefined) params = params.set('propertyId', propertyId);
    return this.http.get<RiskReport[]>(`${this.api}/filings/report`, { params });
  }

  submitDraft(request: SubmitTriageDraft): Observable<ClaimSummary> {
    return this.http.post<ClaimSummary>(`${this.api}/chatbot/drafts/submit`, request, { headers: this.idempotencyHeaders() });
  }

  getReviewQueue(): Observable<TriageAttempt[]> { return this.http.get<TriageAttempt[]>(`${this.api}/chatbot/review`); }

  submitFeedback(attemptId: number, feedback: TriageFeedback): Observable<void> {
    return this.http.post<void>(`${this.api}/chatbot/review/${attemptId}/feedback`, feedback);
  }

  exportClaims(status?: ClaimStatus, filingType?: string, city?: string, search?: string): Observable<Blob> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (filingType) params = params.set('filingType', filingType);
    if (city) params = params.set('city', city);
    if (search) params = params.set('search', search);
    return this.http.get(`${this.api}/filings/export`, { params, responseType: 'blob' });
  }

  private idempotencyHeaders(): HttpHeaders { return new HttpHeaders({ 'Idempotency-Key': crypto.randomUUID() }); }
}
