[CmdletBinding()]
param(
    [int]$Seed = 42,
    [ValidateRange(4, 5000)]
    [int]$Count = 24,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $OutputDirectory = Join-Path $repositoryRoot 'outputs\synthetic-portfolio'
}
$sampleRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $sampleRoot | Out-Null
$random = [System.Random]::new($Seed)
$categories = @('Title Claim', 'Lien', 'Easement', 'Deed Dispute')
$geographies = @('Fictional North Borough', 'Fictional Harbor Borough', 'Fictional West Borough', 'Fictional Garden Borough')
$statuses = @('New', 'Under Review', 'Escalated', 'Resolved', 'Closed')
$transitions = @{
    New = @('Under Review', 'Escalated', 'Closed')
    'Under Review' = @('Escalated', 'Resolved', 'Closed')
    Escalated = @('Under Review', 'Resolved', 'Closed')
    Resolved = @('Closed')
}
$baseDate = [DateTime]::SpecifyKind([DateTime]::Parse('2026-01-05T09:00:00'), [DateTimeKind]::Utc)
$claims = [System.Collections.Generic.List[object]]::new()
$history = [System.Collections.Generic.List[object]]::new()
$attempts = [System.Collections.Generic.List[object]]::new()

for ($index = 1; $index -le $Count; $index++) {
    $claimId = 'SYN-' + $index.ToString('0000')
    $category = $categories[$random.Next($categories.Count)]
    $created = $baseDate.AddDays($index).AddMinutes($random.Next(0, 600))
    $source = if ($random.Next(0, 2) -eq 0) { 'Manual' } else { 'Assisted' }
    $current = 'New'
    $events = [System.Collections.Generic.List[object]]::new()
    $events.Add([pscustomobject]@{ DemoClaimId = $claimId; PreviousStatus = $null; NewStatus = 'New'; EventUtc = $created.ToString('o'); EventKind = 'Application event'; IsSynthetic = $true })
    $steps = $random.Next(0, 3)
    for ($step = 0; $step -lt $steps; $step++) {
        $choices = $transitions[$current]
        if ($null -eq $choices) { break }
        $next = $choices[$random.Next($choices.Count)]
        $eventTime = $created.AddHours(($step + 1) * $random.Next(4, 30))
        $events.Add([pscustomobject]@{ DemoClaimId = $claimId; PreviousStatus = $current; NewStatus = $next; EventUtc = $eventTime.ToString('o'); EventKind = 'Application event'; IsSynthetic = $true })
        $current = $next
        if ($current -eq 'Closed') { break }
    }
    foreach ($event in $events) { $history.Add($event) }
    $resolved = $events | Where-Object { $_.NewStatus -in @('Resolved', 'Closed') } | Select-Object -First 1
    $score = if ($source -eq 'Assisted') { [Math]::Round(0.72 + ($random.NextDouble() * 0.26), 4) } else { $null }
    $claims.Add([pscustomobject]@{
        DemoClaimId = $claimId; PrimaryCategory = $category; CurrentStatus = $current; CreationSource = $source; Geography = $geographies[$random.Next($geographies.Count)]
        CreatedUtc = $created.ToString('o'); ResolvedUtc = if ($resolved) { $resolved.EventUtc } else { $null }; Priority = $random.Next(1, 6); ModelScore = $score
        ModelVersion = if ($score) { 'synthetic-fixture-v1' } else { $null }; IsSynthetic = $true; SyntheticSeed = $Seed
    })
    $attemptOutcome = if ($source -eq 'Assisted') { @('Created', 'NeedsInformation', 'NeedsReview')[$random.Next(3)] } else { 'ManualSubmission' }
    $attemptCreated = if ($attemptOutcome -eq 'Created') { $claimId } else { $null }
    $attempts.Add([pscustomobject]@{
        DemoAttemptId = ('SYN-ATT-' + $index.ToString('0000')); Outcome = $attemptOutcome; StartedUtc = $created.AddMinutes(-5).ToString('o'); CompletedUtc = $created.ToString('o')
        PredictedCategory = if ($source -eq 'Assisted') { $category } else { $null }; Score = $score; ModelVersion = if ($score) { 'synthetic-fixture-v1' } else { $null }
        CreatedDemoClaimId = $attemptCreated; ExtractionMode = if ($source -eq 'Assisted') { 'deterministic' } else { 'manual' }; IsSynthetic = $true; SyntheticSeed = $Seed
    })
}

$claims | Export-Csv (Join-Path $sampleRoot 'claims.csv') -NoTypeInformation -Encoding UTF8
$history | Export-Csv (Join-Path $sampleRoot 'status_history.csv') -NoTypeInformation -Encoding UTF8
$attempts | Export-Csv (Join-Path $sampleRoot 'triage_attempts.csv') -NoTypeInformation -Encoding UTF8
$manifest = [pscustomobject]@{ GeneratedUtc = [DateTime]::UtcNow.ToString('o'); Seed = $Seed; ClaimCount = $claims.Count; HistoryCount = $history.Count; AttemptCount = $attempts.Count; SourceDesignation = 'Synthetic fictional portfolio data'; ImportedToSql = $false }
$manifest | ConvertTo-Json | Set-Content (Join-Path $sampleRoot 'manifest.json') -Encoding UTF8

$chronologyErrors = @($claims | Where-Object { $_.ResolvedUtc -and ([DateTime]$_.ResolvedUtc -lt [DateTime]$_.CreatedUtc) }).Count
$orphanAttempts = @($attempts | Where-Object { $_.CreatedDemoClaimId -and $_.CreatedDemoClaimId -notin $claims.DemoClaimId }).Count
$duplicateClaims = @($claims | Group-Object DemoClaimId | Where-Object Count -gt 1).Count
if ($chronologyErrors -or $orphanAttempts -or $duplicateClaims) { throw "Synthetic validation failed: chronology=$chronologyErrors orphanAttempts=$orphanAttempts duplicateClaims=$duplicateClaims" }
Write-Host "Generated $($claims.Count) synthetic claims, $($history.Count) status events, and $($attempts.Count) triage attempts in $sampleRoot"
