# Execute the corrected SQL upgrade (2.0.3)

The files are in `TitleClaimTracker/Database` inside your repository.
The project is script-managed; there is no EF migrations deployment path.

## Execution order: update the existing database

1. Stop the running application and take a full backup of `TitleClaimTracker`.
2. Open a NEW SSMS query window on your usual server and select `TitleClaimTracker`.
3. Run `Inspect_TitleClaimIntelligence.sql` and retain the results.
4. Open the LATEST `Upgrade_TitleClaimIntelligence.sql`. Close any older open copies.
   Its `@ExpectedDatabase` setting now defaults to `TitleClaimTracker`, matching the
   requested in-place upgrade. Confirm the query's dropdown also selects that database.
5. Execute the WHOLE file with F5, without selecting a subsection. Leave SQLCMD mode OFF.
	  Do not run the root TableCreation.sql first.
6. If any error appears, save the Messages output and mismatch results. Do not manually
   shrink columns or disable checks. All schema phases share a rollback transaction.
7. Run the latest `Verify_TitleClaimIntelligence.sql` against the same database. Expect
   schema version `2.0.3`, no duplicate-grain rows, and no known legacy audit triggers.
   Existing category IDs/names, claim IDs, audit IDs, claim status strings and rows are retained.
8. Build/start the matching application changes in this repository using the existing
   connection string. No dataset import is required for this operation.

The earlier 51005 error intentionally stopped the transaction. The replacement script
is cumulative: do not run 2.0.1 first. A database already on 2.0.1 can also use this script.
For an optional restored-copy test, change @ExpectedDatabase and the SSMS dropdown to
the copy's name. The script never creates or switches databases.

Example application commands from the repository's `TitleClaimTracker` directory:

```powershell
dotnet build
$env:ConnectionStrings__DefaultConnection = 'Server=localhost\SQLEXPRESS01;Database=TitleClaimTracker;Integrated Security=True;TrustServerCertificate=True;'
dotnet run
```

Use your actual instance name. Environment variables apply to the current shell and its
child process. The app does not automatically run the upgrade. Enable admin bootstrap
only if required, using externally supplied `Identity__BootstrapAdmin__Enabled`,
`Identity__BootstrapAdmin__Email`, and `Identity__BootstrapAdmin__Password`; disable the
bootstrap flag after the account is provisioned. Do not store the password in SQL or Git.

## SQL Server requirements

SQL Server 2016 SP1 (13.0.4001) or newer, compatibility level at least 130.
The version check covers SP1 because the script uses CREATE OR ALTER.
Use an account allowed to create/alter the schema. The default schema needs no dataset.

## Legacy data handling

The original root TableCreation.sql schema and the newer prototype schema are now
supported, along with fresh databases and compatible 2.0.0/2.0.1 installations.

- FilingTypes.TypeName is nvarchar(100). Shorter 50-character definitions are widened.
- LegalFilings.ClaimantName is nvarchar(255), preserving longer existing names.
- AuditLog.TableName is nvarchar(128), and AuditID is widened from int to bigint.
  Existing identity values are preserved. The original audit primary key is rebuilt
  transactionally with its original name and clustering choice. Custom dependent
  indexes/foreign keys cause a specific error instead of being silently dropped.
- Properties.CreatedDate and AuditLog.ChangeDate become datetime2(7); the stored date
  values are retained. Audit ChangeDate stays nullable so unknown dates stay unknown.
  Old local timestamps are not reinterpreted as UTC. Defaults for FUTURE writes use UTC.
- Pending Review and Active Investigation are retained as legacy status labels.
  New claims still start New. Administrators may explicitly move a legacy claim to
  Under Review, Escalated, Resolved or Closed through the existing status endpoint.
  Modern claims are not automatically moved into legacy statuses.
- Existing filing category names and IDs are unchanged, including Easement Issue,
  Lien Removal and Zoning Violation. Missing modern categories are added independently;
  the script does not infer that old and new categories are semantically identical.
- The older generated-name status check constraint is replaced with the expanded
  allowed-status set. Unrelated constraints remain intact.

No category/status values are renamed, no historical timestamps are invented, and no
rows are deleted. Unknown types, widths, status values or custom dependencies still
stop the transaction with an error. The contract remains explicit, not permissive.

## Visual Studio SQL80001

The repository is an ASP.NET Core Web project, not a SQL Server Database Project; no
`.sqlproj` was found. The original script parses successfully with the SQL Server 2016
parser, so its THROW errors were not evidence that the server rejected the syntax.

Open the scripts through SSMS or a SQL editor connected to the intended SQL Server.
If you added them to a separate SSDT database project, set the standalone upgrade file's
Build Action to None and set that project's target platform to your actual server version.
Do not add the whole imperative upgrade as a schema-model Build item. Updating Visual
Studio SQL tooling may be needed if its editor still uses an old parser. Do not replace
THROW with RAISERROR or insert semicolons indiscriminately into one-line IF branches.

## What changed

- One batch with explicit destination protection and one transaction: a preflight failure
  cannot be followed by another GO batch that proceeds with the upgrade.
- Schema phases compile separately via sp_executesql after prior ALTER TABLE operations.
  This avoids referencing new columns before SQL Server sees them.
- All THROW branches have explicit BEGIN/END blocks or are inside existing blocks.
- Added missing AspNetRoleClaims and its index.
- Composite Identity primary keys are NONCLUSTERED. SQL Server 2016+ permits 1700-byte
  nonclustered keys; CHECK constraints reject combined key values above that limit.
  Individual nvarchar(450) columns and existing IDs are retained, never truncated.
  SQL Server may still issue a maximum-potential-key-length warning at creation because
  declared widths exceed 1700 bytes; the checked actual combined value is what is bounded.
- Application FKs use NO ACTION, matched by EF Restrict, avoiding multiple cascading
  paths and retaining account/model references. Deleting referenced users/models is refused;
  use account deactivation and model deactivation instead. Claims remain soft deleted.
- Full column checks catch missing-column NULL comparisons and incompatible partial schemas.
- Both known legacy filing-audit triggers are removed only in the schema transaction;
  deploy with the checked-in EF audit writer. Stop the old application during deployment.
- Existing status history is not duplicated, and 2.0.3 is recorded only after success.
- The cumulative upgrade is the only supported setup path. Root SQL schemas remain historical
  examples, not setup steps.
- Fixed the existing AuthController roles collection conversion that prevented compilation.

## Verification performed by this change

See ValidationResults.md for the exact checks and limits. No existing database was changed.
Live SQL execution must still be verified with the steps above; parsing is not execution.

## Recovery

On a failed transactional phase, inspect the error and confirm existing row counts/version
before retrying. Do not drop the new tables as a rollback technique. For recovery after a
successful deployment, restore the backup to a separate database and validate it with the
matching application revision before any deliberate replacement of a live database.
SSMS Restore Verify Only is useful but is not a substitute for testing an actual restore.


## 2.0.3 correction for Msg 5074 / 4922

The previous migration tried to alter CreatedDate before removing its default.
This release discovers the default names for Properties.CreatedDate and
AuditLog.ChangeDate, removes them first, compiles/runs the date conversions,
then recreates their UTC defaults. All steps remain in the outer transaction.
Do not manually remove defaults or execute pieces of the older script.
Close the old SSMS tab, open this release, and execute the complete file.
