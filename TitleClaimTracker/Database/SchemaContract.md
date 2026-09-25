# Title Claim Intelligence database contract

## Authority and target

- **Database:** explicitly selected target; default guard `TitleClaimTracker`
- **Schema:** `dbo`
- **SQL Server:** SQL Server 2016 SP1 or later, compatibility level 130 or later. The default portfolio setup uses SQL Server Express/Developer/Standard-compatible features only; no Enterprise-only feature is required.
- **Schema authority:** `Database/Upgrade_TitleClaimIntelligence.sql` is the versioned, reviewed deployment artifact for the existing script-managed database. The application does not call `Database.Migrate()` and does not create or alter tables at startup.
- **Version:** `2.0.3`, recorded in `dbo.SchemaVersions`.
- **EF Core:** the application uses EF Core 8.0.11 and ASP.NET Core Identity EF Core 8.0.11. `Infrastructure/Data/TitleClaimDbContext.cs` is the matching object-relational mapping, not a second deployment definition.
- **Legacy baseline:** the repository contained no `Migrations` directory and no `__EFMigrationsHistory`. Existing databases are upgraded in place by the SSMS script. Do not run an EF migration or apply the SSMS script twice through a different path. A future schema change must add a new reviewed script version and matching mappings.

The upgrade never creates, drops or switches databases, or drops/recreates legacy application tables. See DatabaseSetup.md for the supported schema variants. It creates absent objects individually, preserves identity values and rows, and throws a numbered error when a required existing object has an incompatible shape.

## Server-generated values and write ownership

The server/application must generate or control the following values:

- `Identity` account IDs, password hashes, security stamps, concurrency stamps, normalized values, and role membership are generated through ASP.NET Core Identity APIs. SQL never creates a password, hash, or administrator account.
- Identity roles are `User` and `Admin`. The optional application bootstrap reads `Identity:BootstrapAdmin:Email`, `Identity:BootstrapAdmin:Password`, and `Identity:BootstrapAdmin:Enabled` from external configuration. It uses `RoleManager`/`UserManager`; it does not log or persist a configured password in SQL text.
- Account and application UTC timestamps use `SYSUTCDATETIME()` defaults. Claim `UpdatedUtc`, soft-delete metadata, status history, and audit actor values are assigned by the authenticated application write path.
- `LegalFilings.RowVersion` is SQL Server `rowversion` and is an EF concurrency token. An update that affects zero rows because the rowversion changed fails instead of overwriting a concurrent update.
- Identity and application surrogate keys are `IDENTITY` values. Public upserts use `(DataSourceID, ExternalPropertyKey)` and `(DataSourceID, ExternalDocumentKey)`, not an address or a generated claim ID.
- `IdempotencyRecords.PayloadHash` is a server-calculated SHA-256 hash of the canonical request payload. The same user, operation, key, and different payload hash is a conflict.

## Tables

Types below are SQL Server types. `NULL` and `NOT NULL` are part of the contract. Defaults are shown exactly as deployed by the upgrade script. Existing legacy columns are retained unless explicitly noted.

### `SchemaVersions`

| Column | Type | Nullability | Default |
|---|---|---|---|
| `SchemaVersionID` | `int IDENTITY(1,1)` | NOT NULL | generated |
| `Version` | `nvarchar(32)` | NOT NULL | none |
| `AppliedUtc` | `datetime2(7)` | NOT NULL | `SYSUTCDATETIME()` |
| `AppliedBy` | `nvarchar(128)` | NOT NULL | `SUSER_SNAME()` |
| `ScriptName` | `nvarchar(260)` | NOT NULL | none |
| `Description` | `nvarchar(500)` | NOT NULL | none |

Primary key: `PK_SchemaVersions (SchemaVersionID)`. Unique key: `UQ_SchemaVersions_Version (Version)`. This table is deployment metadata and is not an Identity or claim table.

### `Properties` (legacy, reused)

| Column | Type | Nullability | Default |
|---|---|---|---|
| `PropertyID` | `int IDENTITY(1,1)` | NOT NULL | generated |
| `Address` | `nvarchar(255)` | NOT NULL | none |
| `City` | `nvarchar(100)` | NOT NULL | none |
| `State` | `char(2)` | NOT NULL | none |
| `LegalDescription` | `nvarchar(max)` | NULL | none |
| `CreatedDate` | `datetime2(7)` | NOT NULL | `SYSUTCDATETIME()` |

Primary key: `PK_Properties (PropertyID)`. Check: `State` must match `[A-Z][A-Z]`. A property is not deleted by a claim soft delete. `LegalFilings.PropertyID` references this table with no cascade.

### `FilingTypes` (legacy, reused)

| Column | Type | Nullability | Default |
|---|---|---|---|
| `FilingTypeID` | `int IDENTITY(1,1)` | NOT NULL | generated |
| `TypeName` | `nvarchar(100)` | NOT NULL | none |

Primary key: `PK_FilingTypes (FilingTypeID)`. Unique key/index: `UQ_FilingTypes_TypeName (TypeName)`. Controlled seed values are `Title Claim`, `Lien`, `Easement`, and `Deed Dispute`.

### `LegalFilings` (legacy, reused and extended)

| Column | Type | Nullability | Default |
|---|---|---|---|
| `FilingID` | `int IDENTITY(1,1)` | NOT NULL | generated |
| `PropertyID` | `int` | NOT NULL | none |
| `FilingTypeID` | `int` | NOT NULL | none; this is the primary filing type |
| `DateFiled` | `date` | NOT NULL | none |
| `Status` | `nvarchar(50)` | NOT NULL | `N'New'` |
| `ClaimantName` | `nvarchar(255)` | NULL | none; extracted claimant/owner, not the account |
| `Notes` | `nvarchar(max)` | NULL | none |
| `RiskScore` | `float` | NULL | none; legacy heuristic risk score, not a legal outcome or calibrated correctness probability |
| `SubmittedByUserId` | `nvarchar(450)` | NULL | none; NULL is retained for legacy/unassigned rows |
| `CreatedUtc` | `datetime2(7)` | NOT NULL | `SYSUTCDATETIME()` |
| `UpdatedUtc` | `datetime2(7)` | NOT NULL | `SYSUTCDATETIME()` |
| `CreationSource` | `nvarchar(20)` | NOT NULL | `N'Manual'`; allowed `Manual`, `Assisted` |
| `IssueTags` | `nvarchar(max)` | NULL | none; JSON when present |
| `ClassificationScore` | `decimal(9,8)` | NULL | none; model classification score in `[0,1]`, not a probability of legal correctness |
| `ModelVersionID` | `bigint` | NULL | none |
| `TriagePriority` | `tinyint` | NOT NULL | `3`; heuristic priority where `1` is most urgent and `5` least urgent |
| `TriageRuleVersion` | `nvarchar(50)` | NOT NULL | `N'triage-v1'` |
| `RowVersion` | `rowversion` | NOT NULL | SQL Server generated |
| `IsDeleted` | `bit` | NOT NULL | `0` |
| `DeletedUtc` | `datetime2(7)` | NULL | none |
| `DeletedByUserId` | `nvarchar(450)` | NULL | none |
| `IsSyntheticDemoData` | `bit` | NOT NULL | `0` |

Primary key: `PK_LegalFilings (FilingID)`. Foreign keys:

- `FK_LegalFilings_Properties (PropertyID)` -> `Properties(PropertyID)`, no cascade.
- `FK_LegalFilings_FilingTypes (FilingTypeID)` -> `FilingTypes(FilingTypeID)`, no cascade.
- `FK_LegalFilings_SubmittedByUser (SubmittedByUserId)` -> `AspNetUsers(Id)`, `ON DELETE NO ACTION`.
- `FK_LegalFilings_DeletedByUser (DeletedByUserId)` -> `AspNetUsers(Id)`, `ON DELETE NO ACTION`.
- `FK_LegalFilings_ModelVersion (ModelVersionID)` -> `ModelVersions(ModelVersionID)`, `ON DELETE NO ACTION`.

Checks:

- `Status` is one of `New`, `Under Review`, `Escalated`, `Resolved`, `Closed`.
- `RiskScore` is NULL or between 0 and 1.
- `ClassificationScore` is NULL or between 0 and 1.
- `CreationSource` is `Manual` or `Assisted`.
- `TriagePriority` is 1 through 5.
- `IssueTags` is NULL or valid SQL Server JSON.
- An active row has no deletion timestamp/account; a soft-deleted row has `IsDeleted = 1`. The application supplies the deletion timestamp and actor.

Ownership: application-created claims require an authenticated `SubmittedByUserId`. Regular-user reads are restricted to that account and active rows. Administrators can read active rows across accounts, including NULL-owner legacy rows. The upgrade does not assign any existing row to an arbitrary account. The current API uses soft deletion, so history is not removed.

### `AuditLog` (legacy, reused and extended)

| Column | Type | Nullability | Default |
|---|---|---|---|
| `AuditID` | `bigint IDENTITY(1,1)` | NOT NULL | generated |
| `TableName` | `nvarchar(128)` | NOT NULL | none |
| `RecordID` | `int` | NOT NULL | none; currently the `LegalFilings.FilingID` key |
| `ColumnChanged` | `nvarchar(100)` | NULL | none |
| `OldValue` | `nvarchar(max)` | NULL | none |
| `NewValue` | `nvarchar(max)` | NULL | none |
| `ChangeDate` | `datetime2(7)` | NULL | `SYSUTCDATETIME()` |
| `ActorUserId` | `nvarchar(450)` | NULL | none |
| `Action` | `nvarchar(30)` | NOT NULL | `N'Update'`; allowed `Insert`, `Update`, `SoftDelete`, `StatusTransition` |

Primary key: `PK_AuditLog (AuditID)`. Foreign key: `FK_AuditLog_ActorUser (ActorUserId)` -> `AspNetUsers(Id)`, `ON DELETE NO ACTION`. Index: `IX_AuditLog_Record (TableName, RecordID, ChangeDate)`.

EF Core is the single authoritative audit writer. The upgrade drops `dbo.trg_LegalFilings_Audit`; it does not leave both a trigger and EF writer active. EF writes audit rows and status history in the same transaction as a claim change. Legacy audit rows keep a NULL actor and are not rewritten.

### ASP.NET Core Identity tables

The following tables use the standard ASP.NET Core Identity 8 string-key shape. Identity creates account credentials through its APIs; the upgrade contains no user rows.

#### `AspNetUsers`

| Column | Type | Nullability | Default |
|---|---|---|---|
| `Id` | `nvarchar(450)` | NOT NULL | application generated |
| `UserName` | `nvarchar(256)` | NULL | none |
| `NormalizedUserName` | `nvarchar(256)` | NULL | Identity generated |
| `Email` | `nvarchar(256)` | NULL | none |
| `NormalizedEmail` | `nvarchar(256)` | NULL | Identity generated |
| `EmailConfirmed` | `bit` | NOT NULL | none; Identity writes it |
| `PasswordHash` | `nvarchar(max)` | NULL | Identity generated when password is set |
| `SecurityStamp` | `nvarchar(max)` | NULL | Identity generated |
| `ConcurrencyStamp` | `nvarchar(max)` | NULL | Identity generated |
| `PhoneNumber` | `nvarchar(max)` | NULL | none |
| `PhoneNumberConfirmed` | `bit` | NOT NULL | none |
| `TwoFactorEnabled` | `bit` | NOT NULL | none |
| `LockoutEnd` | `datetimeoffset(7)` | NULL | none |
| `LockoutEnabled` | `bit` | NOT NULL | none |
| `AccessFailedCount` | `int` | NOT NULL | none |
| `DisplayName` | `nvarchar(200)` | NULL | none |
| `CreatedUtc` | `datetime2(7)` | NOT NULL | `SYSUTCDATETIME()` |
| `IsActive` | `bit` | NOT NULL | `1` |

Primary key: `PK_AspNetUsers (Id)`. Indexes: filtered unique `UserNameIndex (NormalizedUserName)` and `EmailIndex (NormalizedEmail)`.

#### `AspNetRoles`

Columns: `Id nvarchar(450) NOT NULL` (PK `PK_AspNetRoles`), `Name nvarchar(256) NULL`, `NormalizedName nvarchar(256) NULL`, and `ConcurrencyStamp nvarchar(max) NULL`. Filtered unique index: `RoleNameIndex (NormalizedName)`.

#### `AspNetUserClaims`

Columns: `Id int IDENTITY(1,1) NOT NULL` (PK `PK_AspNetUserClaims`), `UserId nvarchar(450) NOT NULL`, `ClaimType nvarchar(max) NULL`, `ClaimValue nvarchar(max) NULL`. FK `FK_AspNetUserClaims_AspNetUsers_UserId` cascades with the Identity user record. Index: `IX_AspNetUserClaims_UserId`.

#### `AspNetUserLogins`

Columns: `LoginProvider nvarchar(450) NOT NULL`, `ProviderKey nvarchar(450) NOT NULL`, `ProviderDisplayName nvarchar(max) NULL`, `UserId nvarchar(450) NOT NULL`. Composite PK `PK_AspNetUserLogins (LoginProvider, ProviderKey)`. FK to `AspNetUsers` cascades. Index: `IX_AspNetUserLogins_UserId`.

#### `AspNetUserRoles`

Columns: `UserId nvarchar(450) NOT NULL`, `RoleId nvarchar(450) NOT NULL`. Composite PK `PK_AspNetUserRoles (UserId, RoleId)`. FKs to users and roles cascade as required by Identity. Index: `IX_AspNetUserRoles_RoleId`.

#### `AspNetUserTokens`

Columns: `UserId nvarchar(450) NOT NULL`, `LoginProvider nvarchar(450) NOT NULL`, `Name nvarchar(450) NOT NULL`, `Value nvarchar(max) NULL`. Composite PK `PK_AspNetUserTokens (UserId, LoginProvider, Name)`. FK to `AspNetUsers` cascades.

No cascade relationship from Identity users to audit or claim/status history is defined. Nullable actor/owner references use `NO ACTION` where the history can survive account removal; required operational references use `NO ACTION`.

### `ClaimStatusHistory`

| Column | Type | Nullability | Default |
|---|---|---|---|
| `StatusHistoryID` | `bigint IDENTITY(1,1)` | NOT NULL | generated |
| `FilingID` | `int` | NOT NULL | none |
| `FromStatus` | `nvarchar(50)` | NULL | none; NULL is an initial/baseline record |
| `ToStatus` | `nvarchar(50)` | NOT NULL | none |
| `ActorUserId` | `nvarchar(450)` | NULL | none |
| `TransitionedUtc` | `datetime2(7)` | NOT NULL | `SYSUTCDATETIME()` |
| `Reason` | `nvarchar(500)` | NULL | none |

PK: `PK_ClaimStatusHistory (StatusHistoryID)`. FKs: `FilingID` -> `LegalFilings` with `NO ACTION`; `ActorUserId` -> `AspNetUsers` with `NO ACTION`. Indexes: `IX_ClaimStatusHistory_Filing_Time (FilingID, TransitionedUtc, StatusHistoryID)` and `IX_ClaimStatusHistory_Actor_Time (ActorUserId, TransitionedUtc)`. The upgrade adds one `FromStatus = NULL` baseline row per existing filing and does not invent an actor.

Allowed application transitions are:

```text
New          -> Under Review, Escalated, Closed
Under Review -> Escalated, Resolved, Closed
Escalated    -> Under Review, Resolved, Closed
Resolved     -> Closed
Closed       -> none
```

A baseline row may use NULL -> any current valid legacy status. New application claims start at `New`. The EF service checks the transition map and the database check constraint rejects other history pairs. A same-status request does not create a transition.

### `TriageAttempts`

| Column | Type | Nullability | Default |
|---|---|---|---|
| `TriageAttemptID` | `bigint IDENTITY(1,1)` | NOT NULL | generated |
| `SubmittedByUserId` | `nvarchar(450)` | NOT NULL | authenticated account |
| `InputText` | `nvarchar(max)` | NOT NULL | none; internal/private |
| `PredictedFilingTypeID` | `int` | NULL | none |
| `ClassificationScore` | `decimal(9,8)` | NULL | none; `[0,1]`, not correctness probability |
| `HeuristicPriority` | `tinyint` | NULL | none; 1 through 5 |
| `TriageRuleVersion` | `nvarchar(50)` | NOT NULL | none |
| `ModelVersionID` | `bigint` | NULL | none |
| `CreatedFilingID` | `int` | NULL | none; NULL when no claim was created |
| `Outcome` | `nvarchar(30)` | NOT NULL | `N'AwaitingConfirmation'`; `AwaitingConfirmation`, `Created`, `Rejected`, or `Failed` |
| `CreatedUtc` | `datetime2(7)` | NOT NULL | `SYSUTCDATETIME()` |
| `CompletedUtc` | `datetime2(7)` | NULL | none |
| `ExtractedClaimantName` | `nvarchar(200)` | NULL | none |
| `ExtractedAddress` | `nvarchar(255)` | NULL | none |
| `ExtractedCity` | `nvarchar(100)` | NULL | none |
| `ExtractedState` | `char(2)` | NULL | none |
| `ExtractedNotes` | `nvarchar(max)` | NULL | none |
| `FailureCode` | `nvarchar(100)` | NULL | none |
| `FailureMessage` | `nvarchar(max)` | NULL | none |

PK: `PK_TriageAttempts (TriageAttemptID)`. FKs: submitting user `NO ACTION`; predicted filing type `NO ACTION`; model version `NO ACTION`; created filing `NO ACTION`. Index: `IX_TriageAttempts_User_CreatedUtc`. A triage attempt is recorded before/without a claim, so a confirmation request or failure is not lost.

### `TriageFeedback`

| Column | Type | Nullability | Default |
|---|---|---|---|
| `TriageFeedbackID` | `bigint IDENTITY(1,1)` | NOT NULL | generated |
| `TriageAttemptID` | `bigint` | NOT NULL | none |
| `ReviewedByUserId` | `nvarchar(450)` | NOT NULL | authenticated reviewer |
| `WasCorrect` | `bit` | NOT NULL | none |
| `CorrectedFilingTypeID` | `int` | NULL | none; required when `WasCorrect = 0` |
| `CorrectedIssueTags` | `nvarchar(max)` | NULL | none |
| `CorrectionNotes` | `nvarchar(max)` | NULL | none |
| `ReviewedUtc` | `datetime2(7)` | NOT NULL | `SYSUTCDATETIME()` |

PK: `PK_TriageFeedback (TriageFeedbackID)`. Unique key/index: `UQ_TriageFeedback_TriageAttempt (TriageAttemptID)`, so one reviewed correction belongs to one prediction. FKs to `TriageAttempts` and reviewer use `NO ACTION`; corrected filing type uses `NO ACTION`.

### `ModelVersions`

| Column | Type | Nullability | Default |
|---|---|---|---|
| `ModelVersionID` | `bigint IDENTITY(1,1)` | NOT NULL | generated |
| `ModelName` | `nvarchar(100)` | NOT NULL | none |
| `Version` | `nvarchar(50)` | NOT NULL | none |
| `ArtifactUri` | `nvarchar(2048)` | NULL | none |
| `ArtifactSha256` | `binary(32)` | NULL | none |
| `DatasetProvenance` | `nvarchar(max)` | NOT NULL | none |
| `TrainedUtc` | `datetime2(7)` | NULL | none |
| `RegisteredUtc` | `datetime2(7)` | NOT NULL | `SYSUTCDATETIME()` |
| `IsActive` | `bit` | NOT NULL | `0` |

PK: `PK_ModelVersions (ModelVersionID)`. Unique key/index: `UQ_ModelVersions_Name_Version (ModelName, Version)`. Filings and triage attempts reference a version with `NO ACTION`; evaluations reference it with `NO ACTION`.

### `ModelEvaluationResults`

| Column | Type | Nullability | Default |
|---|---|---|---|
| `ModelEvaluationResultID` | `bigint IDENTITY(1,1)` | NOT NULL | generated |
| `ModelVersionID` | `bigint` | NOT NULL | none |
| `DatasetName` | `nvarchar(200)` | NOT NULL | none |
| `DatasetVersion` | `nvarchar(100)` | NOT NULL | none |
| `DatasetProvenance` | `nvarchar(max)` | NOT NULL | none |
| `EvaluatedUtc` | `datetime2(7)` | NOT NULL | none |
| `SampleCount` | `bigint` | NOT NULL | none; >= 0 |
| `Accuracy` | `decimal(9,8)` | NOT NULL | none; [0,1] |
| `MacroF1` | `decimal(9,8)` | NOT NULL | none; [0,1] |
| `WeightedF1` | `decimal(9,8)` | NOT NULL | none; [0,1] |
| `PrecisionScore` | `decimal(9,8)` | NULL | none; [0,1] when present |
| `RecallScore` | `decimal(9,8)` | NULL | none; [0,1] when present |
| `Notes` | `nvarchar(max)` | NULL | none |

PK: `PK_ModelEvaluationResults (ModelEvaluationResultID)`. Unique key/index: `UQ_ModelEvaluationResults_Model_Dataset (ModelVersionID, DatasetName, DatasetVersion)`. FK to `ModelVersions` is `NO ACTION`. Dataset provenance is retained with each evaluation; metrics are not inferred from claims.

### `IdempotencyRecords`

| Column | Type | Nullability | Default |
|---|---|---|---|
| `IdempotencyRecordID` | `bigint IDENTITY(1,1)` | NOT NULL | generated |
| `UserId` | `nvarchar(450)` | NOT NULL | authenticated account |
| `OperationName` | `nvarchar(100)` | NOT NULL | server-defined operation |
| `IdempotencyKey` | `nvarchar(200)` | NOT NULL | client request key |
| `PayloadHash` | `varbinary(32)` | NOT NULL | server SHA-256 |
| `State` | `nvarchar(20)` | NOT NULL | `N'Started'`; `Started`, `Completed`, or `Failed` |
| `ResponseStatusCode` | `int` | NULL | none |
| `ResponseBody` | `nvarchar(max)` | NULL | none; private response replay data |
| `CreatedUtc` | `datetime2(7)` | NOT NULL | `SYSUTCDATETIME()` |
| `CompletedUtc` | `datetime2(7)` | NULL | none |

PK: `PK_IdempotencyRecords (IdempotencyRecordID)`. Unique scope: `UQ_IdempotencyRecords_User_Operation_Key (UserId, OperationName, IdempotencyKey)`. FK `UserId` -> `AspNetUsers(Id)` uses `NO ACTION`. The server obtains the authenticated user, canonicalizes and hashes the payload, then atomically inserts/locks this scope. A completed matching hash replays the stored response; a different hash returns a conflict; an in-progress record is not duplicated.

### Public-record structures

These tables are reference/staging data only. No row in them is inserted into `LegalFilings` by the upgrade, and an external document is not an application filing or legal dispute.

#### `DataSources`

`DataSourceID int IDENTITY(1,1) NOT NULL` (PK `PK_DataSources`), `SourceName nvarchar(200) NOT NULL`, `SourceUrl nvarchar(2048) NOT NULL`, `ProvenanceStatement nvarchar(max) NOT NULL`, `IsActive bit NOT NULL DEFAULT 1`, `CreatedUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME()`, `UpdatedUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME()`. Unique key/index: `UQ_DataSources_SourceName (SourceName)`. The source URL is retained in full but not indexed because `nvarchar(2048)` exceeds SQL Server's safe nonclustered key size.

#### `IngestionRuns`

`IngestionRunID bigint IDENTITY(1,1) NOT NULL` (PK `PK_IngestionRuns`), `DataSourceID int NOT NULL`, `Status nvarchar(20) NOT NULL DEFAULT N'Pending'`, `StartedUtc datetime2(7) NULL`, `CompletedUtc datetime2(7) NULL`, `LastCheckpoint nvarchar(500) NULL`, `RowsRead bigint NOT NULL DEFAULT 0`, `RowsAccepted bigint NOT NULL DEFAULT 0`, `RowsUpdated bigint NOT NULL DEFAULT 0`, `RowsRejected bigint NOT NULL DEFAULT 0`, `ErrorCount bigint NOT NULL DEFAULT 0`, `CreatedUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME()`. Status values: `Pending`, `Running`, `Completed`, `Failed`, `Cancelled`. Counts must be nonnegative. FK `DataSourceID` -> `DataSources` uses `NO ACTION`; index `IX_IngestionRuns_Source_CreatedUtc (DataSourceID, CreatedUtc)`.

#### `IngestionQuarantineErrors`

`QuarantineErrorID bigint IDENTITY(1,1) NOT NULL` (PK `PK_IngestionQuarantineErrors`), `IngestionRunID bigint NOT NULL`, `ExternalRecordKey nvarchar(500) NOT NULL`, `ErrorCode nvarchar(100) NOT NULL`, `ErrorMessage nvarchar(max) NOT NULL`, `RawPayload nvarchar(max) NULL`, `CreatedUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME()`, `ResolvedUtc datetime2(7) NULL`. FK to `IngestionRuns` uses `NO ACTION`; index `IX_IngestionQuarantineErrors_Run_Record (IngestionRunID, ExternalRecordKey)`.

#### `ExternalProperties`

`ExternalPropertyID bigint IDENTITY(1,1) NOT NULL` (PK `PK_ExternalProperties`), `DataSourceID int NOT NULL`, `ExternalPropertyKey nvarchar(200) NOT NULL`, `ParcelIdentifier nvarchar(100) NULL`, `Address nvarchar(255) NULL`, `City nvarchar(100) NULL`, `State char(2) NULL`, `County nvarchar(100) NULL`, `RawPayload nvarchar(max) NULL`, `FirstSeenUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME()`, `LastSeenUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME()`. Unique upsert key/index `UQ_ExternalProperties_Source_Key (DataSourceID, ExternalPropertyKey)`. Additional index `IX_ExternalProperties_Source_Parcel (DataSourceID, ParcelIdentifier)`. FK to `DataSources` uses `NO ACTION`.

#### `ExternalPropertyDocuments`

`ExternalPropertyDocumentID bigint IDENTITY(1,1) NOT NULL` (PK `PK_ExternalPropertyDocuments`), `DataSourceID int NOT NULL`, `ExternalDocumentKey nvarchar(200) NOT NULL`, `SourceDocumentType nvarchar(100) NOT NULL`, `RecordedDate date NULL`, `DocumentUrl nvarchar(2048) NULL`, `RawPayload nvarchar(max) NULL`, `FirstSeenUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME()`, `LastSeenUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME()`, `LastIngestionRunID bigint NULL`. Unique source/document key/index `UQ_ExternalPropertyDocuments_Source_Key (DataSourceID, ExternalDocumentKey)`. Index `IX_ExternalPropertyDocuments_DocumentType (SourceDocumentType)`. FKs to `DataSources` use `NO ACTION`; `LastIngestionRunID` -> `IngestionRuns` uses `NO ACTION`.

`SourceDocumentType` is independent of `FilingTypes`; an ACRIS deed, mortgage, lien, or other public-record type is never silently converted into an application filing category.

#### `ExternalDocumentProperties`

`ExternalPropertyDocumentID bigint NOT NULL`, `ExternalPropertyID bigint NOT NULL`, `RelationshipType nvarchar(100) NOT NULL`, `SourceRelationshipKey nvarchar(200) NULL`. Composite PK `PK_ExternalDocumentProperties (ExternalPropertyDocumentID, ExternalPropertyID)`. FKs to `ExternalPropertyDocuments` and `ExternalProperties` both use `NO ACTION`; index `IX_ExternalDocumentProperties_Property (ExternalPropertyID)`. This is a many-to-many relationship: one source document may relate to multiple external properties.

## Reporting views and procedure

All reporting objects are read-only SQL projections. One-to-many joins are either absent or reduced through a unique relationship/subquery so they do not multiply claim/document rows.

| Object | Grain | Join keys and calculations | Timestamp meaning / denominator |
|---|---|---|---|
| `vw_ClaimOperations` | One row per `LegalFilings` row | Inner joins `PropertyID` and `FilingTypeID`; includes current status, ownership, source, score, priority, and soft-delete flag | `CreatedUtc`/`UpdatedUtc` are claim lifecycle timestamps; operational active-claim counts filter `IsDeleted = 0` |
| `vw_ClaimStatusTransitions` | One row per `ClaimStatusHistory` row | `FilingID` is the claim key; no property join that could multiply rows | `TransitionedUtc` is the transition event time; transition counts use rows in this view, not current claim status |
| `vw_TriageAttemptsOutcomes` | One row per `TriageAttempts` row | Left joins predicted/corrected types and the one-to-one `TriageFeedback` row | `CreatedUtc` is attempt start/persistence time and `CompletedUtc` is outcome time; denominators include attempts that did not create a claim |
| `vw_ModelEvaluationResults` | One row per `ModelEvaluationResults` row | Joins the one `ModelVersions` row by `ModelVersionID` | `EvaluatedUtc` is evaluation time; metrics and `SampleCount` come from the reviewed dataset record |
| `vw_PublicDocumentImportQuality` | One row per `IngestionRuns` row | Joins `DataSources`; scalar counts use `LastIngestionRunID` and `IngestionRunID` subqueries to avoid document/error multiplication | Run start/completion timestamps; quality denominator is the run's recorded `RowsRead`/counts |
| `vw_PublicDemoClaimExport` | One row per approved synthetic active claim | Joins property and filing type; excludes user ID, claimant, notes, descriptions, credentials, and non-synthetic rows | Contains a constant `DataClassification = 'Synthetic application data'`; it is not a production/public account export |

`sp_GetFilingRiskReport @PropertyID` returns one row per application property, excludes soft-deleted claims, averages the legacy heuristic `RiskScore`, counts open claims (`Status` not `Resolved`/`Closed`), total active claims, and distinct filing types. NULL risk scores are excluded from the average; no value is described as a legal-outcome prediction.

## Public-data and demo rules

- `DataSources`, `IngestionRuns`, `IngestionQuarantineErrors`, `ExternalProperties`, `ExternalPropertyDocuments`, and `ExternalDocumentProperties` are source/provenance data and never become user claims automatically.
- External identity is scoped by `DataSourceID` plus the source-provided key. Checkpointing uses `IngestionRuns.LastCheckpoint`; a development run can process 10,000 rows and a benchmark can process up to millions without schema changes. The upgrade inserts no synthetic bulk rows.
- Raw payloads and source URLs remain subject to source license, retention, and access controls.
- `vw_PublicDemoClaimExport` is the separate export contract. Only records explicitly marked `IsSyntheticDemoData = 1` by an application/demo seed path are eligible. Credentials, password hashes, claimant names, notes, personal descriptions, user IDs, and non-demo account/claim data are excluded.

## Migration, ownership, audit, and recovery invariants

1. The SQL script is run once per database version in SSMS. A repeated run rechecks shapes, recreates read-only definitions, leaves rows intact, and does not insert a second `2.0.3` version record.
2. A missing legacy owner is not repaired by guessing. NULL-owner rows remain administrator-only until an explicit, authenticated assignment workflow exists.
3. The extracted `ClaimantName` is not an account identity. The submitting account is `SubmittedByUserId`.
4. EF Core is the only application audit writer. The legacy trigger is removed. Raw SQL writers must use an approved application path if auditability is required.
5. Status changes use the service transition map and create actor/timestamp history. The database check prevents unsupported transition pairs but cannot independently prove that the latest history row equals the current status; direct application writes are not an approved write path.
6. Claims are soft deleted. No FK from `AuditLog` or `ClaimStatusHistory` cascades when a claim is removed, and current records remain available for historical reporting.
7. Model scores are nullable for manual claims. When present, `ClassificationScore` is a bounded model classification score, while `RiskScore` and `TriagePriority` are separately stored heuristics. Neither is a probability of legal correctness.
8. The SQL script does not provision an administrator. The application bootstrap or the registration/login APIs use Identity APIs and externally supplied secrets.


## 2.0.1 corrections

AspNetRoleClaims is included: Id int IDENTITY primary key, RoleId nvarchar(450) NOT NULL,
ClaimType and ClaimValue nvarchar(max) NULL; RoleId references AspNetRoles(Id) with CASCADE
and has IX_AspNetRoleClaims_RoleId. This matches ASP.NET Core Identity's role-claims entity.

AspNetUserLogins, AspNetUserRoles and AspNetUserTokens use NONCLUSTERED composite primary
keys. Each has CK_<table>_KeyBytes enforcing the sum of DATALENGTH of its key columns <=1700.
Individual key column sizes are unchanged. Existing values exceeding the total limit are
rejected, never truncated. The upgrade rebuilds existing clustered primary keys on these
three tables as nonclustered keys within its transaction.

All non-Identity application foreign keys use NO ACTION. EF uses Restrict consistently.
Referenced account/model deletion is blocked; deactivate records instead. The earlier
2.0.0 SET NULL design could produce multiple SQL Server cascade paths.

DatabaseSetup.md supersedes older instructions about starting in master and automatic database
creation.


## 2.0.3 legacy compatibility (supersedes earlier five-status-only descriptions)

The in-place upgrade supports the root TableCreation.sql schema. TypeName uses
nvarchar(100), ClaimantName nvarchar(255), and AuditLog.TableName nvarchar(128).
AuditID is widened to bigint while preserving identity values. AuditLog.ChangeDate
is nullable datetime2(7); unknown original dates remain NULL. Existing local clock
values are retained without assuming a timezone. Future timestamp defaults use UTC.

Valid stored statuses include Pending Review and Active Investigation. Baseline
history permits NULL -> either legacy label. From either legacy label an explicit
admin transition to Under Review, Escalated, Resolved or Closed is allowed. The
modern transition rules are otherwise unchanged; new claims start New.

No existing filing-type name or ID is remapped. Old and new categories can coexist.
The script does not treat an old category as an ML ground-truth label automatically.
