# Repository Map

This map explains where current project work lives and which files are intentionally kept. It is not an install or deployment guide.

## Diagram Style

Architecture and data-flow diagrams use simple rectangular blocks connected by arrows. README files explain why each flow is separated. The shared application architecture is in [system-context.mmd](diagrams/system-context.mmd).

## Current Layout

| Location | Purpose |
|---|---|
| `README.md` | Project summary, stack, architecture, local setup, analytics interpretation, and future scope. |
| `TitleClaimTracker/ClaimTracker/` | ASP.NET Core API, identity, claim services, SQL mapping, PDF handling, and extraction integration. |
| `TitleClaimTracker/ClientApp/` | Angular claimant and administrator experience, including Claims analytics. |
| `TitleClaimTracker.Tests/Backend/` | .NET unit, service, security, workflow, and PDF analysis tests. |
| `TitleClaimTracker/Database/` | SQL Server contract, versioned upgrades, verification scripts, and historical archives. |
| `TitleClaimTracker/Data/` | Separate public ACRIS, ML-labeling, and synthetic data tracks. |
| `TitleClaimTracker/Analytics/` | Synthetic analytics preparation and optional read-only PySpark export. |
| `TitleClaimTracker/IntelligenceService/` | Optional Python HTTP extraction/classification service and synthetic trainer. |
| `docs/` | Application notes, data guides, architecture, schema guidance, and repository records. |

## Preservation and Cleanup

- The canonical 2.0.3 SQL upgrade remains unchanged. Optional PDF storage is an additive 2.0.4 upgrade with its own verifier; the app does not run either script at startup.
- Archived SQL and the legacy project README are retained for reference, not as supported deployment paths.
- Checked-in synthetic CSVs are small source fixtures. Generated snapshots, local ACRIS outputs, reports, caches, binaries, and Angular bundles are not source documentation.
- Existing local data and generated files were left in place. Angular `.angular/` and `dist/` directories are ignored for future untracked output; ignore rules do not remove files already tracked by Git.
- No database, import, public-data download, Tableau publication, or deployment was performed as part of this documentation cleanup.
