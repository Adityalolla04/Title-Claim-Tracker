# Tableau / Prep Handoff

The PowerShell script is a legacy synthetic fixture generator. The SQL-backed PySpark runner is the opt-in internal extraction path and is not approved for Tableau Public. No `.tfl` or `.tflx` is included because none was authored or validated here.

## Inputs and outputs

- Input: the synthetic records defined in `Analytics/Scripts/Prepare-Analytics.ps1`.
- Output: the seven CSVs in `Analytics/Data/Prepared/` plus `Analytics/outputs/quality-report.json`.
- Grain: claims and triage attempts are one-to-one datasets; status history is one-to-many and must be aggregated before relating it to claims.

## Optional Tableau Prep flow

1. Add each prepared CSV as a separate input.
2. Set IDs and categories to string, scores to decimal, counts to integer, and UTC timestamps to datetime.
3. Trim supported text values and preserve source labels.
4. Validate `IsSynthetic=true`, score range 0..1, non-negative counts, and parent references.
5. Deduplicate claims by `DemoClaimId`, attempts by `DemoAttemptId`, and status events by `(DemoClaimId, EventUtc, NewStatus)`.
6. Aggregate status events to one row per claim before any claim-level join.
7. Keep `public_document_summary.csv` as a separate subject; never join it to claim facts to imply dispute rates.
8. Output separate clean CSVs or a Hyper extract. Retain rejected rows in a quarantine output if a future internal source is added.
9. Reconcile row counts and dashboard totals to `data_quality_summary.csv`.

## Workbook handoff

Recommended dashboard tabs are Claim Operations, Intake Automation, Model Quality, and Public Record Explorer. Every dashboard should show `SourceDesignation`/synthetic origin, `SnapshotUtc`, model version where relevant, and the limitations in `MetricsDefinitions.md`.

Tableau Public publication remains a manual account action. Do not invent a workbook URL or imply that local CSV generation refreshes a published workbook automatically.
