# Implementation Summary

This file summarizes the current local application and records the checks run for this repository revision. Detailed startup instructions are in the [root README](../README.md).

## Current Application

- Angular provides registration, login, claimant intake, Claims search, claim detail, administrator review, and role-scoped claim analytics.
- ASP.NET Core owns authentication, authorization, claim ownership, validation, persistence, audit history, idempotency, and status transitions.
- Claimants see their own active claims. Administrators can review active claims across accounts and see internal model/risk fields; those fields are not legal correctness probabilities.
- PDF attachments are private and require the separate, manually applied SQL Server 2.0.4 upgrade. Administrator analysis extracts selectable text from up to 25 pages and 24,000 characters. OCR is not enabled.
- The Python service is an optional extraction provider. Its synthetic classifier is not connected to live claim triage.
- The offline synthetic analytics preparer and SQL/PySpark exporter are separate from the live Claims-page charts. The SQL exporter is read-only and needs an approved connection, Java, and JDBC driver.

## Local Verification

The following checks passed during the current review:

- Backend tests: 30 passed, 2 legacy browser tests skipped, 0 failed.
- Angular production build completed successfully.
- PDF analysis tests covered selectable text and the no-text/scanned-PDF response.
- SQL 2.0.4 scripts passed static checks for forbidden `USE`, `GO`, `CREATE DATABASE`, and SQLCMD directives.
- API health returned `healthy`; the Angular page returned HTTP 200; the analysis route returned 401 without authentication.

The SQL upgrade was not executed, and no database was changed. The 2.0.4 storage and live SQL/PySpark export require separate database and environment validation.

Run the repeatable application checks from the repository root:

```powershell
dotnet test .\TitleClaimTracker.Tests\Backend\TitleClaimTracker.Tests.csproj
Push-Location .\TitleClaimTracker\ClientApp
npm run build
Pop-Location
```

## Remaining Work

- Exercise full claimant and administrator browser journeys, including account isolation and status changes.
- Apply and test schema 2.0.4 on a disposable SQL Server database before relying on attachments.
- Add OCR only with approved privacy and retention controls.
- Evaluate and calibrate classification using representative, reviewed data before connecting it to live decisions.
- Run the SQL/PySpark export in an approved environment and validate any Tableau workbook before publication.
- Keep deployment outside this local testing scope.
