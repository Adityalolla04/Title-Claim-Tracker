[CmdletBinding()]
param(
    [int]$Seed = 42,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $repositoryRoot = Split-Path -Parent (Split-Path -Parent $root)
    $OutputDirectory = Join-Path $repositoryRoot 'outputs\analytics-prepared'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$prepared = Join-Path $OutputDirectory 'data'
$validation = $OutputDirectory
New-Item -ItemType Directory -Force -Path $prepared, $validation | Out-Null
Get-ChildItem $prepared -Filter '*.csv' -ErrorAction SilentlyContinue | Remove-Item -Force
$snapshot = [DateTime]::UtcNow.ToString('o')
$evaluationFile = Join-Path (Split-Path -Parent $root) 'ClaimTracker\ML\Training\evaluation.json'
if (-not (Test-Path $evaluationFile)) { throw 'ML evaluation metadata is missing. Run dotnet run -- --train-model before preparing analytics.' }
$evaluation = Get-Content $evaluationFile -Raw | ConvertFrom-Json
$modelVersion = $evaluation.modelVersion

$claims = @(
    [pscustomobject]@{ DemoClaimId='DEMO-1001'; PrimaryCategory='Title Claim'; CurrentStatus='Under Review'; CreationSource='Assisted'; Geography='Synthetic Midwest'; CreatedUtc='2026-01-05T14:00:00Z'; ResolvedUtc=$null; Priority=2; ModelScore=0.93; ModelVersion=$modelVersion; IsSynthetic=$true; SnapshotUtc=$snapshot },
    [pscustomobject]@{ DemoClaimId='DEMO-1002'; PrimaryCategory='Lien'; CurrentStatus='Resolved'; CreationSource='Manual'; Geography='Synthetic Northeast'; CreatedUtc='2026-01-08T16:30:00Z'; ResolvedUtc='2026-01-19T12:00:00Z'; Priority=3; ModelScore=$null; ModelVersion=$null; IsSynthetic=$true; SnapshotUtc=$snapshot },
    [pscustomobject]@{ DemoClaimId='DEMO-1003'; PrimaryCategory='Easement'; CurrentStatus='New'; CreationSource='Assisted'; Geography='Synthetic West'; CreatedUtc='2026-02-02T10:00:00Z'; ResolvedUtc=$null; Priority=4; ModelScore=0.88; ModelVersion=$modelVersion; IsSynthetic=$true; SnapshotUtc=$snapshot },
    [pscustomobject]@{ DemoClaimId='DEMO-1004'; PrimaryCategory='Deed Dispute'; CurrentStatus='Closed'; CreationSource='Manual'; Geography='Synthetic South'; CreatedUtc='2026-02-11T09:15:00Z'; ResolvedUtc='2026-03-01T18:00:00Z'; Priority=2; ModelScore=$null; ModelVersion=$null; IsSynthetic=$true; SnapshotUtc=$snapshot }
)
$status = @(
    [pscustomobject]@{ DemoClaimId='DEMO-1001'; PreviousStatus=$null; NewStatus='Under Review'; EventUtc='2026-01-05T14:00:00Z'; EventKind='Imported legacy baseline'; IsSynthetic=$true },
    [pscustomobject]@{ DemoClaimId='DEMO-1001'; PreviousStatus='New'; NewStatus='Under Review'; EventUtc='2026-01-06T09:00:00Z'; EventKind='Application event'; IsSynthetic=$true },
    [pscustomobject]@{ DemoClaimId='DEMO-1002'; PreviousStatus=$null; NewStatus='New'; EventUtc='2026-01-08T16:30:00Z'; EventKind='Application event'; IsSynthetic=$true },
    [pscustomobject]@{ DemoClaimId='DEMO-1002'; PreviousStatus='New'; NewStatus='Resolved'; EventUtc='2026-01-19T12:00:00Z'; EventKind='Application event'; IsSynthetic=$true },
    [pscustomobject]@{ DemoClaimId='DEMO-1003'; PreviousStatus=$null; NewStatus='New'; EventUtc='2026-02-02T10:00:00Z'; EventKind='Application event'; IsSynthetic=$true },
    [pscustomobject]@{ DemoClaimId='DEMO-1004'; PreviousStatus=$null; NewStatus='New'; EventUtc='2026-02-11T09:15:00Z'; EventKind='Application event'; IsSynthetic=$true },
    [pscustomobject]@{ DemoClaimId='DEMO-1004'; PreviousStatus='New'; NewStatus='Closed'; EventUtc='2026-03-01T18:00:00Z'; EventKind='Application event'; IsSynthetic=$true }
)
$attempts = @(
    [pscustomobject]@{ DemoAttemptId='ATT-2001'; Outcome='Created'; StartedUtc='2026-01-05T13:55:00Z'; CompletedUtc='2026-01-05T14:00:00Z'; PredictedCategory='Title Claim'; Score=0.93; ModelVersion=$modelVersion; CreatedDemoClaimId='DEMO-1001'; ExtractionMode='deterministic'; IsSynthetic=$true },
    [pscustomobject]@{ DemoAttemptId='ATT-2002'; Outcome='NeedsInformation'; StartedUtc='2026-02-02T09:55:00Z'; CompletedUtc='2026-02-02T10:00:00Z'; PredictedCategory='Easement'; Score=0.88; ModelVersion=$modelVersion; CreatedDemoClaimId='DEMO-1003'; ExtractionMode='deterministic'; IsSynthetic=$true },
    [pscustomobject]@{ DemoAttemptId='ATT-2003'; Outcome='NeedsReview'; StartedUtc='2026-02-12T11:00:00Z'; CompletedUtc='2026-02-12T11:00:02Z'; PredictedCategory='Deed Dispute'; Score=0.61; ModelVersion=$modelVersion; CreatedDemoClaimId=$null; ExtractionMode='manual'; IsSynthetic=$true }
)
$documents = @(
    [pscustomobject]@{ Geography='Synthetic Northeast'; RecordDate='2026-01-31'; SourceDocumentType='Deed'; DocumentCount=18; SourceDesignation='Synthetic public-record demonstration' },
    [pscustomobject]@{ Geography='Synthetic Northeast'; RecordDate='2026-01-31'; SourceDocumentType='Mortgage'; DocumentCount=11; SourceDesignation='Synthetic public-record demonstration' },
    [pscustomobject]@{ Geography='Synthetic West'; RecordDate='2026-02-28'; SourceDocumentType='Easement'; DocumentCount=9; SourceDesignation='Synthetic public-record demonstration' }
)
$classNames = @('Deed Dispute', 'Easement', 'Lien', 'Title Claim')
$metrics = @([pscustomobject]@{ ModelVersion=$modelVersion; DatasetVersion=$evaluation.datasetVersion; ClassName='All'; MetricName='Accuracy'; MetricValue=$evaluation.accuracy; SampleCount=$evaluation.evaluationSamples; Provenance=$evaluation.datasetProvenance })
for ($index = 0; $index -lt $evaluation.perClass.Count; $index++) {
    $classMetric = $evaluation.perClass[$index]
    $metrics += [pscustomobject]@{ ModelVersion=$modelVersion; DatasetVersion=$evaluation.datasetVersion; ClassName=$classNames[$index]; MetricName='Precision'; MetricValue=$classMetric.precision; SampleCount=$evaluation.evaluationSamples; Provenance=$evaluation.datasetProvenance }
    $metrics += [pscustomobject]@{ ModelVersion=$modelVersion; DatasetVersion=$evaluation.datasetVersion; ClassName=$classNames[$index]; MetricName='Recall'; MetricValue=$classMetric.recall; SampleCount=$evaluation.evaluationSamples; Provenance=$evaluation.datasetProvenance }
    $metrics += [pscustomobject]@{ ModelVersion=$modelVersion; DatasetVersion=$evaluation.datasetVersion; ClassName=$classNames[$index]; MetricName='F1'; MetricValue=$classMetric.f1; SampleCount=$evaluation.evaluationSamples; Provenance=$evaluation.datasetProvenance }
}
$matrix = @()
for ($actual = 0; $actual -lt $evaluation.confusionMatrix.Count; $actual++) { for ($predicted = 0; $predicted -lt $evaluation.confusionMatrix[$actual].Count; $predicted++) { $matrix += [pscustomobject]@{ ModelVersion=$modelVersion; DatasetVersion=$evaluation.datasetVersion; ActualClass=$classNames[$actual]; PredictedClass=$classNames[$predicted]; Count=$evaluation.confusionMatrix[$actual][$predicted]; Provenance=$evaluation.datasetProvenance } } }
$claims | Export-Csv (Join-Path $prepared 'claims.csv') -NoTypeInformation
$status | Export-Csv (Join-Path $prepared 'status_history.csv') -NoTypeInformation
$attempts | Export-Csv (Join-Path $prepared 'triage_attempts.csv') -NoTypeInformation
$metrics | Export-Csv (Join-Path $prepared 'model_metrics.csv') -NoTypeInformation
$matrix | Export-Csv (Join-Path $prepared 'confusion_matrix.csv') -NoTypeInformation
$documents | Export-Csv (Join-Path $prepared 'public_document_summary.csv') -NoTypeInformation

$duplicateClaims = @($claims | Group-Object DemoClaimId | Where-Object Count -gt 1).Count
$orphanEvents = @($status | Where-Object { $_.DemoClaimId -notin $claims.DemoClaimId }).Count
$invalidScores = @($claims | Where-Object { $null -ne $_.ModelScore -and ($_.ModelScore -lt 0 -or $_.ModelScore -gt 1) }).Count
$chronologyErrors = @($claims | Where-Object { $null -ne $_.ResolvedUtc -and [DateTime]$_.ResolvedUtc -lt [DateTime]$_.CreatedUtc }).Count
$checks = @($duplicateClaims, $orphanEvents, $invalidScores, $chronologyErrors)
$passed = ($checks | Where-Object { $_ -gt 0 }).Count -eq 0
$quality = [pscustomobject]@{ SnapshotUtc=$snapshot; Mode='Public synthetic demo'; Status=if ($passed) { 'Passed' } else { 'Failed' }; InputRows=($claims.Count + $status.Count + $attempts.Count + $metrics.Count + $matrix.Count + $documents.Count); OutputRows=($claims.Count + $status.Count + $attempts.Count + $metrics.Count + $matrix.Count + $documents.Count); RejectedRows=0; DuplicateRows=$duplicateClaims; OrphanRows=$orphanEvents; InvalidScoreRows=$invalidScores; ChronologyErrors=$chronologyErrors; IsSynthetic=$true }
$quality | ConvertTo-Json | Set-Content (Join-Path $validation 'quality-report.json')
[pscustomobject]@{ SnapshotUtc=$snapshot; DatasetName='all prepared outputs'; InputRows=$quality.InputRows; OutputRows=$quality.OutputRows; RejectedRows=$quality.RejectedRows; DuplicateRows=$duplicateClaims; NullRows=0; ValidationStatus=$quality.Status } | Export-Csv (Join-Path $prepared 'data_quality_summary.csv') -NoTypeInformation
if (-not $passed) { throw 'Critical analytics validation failed. See outputs/quality-report.json.' }
Write-Host "Prepared synthetic analytics data in $prepared"
Write-Host "Validation: $($quality.Status)"
