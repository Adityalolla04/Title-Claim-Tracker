# Tableau Desktop and Copilot Dashboard Instructions

GitHub Copilot is available in VS Code, not as a built-in tab in Tableau Desktop. Use Copilot Chat for the workbook plan, field calculations, and troubleshooting while you create the workbook in Tableau Desktop. Do not ask it to fabricate results or treat generated Tableau XML as a validated workbook.

## 1. Generate the Synthetic Inputs

From the repository root in PowerShell, run:

```powershell
$tableauOutput = Join-Path $env:TEMP 'TitleClaimTracker-Tableau-Demo'
& .\TitleClaimTracker\Analytics\Scripts\Prepare-Analytics.ps1 -OutputDirectory $tableauOutput
$dataFolder = Join-Path $tableauOutput 'data'
```

Open `$tableauOutput\quality-report.json` and confirm `Status` is `Passed` and `IsSynthetic` is `true`. The seven CSVs are in `$dataFolder`. The preparer replaces CSVs in that folder; it does not use SQL Server. This fixture is intentionally small and demonstrates workbook construction, not real operational trends.

## 2. Ask Copilot for Tableau Help

Open Copilot Chat in VS Code with this repository as context and paste:

```text
Help me build a Tableau Desktop workbook manually from this repository's synthetic analytics CSVs. Read Analytics/DataDictionary.md, Analytics/MetricsDefinitions.md, Analytics/Tableau/PrepFlowInstructions.md, and this CopilotDashboardInstructions.md first.

Use only the generated CSVs under my local TitleClaimTracker-Tableau-Demo/data folder. Do not use SQL Server, production data, claimant identities, or invented rows. Do not generate or edit Tableau TWB/TWBX XML. Give me one Tableau Desktop action at a time and wait for me to confirm before continuing.

For each worksheet, name the sheet, state the CSV/data source, exact fields and aggregations, shelf placement, chart type, filters, title, and a short interpretation. Use COUNTD(DemoClaimId) for claims and COUNTD(DemoAttemptId) for triage attempts. Keep claims, status events, triage attempts, model evaluation, and public-document volume as separate subjects. Never join raw status-history events to claims before aggregating events to one row per claim. Preserve NULL model scores and label the source synthetic.

Plan these four dashboard tabs: Claim Operations, Intake Outcomes, Model Evaluation, and Public Document Volume. Flag any metric whose definition, denominator, provenance, or sample count is unclear rather than guessing.
```

Copilot can suggest Tableau calculations and help interpret errors, but you must apply and verify its advice in Tableau Desktop.

## 3. Connect the Files in Tableau Desktop

1. Start Tableau Desktop and select **Connect > To a File > Text file**.
2. Open `claims.csv` from `$dataFolder`. Check that `DemoClaimId` is a string, `CreatedUtc` and `ResolvedUtc` are dates/times, and `Priority` and `ModelScore` are numeric. Keep null `ModelScore` values null.
3. Add `status_history.csv`, `triage_attempts.csv`, `model_metrics.csv`, `confusion_matrix.csv`, `public_document_summary.csv`, and `data_quality_summary.csv` as separate data sources when needed. Do not physically join all files into one table.
4. Keep status events as their own data source, or aggregate them to one row per `DemoClaimId` before creating a relationship to claims. Raw status history has multiple rows per claim and will inflate counts when joined directly.

## 4. Build the Sheets and Dashboards

**Claim Operations**

- KPI: `COUNTD([DemoClaimId])` for total claims.
- KPI: create `Open Flag` as `IF [CurrentStatus] = 'Resolved' OR [CurrentStatus] = 'Closed' THEN 0 ELSE 1 END`; use `SUM([Open Flag])` for open claims.
- Bar chart: `CurrentStatus` or `PrimaryCategory` on Rows; `COUNTD([DemoClaimId])` on Columns.
- Monthly submissions: `DATETRUNC('month', [CreatedUtc])` on Columns; `COUNTD([DemoClaimId])` on Rows.

**Intake Outcomes**

- Use `triage_attempts.csv`; count `COUNTD([DemoAttemptId])` by `Outcome`.
- Present automatic-creation and needs-information rates only with the denominators defined in `MetricsDefinitions.md`.

**Model Evaluation**

- Use `model_metrics.csv` for metric values by `MetricName` and `ClassName`; include model version, dataset version, provenance, and sample count.
- Use `confusion_matrix.csv` separately for an actual-versus-predicted highlight table.
- Label scores as uncalibrated model metrics, not legal correctness probabilities. Do not present the small synthetic fixture as evidence of production performance.

**Public Document Volume**

- Use `public_document_summary.csv`; chart `SUM([DocumentCount])` by `SourceDocumentType` and/or `Geography`.
- Keep this as document volume. Do not label it as claims, disputes, or legal outcomes.

Add the four sheets to dashboards with clear titles, units, and a visible **Synthetic demonstration data** notice. Use `SnapshotUtc` from the generated data to state when the fixture was prepared.

## 5. Validate and Save

Compare worksheet counts with the source CSV rows and the quality report. Confirm the claim total is counted distinctly, no claim counts multiply after relationships, NULL scores are not converted to zero, and every dashboard identifies the synthetic source. Save a local packaged workbook (`.twbx`) so the CSV extracts travel with it. Publish to Tableau Public only after reviewing the packaged contents and confirming that no private or production data is included.

This Tableau workbook is separate from the application's live, role-scoped analytics. It does not show an account's claims and is not refreshed from application sign-ins.