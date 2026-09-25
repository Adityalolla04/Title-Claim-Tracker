// File: TitleClaimTracker/ClientApp/src/app/core/models/claim.model.ts
export type ClaimStatus = 'New' | 'Under Review' | 'Escalated' | 'Resolved' | 'Closed' | 'Pending Review' | 'Active Investigation';
export type FilingType = 'Title Claim' | 'Lien' | 'Easement' | 'Deed Dispute';

export interface ClaimSummary { filingID: number; propertyID: number; address: string; city: string; state: string; filingType: string; dateFiled: string; status: ClaimStatus; claimantName: string | null; notes: string | null; riskScore: number | null; }
export interface CreateClaim { address: string; city: string; state: string; legalDescription: string | null; filingType: FilingType; dateFiled: string; claimantName: string | null; notes: string | null; riskScore: number | null; }
export interface ClaimStatusHistory { statusHistoryID: number; fromStatus: ClaimStatus | null; toStatus: ClaimStatus; transitionedUtc: string; reason: string | null; }
export interface ClaimDetail extends ClaimSummary { legalDescription: string | null; creationSource: 'Manual' | 'Assisted'; classificationScore: number | null; triagePriority: number; rowVersion: string; statusTimeline: ClaimStatusHistory[]; }
export interface UpdateClaim extends Omit<CreateClaim, 'riskScore'> { rowVersion: string; }
export interface UpdateClaimStatus { status: ClaimStatus; rowVersion: string; reason: string | null; }
export interface SubmitTriageDraft extends Omit<CreateClaim, 'dateFiled' | 'riskScore'> { triageAttemptID: number; }
export interface TriageAttempt { triageAttemptID: number; outcome: string; uiOutcome: string; predictedFilingType: string | null; classificationScore: number | null; heuristicPriority: number | null; extractedAddress: string | null; extractedCity: string | null; extractedState: string | null; extractedClaimantName: string | null; extractedNotes: string | null; createdFilingID: number | null; createdUtc: string; }
export interface TriageFeedback { triageAttemptID: number; wasCorrect: boolean; correctedFilingTypeID: number | null; correctedIssueTags: string | null; correctionNotes: string | null; }
export interface ChatTriageRequest { message: string; }
export interface ClaimExtraction { claimantName: string | null; filingType: string; riskScore: number; legalNotes: string; confidenceScore: number; }
export interface ChatbotResponse { message: string; requiresConfirmation: boolean; extractedClaim: ClaimExtraction | null; savedFiling: ClaimSummary | null; triageAttemptID: number | null; outcome: 'created' | 'needsInformation' | 'needsReview' | 'outOfScope' | 'failed'; extractionMode: string; }
export interface PagedClaims { items: ClaimSummary[]; total: number; page: number; pageSize: number; }
export interface RiskReport { propertyID: number; address: string; city: string; state: string; averageRiskScore: number; openClaimsCount: number; filingCount: number; filingTypeCount: number; }
