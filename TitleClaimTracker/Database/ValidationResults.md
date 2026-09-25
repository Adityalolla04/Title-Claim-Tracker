# Validation results â€” SQL upgrade 2.0.3

Performed on 2026-09-21 against the supplied repository.

## Passed

- Backend `dotnet build --no-restore`: 0 warnings, 0 errors.
- SQL Server 2016 ScriptDom parsed the upgrade and all embedded constant SQL:
  31 outer/nested batches, no syntax errors.
- Inspection script: 3 parsed batches; verification script: 3; retired initializer: 1.
- EF Core design-time relational model generated successfully offline:
  23 application/Identity tables, 197 columns.
- All 197 mapped columns match the upgrade's complete column manifest for type,
  size and nullability (default datetime precision normalized to 7).
  Six additional columns belong only to SchemaVersions deployment metadata.
- EF-generated schema also parsed successfully with SQL Server 2016 ScriptDom.
- Application foreign-key delete actions checked against the NO ACTION/Restrict policy.
- Structural checks for destination guard, transaction, absence of GO/USE,
  schema version, missing Identity role-claims table, and composite-key handling passed.
- Whitespace check passed on the source files changed in this task.

## Not executed

- Fresh SQL Server database upgrade.
- Upgrade against a restored existing database.
- Repeat execution on SQL Server, rollback injection, Identity registration/login
  against the new schema, or data/constraint execution tests.

Connections attempted with Windows authentication were rejected by the execution
environment (SSPI/encryption errors), including LocalDB. No upgrade was submitted
and no existing database was changed. Static syntax/model checks cannot substitute
for execution tests. Run DatabaseSetup.md's development-copy procedure before
using the upgrade against the original database.

## Existing repository conditions

The repository already contained substantial uncommitted work. It was preserved.
The original upgrade itself passed the modern parser; the reported SQL80001 errors
were from Visual Studio validation, not reproduced SQL Server execution failures.
The original root TableCreation.sql schema is now handled by a lossless normalization
phase. Original status/category strings are retained, not guessed or remapped.
The revised EF model still matches all 197 application columns.

An existing AuthController compile error (IList<string> passed as IReadOnlyList<string>)
was corrected using ToArray so the matching EF model/application could be built.
The repository-wide whitespace check also reports pre-existing whitespace in
TitleClaimTracker.csproj and appsettings.Development.json; those files were not changed.


## Default dependency regression

- Confirmed the prior script ordered ALTER COLUMN before default removal.
- Added an ordering check: discover/drop both defaults, convert both columns,
  recreate both defaults. Each column alteration is compiled after default removal.
- Existing datetime2 audit columns already nullable are not altered again.
- No C# model change was required by this correction.
- Live SQL execution remains unverified; no database writes were performed here.
