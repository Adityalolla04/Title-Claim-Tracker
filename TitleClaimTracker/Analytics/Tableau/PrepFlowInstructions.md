# Tableau / Prep Handoff

The PowerShell script is a legacy synthetic fixture generator. The SQL-backed PySpark runner is the opt-in internal extraction path and is not approved for Tableau Public. No `.tfl` or `.tflx` is included because none was authored or validated here.

## Generate the Tableau inputs

From the repository root, generate the synthetic files outside the repository:

```powershell
$tableauOutput = Join-Path $env:TEMP 'TitleClaimTracker-Tableau-Demo'
& .\TitleClaimTracker\Analytics\Scripts\Prepare-Analytics.ps1 -OutputDirectory $tableauOutput
```

Use the CSVs in `$tableauOutput/data/`. The quality report is `$tableauOutput/quality-report.json`; confirm its status is `Passed` and `IsSynthetic` is `true` before building the workbook. The generator does not read or write SQL Server. It replaces CSVs in the chosen output folder when rerun.

The data subjects have different grains: `claims.csv` is one row per claim, `triage_attempts.csv` is one row per attempt, and `status_history.csv` is one row per status event. Keep them as separate Tableau data sources or use logical relationships; do not physically join raw status events to claims and then count claim rows.

## Optional Tableau Prep flow

1. In Tableau Desktop, use **Connect > To a File > Text file** and open `claims.csv` from `$tableauOutput/data/`. Add other CSVs as separate logical tables or separate data sources only for views that need them.
2. Set IDs and categories to string, scores to decimal, counts to integer, and UTC timestamps to datetime.
3. Trim supported text values and preserve source labels.
4. Validate `IsSynthetic=true`, score range 0..1, non-negative counts, and parent references.
5. Deduplicate claims by `DemoClaimId`, attempts by `DemoAttemptId`, and status events by `(DemoClaimId, EventUtc, NewStatus)`.
6. Aggregate status events to one row per claim before any claim-level join.
7. Keep `public_document_summary.csv` as a separate subject; never join it to claim facts to imply dispute rates.
8. Output separate clean CSVs or a Hyper extract. Retain rejected rows in a quarantine output if a future internal source is added.
9. Reconcile row counts and dashboard totals to `data_quality_summary.csv`.

## Workbook handoff

Recommended dashboard tabs are Claim Operations, Intake Outcomes, Model Evaluation, and Public Document Volume. Every dashboard should show synthetic source designation, `SnapshotUtc`, model version where relevant, and the limitations in `MetricsDefinitions.md`.

Tableau Public publication remains a manual account action. Do not invent a workbook URL or imply that local CSV generation refreshes a published workbook automatically.
