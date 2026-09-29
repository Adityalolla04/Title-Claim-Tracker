# Analytics Data Products

- This project has separate analytics paths: live role-scoped metrics in the signed-in Angular Analytics page, a static Matplotlib image built from synthetic CSVs, and optional offline exports.

## Live Claims Analytics

Sign in and open **Analytics** (or **Claims** for the compact summary). The live charts are produced by `GET /api/filings/analytics`; the API filters active records to the current claimant and returns all active claimants' records only to an administrator. No account selector is accepted from the browser.

```mermaid
flowchart TD
	Account[Signed-in account] --> ClaimsPage[Angular Claims page]
	ClaimsPage --> AnalyticsApi[Analytics API]
	AnalyticsApi --> Ownership[Owner scope unless administrator]
	Ownership --> Totals[Total, open, resolved or closed]
	Ownership --> Status[Claims by status]
	Ownership --> Type[Claims by category]
	Ownership --> Month[Claims filed in six months]
	Totals --> Charts[Metrics and charts]
	Status --> Charts
	Type --> Charts
	Month --> Charts
```

The ownership filter runs before aggregation so another claimant's records do not affect a claimant's chart. Open counts exclude `Resolved` and `Closed`; category and status counts use active claims. Monthly filed counts use `DateFiled`; monthly solved counts use the first recorded transition to `Resolved` or `Closed`, for the current and previous five calendar months. These are operational workflow counts, not legal outcomes or model-quality measures.

To test the charts, register two claimant accounts and create several fictional claims through the intake page. Sign in as each account and compare totals. Then use an administrator account to change statuses and confirm the admin sees the combined active claims and updated status counts. The synthetic CSV generator below does not seed SQL Server and will not change these charts.

## Offline Synthetic Demo

Run from the repository root. This writes outside the repository so demo outputs do not appear as hundreds of untracked files:

```powershell
$tableauOutput = Join-Path $env:TEMP 'TitleClaimTracker-Tableau-Demo'
& .\TitleClaimTracker\Analytics\Scripts\Prepare-Analytics.ps1 -OutputDirectory $tableauOutput
```

The PowerShell preparer writes seven fictional CSVs under `$tableauOutput/data/` and a quality report at `$tableauOutput/quality-report.json`. It replaces CSVs in that output folder and does not query or update SQL Server. These files are not measured live activity. Connect Tableau Desktop to the CSVs in the `data` folder; do not combine the separate subjects into one flat join.

### Matplotlib README Dashboard

Generate a static comparison image from the same validated synthetic inputs:

```powershell
Push-Location .\TitleClaimTracker\Analytics
python -m pip install -e ".[dashboard,test]"
python .\dashboard.py --data-dir "$tableauOutput\data" --output .\dashboard-preview.png
python -m pytest -q -p no:cacheprovider
Pop-Location
```

![Synthetic claims and triage dashboard preview](dashboard-preview.png)

The preview compares filed versus solved by month, current status, triage outcomes, and class-level F1. It is synthetic and static; it does not show signed-in account data. The application Analytics page is the live, role-scoped dashboard.

## Optional SQL/PySpark Export

The PySpark runner reads allowlisted fields through a read-only SQL login. It excludes claimant names, narratives, extracted personal text, raw payloads, and user identifiers; source keys are pseudonymized per run. Its output contains operational data and is not approved for Tableau Public.

This path requires Java 17+, the analytics Python dependencies, the Microsoft SQL Server JDBC JAR, and an approved read-only account. Set `TCT_ANALYTICS_JDBC_URL`, `TCT_ANALYTICS_JDBC_USER`, `TCT_ANALYTICS_JDBC_PASSWORD`, and `TCT_SQLSERVER_JDBC_JAR` through the current process or a secret manager. Never commit credentials.

```powershell
Push-Location .\TitleClaimTracker\Analytics
python -m pip install -e ".[test]"
python -m pytest -q -p no:cacheprovider
Pop-Location
python .\TitleClaimTracker\Analytics\pyspark_pipeline.py --confirm-read-only-export
```

The explicit confirmation flag is required before a connection attempt. The runner does not write to SQL Server. Runtime export needs a separately approved environment; the local test command validates query and privacy rules only.

Use `DataDictionary.md`, `MetricsDefinitions.md`, and `Tableau/PrepFlowInstructions.md` for dataset fields and worksheet definitions. Aggregate status history before joining it to claims, and publish only reviewed synthetic data to Tableau Public.

For click-by-click Tableau Desktop steps and a paste-ready Copilot prompt, see [Copilot Dashboard Instructions](Tableau/CopilotDashboardInstructions.md).
