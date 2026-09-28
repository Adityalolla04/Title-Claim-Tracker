/* Title Claim Intelligence 2.0.3 - standalone SSMS deployment.
   Select the destination database in SSMS; set @ExpectedDatabase below to
   that exact name. No USE, CREATE DATABASE, SQLCMD variables, or GO required.
   Run the entire file. Stop the application first; back up existing data.
   See DatabaseSetup.md. Do not include as a SQL database-project Build item.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @ExpectedDatabase sysname = N'TitleClaimTracker'; -- existing database; change only if needed
DECLARE @Major int = TRY_CONVERT(int, SERVERPROPERTY(N'ProductMajorVersion'));
DECLARE @Build int = TRY_CONVERT(int, PARSENAME(CONVERT(nvarchar(128), SERVERPROPERTY(N'ProductVersion')), 2));
IF DB_NAME() <> @ExpectedDatabase OR DB_ID() <= 4
BEGIN
    ;THROW 51000, N'Select the intended user database in SSMS and set @ExpectedDatabase to the same name. No changes made.', 1;
END;
IF @Major IS NULL OR @Major < 13 OR (@Major = 13 AND (@Build IS NULL OR @Build < 4001))
BEGIN
    ;THROW 51001, N'SQL Server 2016 SP1 (13.0.4001) or later is required.', 1;
END;
IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND compatibility_level < 130)
BEGIN
    ;THROW 51002, N'Database compatibility level must be 130 or later. Review it before changing it.', 1;
END;
IF @@TRANCOUNT <> 0
BEGIN
    ;THROW 51035, N'Run in a new SSMS query without an existing transaction.', 1;
END;

CREATE TABLE #BeforeCounts (TableName sysname PRIMARY KEY, RowCountBefore bigint NOT NULL);
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @LockResult int;
    EXEC @LockResult = sys.sp_getapplock @Resource=N'TitleClaimIntelligence.SchemaUpgrade', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=0;
    IF @LockResult < 0
    BEGIN
        ;THROW 51036, N'Another schema upgrade is running. Try again after it completes.', 1;
    END;


    -- Phase 1: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 1. Version tracking. */
	IF OBJECT_ID(N''dbo.SchemaVersions'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.SchemaVersions
		(
			SchemaVersionID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SchemaVersions PRIMARY KEY,
			Version nvarchar(32) NOT NULL CONSTRAINT UQ_SchemaVersions_Version UNIQUE,
			AppliedUtc datetime2(7) NOT NULL CONSTRAINT DF_SchemaVersions_AppliedUtc DEFAULT SYSUTCDATETIME(),
			AppliedBy nvarchar(128) NOT NULL CONSTRAINT DF_SchemaVersions_AppliedBy DEFAULT SUSER_SNAME(),
			ScriptName nvarchar(260) NOT NULL,
			Description nvarchar(500) NOT NULL
		);
	END;
	IF COL_LENGTH(N''dbo.SchemaVersions'', N''Version'') IS NULL
	BEGIN
		;THROW 51003, N''dbo.SchemaVersions exists with an incompatible shape: Version is missing.'', 1;
	END;';

    -- Phase 2: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 2. Preserve/create the four legacy application tables individually. */
	IF OBJECT_ID(N''dbo.Properties'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.Properties
		(
			PropertyID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Properties PRIMARY KEY,
			Address nvarchar(255) NOT NULL,
			City nvarchar(100) NOT NULL,
			State char(2) NOT NULL CONSTRAINT CK_Properties_State CHECK (State LIKE ''[A-Z][A-Z]''),
			LegalDescription nvarchar(max) NULL,
			CreatedDate datetime2(7) NOT NULL CONSTRAINT DF_Properties_CreatedDate DEFAULT SYSUTCDATETIME()
		);
	END;

	IF OBJECT_ID(N''dbo.FilingTypes'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.FilingTypes
		(
			FilingTypeID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FilingTypes PRIMARY KEY,
			TypeName nvarchar(100) NOT NULL CONSTRAINT UQ_FilingTypes_TypeName UNIQUE
		);
	END;

	IF OBJECT_ID(N''dbo.LegalFilings'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.LegalFilings
		(
			FilingID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LegalFilings PRIMARY KEY,
			PropertyID int NOT NULL,
			FilingTypeID int NOT NULL,
			DateFiled date NOT NULL,
			Status nvarchar(50) NOT NULL CONSTRAINT DF_LegalFilings_Status DEFAULT N''New'',
			ClaimantName nvarchar(255) NULL,
			Notes nvarchar(max) NULL,
			RiskScore float NULL,
			CONSTRAINT FK_LegalFilings_Properties FOREIGN KEY (PropertyID) REFERENCES dbo.Properties(PropertyID),
			CONSTRAINT FK_LegalFilings_FilingTypes FOREIGN KEY (FilingTypeID) REFERENCES dbo.FilingTypes(FilingTypeID),
			CONSTRAINT CK_LegalFilings_Status CHECK (Status IN (N''New'', N''Under Review'', N''Escalated'', N''Resolved'', N''Closed'', N''Pending Review'', N''Active Investigation'')),
			CONSTRAINT CK_LegalFilings_RiskScore CHECK (RiskScore IS NULL OR (RiskScore BETWEEN 0 AND 1))
		);
	END;

	IF OBJECT_ID(N''dbo.AuditLog'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.AuditLog
		(
			AuditID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
			TableName nvarchar(128) NOT NULL,
			RecordID int NOT NULL,
			ColumnChanged nvarchar(100) NULL,
			OldValue nvarchar(max) NULL,
			NewValue nvarchar(max) NULL,
			ChangeDate datetime2(7) NULL CONSTRAINT DF_AuditLog_ChangeDate DEFAULT SYSUTCDATETIME()
		);
	END;';

    -- Phase 3: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 2.0.3 lossless legacy structural normalization.
   Preserve account/claim/category IDs, status strings, names and dates.
   All operations share the outer transaction and roll back on incompatibility.
*/

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N''dbo.FilingTypes'') AND name=N''TypeName'' AND system_type_id=231 AND max_length IN (100,200))
BEGIN
    ;THROW 51044, N''FilingTypes.TypeName: unexpected type/width. Inspect the column; no text was truncated.'', 1;
END;
IF COL_LENGTH(N''dbo.FilingTypes'',N''TypeName'') < 200
    ALTER TABLE dbo.FilingTypes ALTER COLUMN TypeName nvarchar(100) NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N''dbo.LegalFilings'') AND name=N''ClaimantName'' AND system_type_id=231 AND max_length IN (400,510))
BEGIN
    ;THROW 51044, N''LegalFilings.ClaimantName: unexpected type/width. Inspect the column; no text was truncated.'', 1;
END;
IF COL_LENGTH(N''dbo.LegalFilings'',N''ClaimantName'') < 510
    ALTER TABLE dbo.LegalFilings ALTER COLUMN ClaimantName nvarchar(255) NULL;

IF NOT EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N''dbo.AuditLog'') AND name=N''TableName'' AND system_type_id=231 AND max_length IN (200,256))
BEGIN
    ;THROW 51044, N''AuditLog.TableName: unexpected type/width. Inspect the column; no text was truncated.'', 1;
END;
IF COL_LENGTH(N''dbo.AuditLog'',N''TableName'') < 256
    ALTER TABLE dbo.AuditLog ALTER COLUMN TableName nvarchar(128) NOT NULL;

/* AuditID int -> bigint preserves identity values and existing audit rows.
   Refuse unknown dependent indexes/foreign keys rather than dropping them. */
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N''dbo.AuditLog'') AND name=N''AuditID'' AND system_type_id=56)
BEGIN
    IF EXISTS(SELECT 1 FROM sys.foreign_key_columns WHERE referenced_object_id=OBJECT_ID(N''dbo.AuditLog'') AND referenced_column_id=COLUMNPROPERTY(OBJECT_ID(N''dbo.AuditLog''),N''AuditID'',''ColumnId''))
       OR EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id
                 WHERE ic.object_id=OBJECT_ID(N''dbo.AuditLog'') AND ic.column_id=COLUMNPROPERTY(OBJECT_ID(N''dbo.AuditLog''),N''AuditID'',''ColumnId'') AND i.is_primary_key=0)
    BEGIN
        ;THROW 51045, N''AuditID has custom dependent indexes/foreign keys. A specific migration is required; none were dropped.'', 1;
    END;
    DECLARE @AuditPk sysname, @AuditPkType int, @AuditDdl nvarchar(max);
    SELECT @AuditPk=i.name, @AuditPkType=i.type FROM sys.indexes i WHERE i.object_id=OBJECT_ID(N''dbo.AuditLog'') AND i.is_primary_key=1;
    IF @AuditPk IS NULL OR (SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id=ic.object_id AND i.index_id=ic.index_id WHERE i.object_id=OBJECT_ID(N''dbo.AuditLog'') AND i.is_primary_key=1 AND ic.key_ordinal>0) <> 1
    BEGIN
        ;THROW 51046, N''AuditLog must have a single-column AuditID primary key for automatic widening.'', 1;
    END;
    SET @AuditDdl=N''ALTER TABLE dbo.AuditLog DROP CONSTRAINT ''+QUOTENAME(@AuditPk)+N'';'';
    EXEC sys.sp_executesql @AuditDdl;
    ALTER TABLE dbo.AuditLog ALTER COLUMN AuditID bigint NOT NULL;
    SET @AuditDdl=N''ALTER TABLE dbo.AuditLog ADD CONSTRAINT ''+QUOTENAME(@AuditPk)+N'' PRIMARY KEY ''+CASE WHEN @AuditPkType=1 THEN N''CLUSTERED'' ELSE N''NONCLUSTERED'' END+N'' (AuditID);'';
    EXEC sys.sp_executesql @AuditDdl;
END;

/* Remove both date defaults BEFORE changing either column type.
   Legacy SQL Server-generated names are discovered from metadata, never guessed.
   Drop, alter, and recreation all share the outer schema transaction. */
DECLARE @Defaults nvarchar(max)=N'''';
SELECT @Defaults=@Defaults+N''ALTER TABLE dbo.''+QUOTENAME(t.name)+N'' DROP CONSTRAINT ''+QUOTENAME(d.name)+N'';''
FROM sys.default_constraints d JOIN sys.tables t ON t.object_id=d.parent_object_id
JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=d.parent_column_id
WHERE t.schema_id=SCHEMA_ID(N''dbo'') AND ((t.name=N''Properties'' AND c.name=N''CreatedDate'') OR (t.name=N''AuditLog'' AND c.name=N''ChangeDate''));
IF @Defaults<>N'''' EXEC sys.sp_executesql @Defaults;

/* Compile each ALTER only after the dependent defaults have been removed.
   Retain original clock values and nullable legacy audit timestamps. */
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N''dbo.Properties'') AND name=N''CreatedDate'' AND system_type_id=61)
    EXEC sys.sp_executesql N''ALTER TABLE dbo.Properties ALTER COLUMN CreatedDate datetime2(7) NOT NULL;'';
IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N''dbo.AuditLog'') AND name=N''ChangeDate'' AND (system_type_id=61 OR (system_type_id=42 AND is_nullable=0)))
    EXEC sys.sp_executesql N''ALTER TABLE dbo.AuditLog ALTER COLUMN ChangeDate datetime2(7) NULL;'';

/* UTC defaults affect FUTURE inserts only; no historical values are rewritten. */
EXEC sys.sp_executesql N''ALTER TABLE dbo.Properties ADD CONSTRAINT DF_Properties_CreatedDate DEFAULT SYSUTCDATETIME() FOR CreatedDate;'';
EXEC sys.sp_executesql N''ALTER TABLE dbo.AuditLog ADD CONSTRAINT DF_AuditLog_ChangeDate DEFAULT SYSUTCDATETIME() FOR ChangeDate;'';

/* The original status CHECK can have a generated name. Replace only checks
   depending solely on Status. Do not drop unrelated/multi-column protections. */
DECLARE @StatusChecks nvarchar(max)=N'''';
SELECT @StatusChecks=@StatusChecks+N''ALTER TABLE dbo.LegalFilings DROP CONSTRAINT ''+QUOTENAME(cc.name)+N'';''
FROM sys.check_constraints cc
WHERE cc.parent_object_id=OBJECT_ID(N''dbo.LegalFilings'') AND (
    cc.parent_column_id=COLUMNPROPERTY(OBJECT_ID(N''dbo.LegalFilings''),N''Status'',''ColumnId'') OR (
      EXISTS(SELECT 1 FROM sys.sql_expression_dependencies dep WHERE dep.referencing_id=cc.object_id AND dep.referenced_id=cc.parent_object_id AND dep.referenced_minor_id=COLUMNPROPERTY(OBJECT_ID(N''dbo.LegalFilings''),N''Status'',''ColumnId''))
      AND NOT EXISTS(SELECT 1 FROM sys.sql_expression_dependencies dep WHERE dep.referencing_id=cc.object_id AND dep.referenced_id=cc.parent_object_id AND dep.referenced_minor_id>0 AND dep.referenced_minor_id<>COLUMNPROPERTY(OBJECT_ID(N''dbo.LegalFilings''),N''Status'',''ColumnId''))));
IF @StatusChecks<>N'''' EXEC sys.sp_executesql @StatusChecks;
';

    -- Phase 4: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* Existing legacy objects are never silently treated as compatible. */
	IF COL_LENGTH(N''dbo.Properties'', N''PropertyID'') IS NULL OR ISNULL(COL_LENGTH(N''dbo.Properties'', N''Address''), -999) <> 510 OR ISNULL(COL_LENGTH(N''dbo.Properties'', N''City''), -999) <> 200 OR ISNULL(COL_LENGTH(N''dbo.Properties'', N''State''), -999) <> 2
	BEGIN
		;THROW 51004, N''dbo.Properties exists with an incompatible legacy shape.'', 1;
	END;
	IF COL_LENGTH(N''dbo.FilingTypes'', N''FilingTypeID'') IS NULL OR ISNULL(COL_LENGTH(N''dbo.FilingTypes'', N''TypeName''), -999) <> 200
	BEGIN
		;THROW 51005, N''dbo.FilingTypes exists with an incompatible legacy shape.'', 1;
	END;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''FilingID'') IS NULL OR ISNULL(COL_LENGTH(N''dbo.LegalFilings'', N''PropertyID''), -999) <> 4 OR ISNULL(COL_LENGTH(N''dbo.LegalFilings'', N''FilingTypeID''), -999) <> 4 OR ISNULL(COL_LENGTH(N''dbo.LegalFilings'', N''DateFiled''), -999) <> 3 OR ISNULL(COL_LENGTH(N''dbo.LegalFilings'', N''Status''), -999) <> 100
	BEGIN
		;THROW 51006, N''dbo.LegalFilings exists with an incompatible legacy shape.'', 1;
	END;
	IF COL_LENGTH(N''dbo.AuditLog'', N''AuditID'') IS NULL OR EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N''dbo.AuditLog'') AND name = N''AuditID'' AND TYPE_NAME(user_type_id) <> N''bigint'')
	BEGIN
		;THROW 51007, N''dbo.AuditLog.AuditID must be bigint; no destructive conversion was attempted.'', 1;
	END;
	IF EXISTS (SELECT 1 FROM dbo.LegalFilings WHERE Status NOT IN (N''New'', N''Under Review'', N''Escalated'', N''Resolved'', N''Closed'', N''Pending Review'', N''Active Investigation''))
	BEGIN
		;THROW 51008, N''dbo.LegalFilings contains a status outside the documented status set.'', 1;
	END;
    INSERT #BeforeCounts VALUES
      (N''Properties'', (SELECT COUNT_BIG(*) FROM dbo.Properties)),
      (N''FilingTypes'', (SELECT COUNT_BIG(*) FROM dbo.FilingTypes)),
      (N''LegalFilings'', (SELECT COUNT_BIG(*) FROM dbo.LegalFilings)),
      (N''AuditLog'', (SELECT COUNT_BIG(*) FROM dbo.AuditLog));
';

    -- Phase 5: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 3. ASP.NET Core Identity 8-compatible tables. */
	IF OBJECT_ID(N''dbo.AspNetUsers'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.AspNetUsers
		(
			Id nvarchar(450) NOT NULL CONSTRAINT PK_AspNetUsers PRIMARY KEY,
			UserName nvarchar(256) NULL,
			NormalizedUserName nvarchar(256) NULL,
			Email nvarchar(256) NULL,
			NormalizedEmail nvarchar(256) NULL,
			EmailConfirmed bit NOT NULL,
			PasswordHash nvarchar(max) NULL,
			SecurityStamp nvarchar(max) NULL,
			ConcurrencyStamp nvarchar(max) NULL,
			PhoneNumber nvarchar(max) NULL,
			PhoneNumberConfirmed bit NOT NULL,
			TwoFactorEnabled bit NOT NULL,
			LockoutEnd datetimeoffset(7) NULL,
			LockoutEnabled bit NOT NULL,
			AccessFailedCount int NOT NULL,
			DisplayName nvarchar(200) NULL,
			CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_AspNetUsers_CreatedUtc DEFAULT SYSUTCDATETIME(),
			IsActive bit NOT NULL CONSTRAINT DF_AspNetUsers_IsActive DEFAULT 1
		);
		CREATE UNIQUE INDEX UserNameIndex ON dbo.AspNetUsers(NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
		CREATE INDEX EmailIndex ON dbo.AspNetUsers(NormalizedEmail);
	END;
	IF COL_LENGTH(N''dbo.AspNetUsers'', N''Id'') IS NULL OR COL_LENGTH(N''dbo.AspNetUsers'', N''NormalizedUserName'') IS NULL OR COL_LENGTH(N''dbo.AspNetUsers'', N''NormalizedEmail'') IS NULL
	BEGIN
		;THROW 51010, N''dbo.AspNetUsers exists with an incompatible ASP.NET Identity shape.'', 1;
	END;
	IF COL_LENGTH(N''dbo.AspNetUsers'', N''DisplayName'') IS NULL
		ALTER TABLE dbo.AspNetUsers ADD DisplayName nvarchar(200) NULL;
	IF COL_LENGTH(N''dbo.AspNetUsers'', N''CreatedUtc'') IS NULL
		ALTER TABLE dbo.AspNetUsers ADD CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_AspNetUsers_CreatedUtc DEFAULT SYSUTCDATETIME() WITH VALUES;
	IF COL_LENGTH(N''dbo.AspNetUsers'', N''IsActive'') IS NULL
		ALTER TABLE dbo.AspNetUsers ADD IsActive bit NOT NULL CONSTRAINT DF_AspNetUsers_IsActive DEFAULT 1 WITH VALUES;

	IF OBJECT_ID(N''dbo.AspNetRoles'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.AspNetRoles
		(
			Id nvarchar(450) NOT NULL CONSTRAINT PK_AspNetRoles PRIMARY KEY,
			Name nvarchar(256) NULL,
			NormalizedName nvarchar(256) NULL,
			ConcurrencyStamp nvarchar(max) NULL
		);
		CREATE UNIQUE INDEX RoleNameIndex ON dbo.AspNetRoles(NormalizedName) WHERE NormalizedName IS NOT NULL;
	END;
	IF COL_LENGTH(N''dbo.AspNetRoles'', N''Id'') IS NULL OR COL_LENGTH(N''dbo.AspNetRoles'', N''NormalizedName'') IS NULL
		BEGIN
		    ;THROW 51011, N''dbo.AspNetRoles exists with an incompatible ASP.NET Identity shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.AspNetUserClaims'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.AspNetUserClaims
		(
			Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetUserClaims PRIMARY KEY,
			UserId nvarchar(450) NOT NULL,
			ClaimType nvarchar(max) NULL,
			ClaimValue nvarchar(max) NULL,
			CONSTRAINT FK_AspNetUserClaims_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
		);
		CREATE INDEX IX_AspNetUserClaims_UserId ON dbo.AspNetUserClaims(UserId);
	END;
	IF COL_LENGTH(N''dbo.AspNetUserClaims'', N''Id'') IS NULL OR COL_LENGTH(N''dbo.AspNetUserClaims'', N''UserId'') IS NULL
		BEGIN
		    ;THROW 51012, N''dbo.AspNetUserClaims exists with an incompatible ASP.NET Identity shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.AspNetUserLogins'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.AspNetUserLogins
		(
			LoginProvider nvarchar(450) NOT NULL,
			ProviderKey nvarchar(450) NOT NULL,
			ProviderDisplayName nvarchar(max) NULL,
			UserId nvarchar(450) NOT NULL,
			CONSTRAINT PK_AspNetUserLogins PRIMARY KEY NONCLUSTERED (LoginProvider, ProviderKey),
			CONSTRAINT FK_AspNetUserLogins_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
		);
		CREATE INDEX IX_AspNetUserLogins_UserId ON dbo.AspNetUserLogins(UserId);
	END;
	IF COL_LENGTH(N''dbo.AspNetUserLogins'', N''LoginProvider'') IS NULL OR COL_LENGTH(N''dbo.AspNetUserLogins'', N''ProviderKey'') IS NULL OR COL_LENGTH(N''dbo.AspNetUserLogins'', N''UserId'') IS NULL
		BEGIN
		    ;THROW 51013, N''dbo.AspNetUserLogins exists with an incompatible ASP.NET Identity shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.AspNetUserRoles'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.AspNetUserRoles
		(
			UserId nvarchar(450) NOT NULL,
			RoleId nvarchar(450) NOT NULL,
			CONSTRAINT PK_AspNetUserRoles PRIMARY KEY NONCLUSTERED (UserId, RoleId),
			CONSTRAINT FK_AspNetUserRoles_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE,
			CONSTRAINT FK_AspNetUserRoles_AspNetRoles_RoleId FOREIGN KEY (RoleId) REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE
		);
		CREATE INDEX IX_AspNetUserRoles_RoleId ON dbo.AspNetUserRoles(RoleId);
	END;
	IF COL_LENGTH(N''dbo.AspNetUserRoles'', N''UserId'') IS NULL OR COL_LENGTH(N''dbo.AspNetUserRoles'', N''RoleId'') IS NULL
		BEGIN
		    ;THROW 51014, N''dbo.AspNetUserRoles exists with an incompatible ASP.NET Identity shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.AspNetUserTokens'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.AspNetUserTokens
		(
			UserId nvarchar(450) NOT NULL,
			LoginProvider nvarchar(450) NOT NULL,
			Name nvarchar(450) NOT NULL,
			Value nvarchar(max) NULL,
			CONSTRAINT PK_AspNetUserTokens PRIMARY KEY NONCLUSTERED (UserId, LoginProvider, Name),
			CONSTRAINT FK_AspNetUserTokens_AspNetUsers_UserId FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE
		);
	END;
	IF COL_LENGTH(N''dbo.AspNetUserTokens'', N''UserId'') IS NULL OR COL_LENGTH(N''dbo.AspNetUserTokens'', N''LoginProvider'') IS NULL OR COL_LENGTH(N''dbo.AspNetUserTokens'', N''Name'') IS NULL
		BEGIN
		    ;THROW 51015, N''dbo.AspNetUserTokens exists with an incompatible ASP.NET Identity shape.'', 1;
		END;
IF OBJECT_ID(N''dbo.AspNetRoleClaims'', N''U'') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetRoleClaims
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AspNetRoleClaims PRIMARY KEY,
        RoleId nvarchar(450) NOT NULL,
        ClaimType nvarchar(max) NULL,
        ClaimValue nvarchar(max) NULL,
        CONSTRAINT FK_AspNetRoleClaims_AspNetRoles_RoleId FOREIGN KEY (RoleId)
            REFERENCES dbo.AspNetRoles(Id) ON DELETE CASCADE
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AspNetRoleClaims'') AND name = N''IX_AspNetRoleClaims_RoleId'')
    CREATE INDEX IX_AspNetRoleClaims_RoleId ON dbo.AspNetRoleClaims(RoleId);
';

    -- Phase 6: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 4. Model registry and application metadata tables. */
	IF OBJECT_ID(N''dbo.ModelVersions'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.ModelVersions
		(
			ModelVersionID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModelVersions PRIMARY KEY,
			ModelName nvarchar(100) NOT NULL,
			Version nvarchar(50) NOT NULL,
			ArtifactUri nvarchar(2048) NULL,
			ArtifactSha256 binary(32) NULL,
			DatasetProvenance nvarchar(max) NOT NULL,
			TrainedUtc datetime2(7) NULL,
			RegisteredUtc datetime2(7) NOT NULL CONSTRAINT DF_ModelVersions_RegisteredUtc DEFAULT SYSUTCDATETIME(),
			IsActive bit NOT NULL CONSTRAINT DF_ModelVersions_IsActive DEFAULT 0,
			CONSTRAINT UQ_ModelVersions_Name_Version UNIQUE (ModelName, Version)
		);
	END;
	IF COL_LENGTH(N''dbo.ModelVersions'', N''ModelVersionID'') IS NULL OR COL_LENGTH(N''dbo.ModelVersions'', N''ModelName'') IS NULL OR COL_LENGTH(N''dbo.ModelVersions'', N''Version'') IS NULL OR COL_LENGTH(N''dbo.ModelVersions'', N''DatasetProvenance'') IS NULL
		BEGIN
		    ;THROW 51016, N''dbo.ModelVersions exists with an incompatible shape.'', 1;
		END;';

    -- Phase 7: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* Add only fields that are absent; existing values are retained. */
	IF COL_LENGTH(N''dbo.LegalFilings'', N''RiskScore'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD RiskScore float NULL;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''SubmittedByUserId'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD SubmittedByUserId nvarchar(450) NULL;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''CreatedUtc'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_LegalFilings_CreatedUtc DEFAULT SYSUTCDATETIME() WITH VALUES;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''UpdatedUtc'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD UpdatedUtc datetime2(7) NOT NULL CONSTRAINT DF_LegalFilings_UpdatedUtc DEFAULT SYSUTCDATETIME() WITH VALUES;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''CreationSource'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD CreationSource nvarchar(20) NOT NULL CONSTRAINT DF_LegalFilings_CreationSource DEFAULT N''Manual'' WITH VALUES;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''IssueTags'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD IssueTags nvarchar(max) NULL;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''ClassificationScore'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD ClassificationScore decimal(9,8) NULL;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''ModelVersionID'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD ModelVersionID bigint NULL;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''TriagePriority'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD TriagePriority tinyint NOT NULL CONSTRAINT DF_LegalFilings_TriagePriority DEFAULT 3 WITH VALUES;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''TriageRuleVersion'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD TriageRuleVersion nvarchar(50) NOT NULL CONSTRAINT DF_LegalFilings_TriageRuleVersion DEFAULT N''triage-v1'' WITH VALUES;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''RowVersion'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD RowVersion rowversion NOT NULL;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''IsDeleted'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD IsDeleted bit NOT NULL CONSTRAINT DF_LegalFilings_IsDeleted DEFAULT 0 WITH VALUES;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''DeletedUtc'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD DeletedUtc datetime2(7) NULL;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''DeletedByUserId'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD DeletedByUserId nvarchar(450) NULL;
	IF COL_LENGTH(N''dbo.LegalFilings'', N''IsSyntheticDemoData'') IS NULL
		ALTER TABLE dbo.LegalFilings ADD IsSyntheticDemoData bit NOT NULL CONSTRAINT DF_LegalFilings_IsSyntheticDemoData DEFAULT 0 WITH VALUES;


	IF COL_LENGTH(N''dbo.AuditLog'', N''ActorUserId'') IS NULL
		ALTER TABLE dbo.AuditLog ADD ActorUserId nvarchar(450) NULL;
	IF COL_LENGTH(N''dbo.AuditLog'', N''Action'') IS NULL
		ALTER TABLE dbo.AuditLog ADD Action nvarchar(30) NOT NULL CONSTRAINT DF_AuditLog_Action DEFAULT N''Update'' WITH VALUES;';

    -- Phase 8: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'IF EXISTS (SELECT 1 FROM dbo.LegalFilings WHERE RiskScore IS NOT NULL AND (RiskScore < 0 OR RiskScore > 1))
		BEGIN
		    ;THROW 51009, N''dbo.LegalFilings contains an out-of-range RiskScore; data was not changed.'', 1;
		END;

	IF EXISTS (SELECT 1 FROM dbo.LegalFilings WHERE ClassificationScore IS NOT NULL AND (ClassificationScore < 0 OR ClassificationScore > 1))
		BEGIN
		    ;THROW 51017, N''dbo.LegalFilings contains an out-of-range ClassificationScore.'', 1;
		END;
	IF EXISTS (SELECT 1 FROM dbo.LegalFilings WHERE CreationSource NOT IN (N''Manual'', N''Assisted''))
		BEGIN
		    ;THROW 51018, N''dbo.LegalFilings contains an unsupported CreationSource.'', 1;
		END;
	IF EXISTS (SELECT 1 FROM dbo.LegalFilings WHERE TriagePriority NOT BETWEEN 1 AND 5)
		BEGIN
		    ;THROW 51019, N''dbo.LegalFilings contains an unsupported TriagePriority.'', 1;
		END;
	IF EXISTS (SELECT 1 FROM dbo.LegalFilings WHERE IssueTags IS NOT NULL AND ISJSON(IssueTags) <> 1)
		BEGIN
		    ;THROW 51020, N''dbo.LegalFilings contains IssueTags that are not valid JSON.'', 1;
		END;

	IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N''CK_LegalFilings_Status'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT CK_LegalFilings_Status CHECK (Status IN (N''New'', N''Under Review'', N''Escalated'', N''Resolved'', N''Closed'', N''Pending Review'', N''Active Investigation''));
	IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N''CK_LegalFilings_RiskScore'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT CK_LegalFilings_RiskScore CHECK (RiskScore IS NULL OR (RiskScore BETWEEN 0 AND 1));
	IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N''CK_LegalFilings_ClassificationScore'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT CK_LegalFilings_ClassificationScore CHECK (ClassificationScore IS NULL OR (ClassificationScore BETWEEN 0 AND 1));
	IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N''CK_LegalFilings_CreationSource'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT CK_LegalFilings_CreationSource CHECK (CreationSource IN (N''Manual'', N''Assisted''));
	IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N''CK_LegalFilings_TriagePriority'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT CK_LegalFilings_TriagePriority CHECK (TriagePriority BETWEEN 1 AND 5);
	IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N''CK_LegalFilings_SoftDelete'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT CK_LegalFilings_SoftDelete CHECK ((IsDeleted = 0 AND DeletedUtc IS NULL AND DeletedByUserId IS NULL) OR IsDeleted = 1);
	IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N''CK_LegalFilings_IssueTagsJson'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT CK_LegalFilings_IssueTagsJson CHECK (IssueTags IS NULL OR ISJSON(IssueTags) = 1);
	IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N''CK_AuditLog_Action'' AND parent_object_id = OBJECT_ID(N''dbo.AuditLog''))
		ALTER TABLE dbo.AuditLog ADD CONSTRAINT CK_AuditLog_Action CHECK (Action IN (N''Insert'', N''Update'', N''SoftDelete'', N''StatusTransition''));';

    -- Phase 9: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 5. Status history, triage, feedback, evaluation, and idempotency. */
	IF OBJECT_ID(N''dbo.ClaimStatusHistory'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.ClaimStatusHistory
		(
			StatusHistoryID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClaimStatusHistory PRIMARY KEY,
			FilingID int NOT NULL,
			FromStatus nvarchar(50) NULL,
			ToStatus nvarchar(50) NOT NULL,
			ActorUserId nvarchar(450) NULL,
			TransitionedUtc datetime2(7) NOT NULL CONSTRAINT DF_ClaimStatusHistory_TransitionedUtc DEFAULT SYSUTCDATETIME(),
			Reason nvarchar(500) NULL,
			CONSTRAINT CK_ClaimStatusHistory_Transition CHECK
			(
				(FromStatus IS NULL AND ToStatus IN (N''New'', N''Under Review'', N''Escalated'', N''Resolved'', N''Closed'', N''Pending Review'', N''Active Investigation'')) OR
				(FromStatus = N''New'' AND ToStatus IN (N''Under Review'', N''Escalated'', N''Closed'')) OR
				(FromStatus = N''Under Review'' AND ToStatus IN (N''Escalated'', N''Resolved'', N''Closed'')) OR
				(FromStatus = N''Escalated'' AND ToStatus IN (N''Under Review'', N''Resolved'', N''Closed'')) OR
				(FromStatus = N''Resolved'' AND ToStatus = N''Closed'') OR
                (FromStatus IN (N''Pending Review'', N''Active Investigation'') AND ToStatus IN (N''Under Review'', N''Escalated'', N''Resolved'', N''Closed''))
			)
		);
	END;
	IF COL_LENGTH(N''dbo.ClaimStatusHistory'', N''StatusHistoryID'') IS NULL OR COL_LENGTH(N''dbo.ClaimStatusHistory'', N''FilingID'') IS NULL OR COL_LENGTH(N''dbo.ClaimStatusHistory'', N''ToStatus'') IS NULL
		BEGIN
		    ;THROW 51021, N''dbo.ClaimStatusHistory exists with an incompatible shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.TriageAttempts'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.TriageAttempts
		(
			TriageAttemptID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TriageAttempts PRIMARY KEY,
			SubmittedByUserId nvarchar(450) NOT NULL,
			InputText nvarchar(max) NOT NULL,
			PredictedFilingTypeID int NULL,
			ClassificationScore decimal(9,8) NULL,
			HeuristicPriority tinyint NULL,
			TriageRuleVersion nvarchar(50) NOT NULL,
			ModelVersionID bigint NULL,
			CreatedFilingID int NULL,
			Outcome nvarchar(30) NOT NULL CONSTRAINT DF_TriageAttempts_Outcome DEFAULT N''AwaitingConfirmation'',
			CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_TriageAttempts_CreatedUtc DEFAULT SYSUTCDATETIME(),
			CompletedUtc datetime2(7) NULL,
			ExtractedClaimantName nvarchar(200) NULL,
			ExtractedAddress nvarchar(255) NULL,
			ExtractedCity nvarchar(100) NULL,
			ExtractedState char(2) NULL,
			ExtractedNotes nvarchar(max) NULL,
			FailureCode nvarchar(100) NULL,
			FailureMessage nvarchar(max) NULL,
			CONSTRAINT CK_TriageAttempts_ClassificationScore CHECK (ClassificationScore IS NULL OR (ClassificationScore BETWEEN 0 AND 1)),
			CONSTRAINT CK_TriageAttempts_HeuristicPriority CHECK (HeuristicPriority IS NULL OR HeuristicPriority BETWEEN 1 AND 5),
			CONSTRAINT CK_TriageAttempts_Outcome CHECK (Outcome IN (N''AwaitingConfirmation'', N''Created'', N''Rejected'', N''Failed''))
		);
	END;
	IF COL_LENGTH(N''dbo.TriageAttempts'', N''TriageAttemptID'') IS NULL OR COL_LENGTH(N''dbo.TriageAttempts'', N''SubmittedByUserId'') IS NULL OR COL_LENGTH(N''dbo.TriageAttempts'', N''InputText'') IS NULL OR COL_LENGTH(N''dbo.TriageAttempts'', N''Outcome'') IS NULL
		BEGIN
		    ;THROW 51022, N''dbo.TriageAttempts exists with an incompatible shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.TriageFeedback'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.TriageFeedback
		(
			TriageFeedbackID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TriageFeedback PRIMARY KEY,
			TriageAttemptID bigint NOT NULL,
			ReviewedByUserId nvarchar(450) NOT NULL,
			WasCorrect bit NOT NULL,
			CorrectedFilingTypeID int NULL,
			CorrectedIssueTags nvarchar(max) NULL,
			CorrectionNotes nvarchar(max) NULL,
			ReviewedUtc datetime2(7) NOT NULL CONSTRAINT DF_TriageFeedback_ReviewedUtc DEFAULT SYSUTCDATETIME(),
			CONSTRAINT CK_TriageFeedback_Correction CHECK (WasCorrect = 1 OR CorrectedFilingTypeID IS NOT NULL)
		);
		CREATE UNIQUE INDEX UQ_TriageFeedback_TriageAttempt ON dbo.TriageFeedback(TriageAttemptID);
	END;
	IF COL_LENGTH(N''dbo.TriageFeedback'', N''TriageFeedbackID'') IS NULL OR COL_LENGTH(N''dbo.TriageFeedback'', N''TriageAttemptID'') IS NULL OR COL_LENGTH(N''dbo.TriageFeedback'', N''ReviewedByUserId'') IS NULL
		BEGIN
		    ;THROW 51023, N''dbo.TriageFeedback exists with an incompatible shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.ModelEvaluationResults'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.ModelEvaluationResults
		(
			ModelEvaluationResultID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModelEvaluationResults PRIMARY KEY,
			ModelVersionID bigint NOT NULL,
			DatasetName nvarchar(200) NOT NULL,
			DatasetVersion nvarchar(100) NOT NULL,
			DatasetProvenance nvarchar(max) NOT NULL,
			EvaluatedUtc datetime2(7) NOT NULL,
			SampleCount bigint NOT NULL,
			Accuracy decimal(9,8) NOT NULL,
			MacroF1 decimal(9,8) NOT NULL,
			WeightedF1 decimal(9,8) NOT NULL,
			PrecisionScore decimal(9,8) NULL,
			RecallScore decimal(9,8) NULL,
			Notes nvarchar(max) NULL,
			CONSTRAINT CK_ModelEvaluationResults_SampleCount CHECK (SampleCount >= 0),
			CONSTRAINT CK_ModelEvaluationResults_Scores CHECK (Accuracy BETWEEN 0 AND 1 AND MacroF1 BETWEEN 0 AND 1 AND WeightedF1 BETWEEN 0 AND 1 AND (PrecisionScore IS NULL OR PrecisionScore BETWEEN 0 AND 1) AND (RecallScore IS NULL OR RecallScore BETWEEN 0 AND 1)),
			CONSTRAINT UQ_ModelEvaluationResults_Model_Dataset UNIQUE (ModelVersionID, DatasetName, DatasetVersion)
		);
	END;
	IF COL_LENGTH(N''dbo.ModelEvaluationResults'', N''ModelEvaluationResultID'') IS NULL OR COL_LENGTH(N''dbo.ModelEvaluationResults'', N''ModelVersionID'') IS NULL OR COL_LENGTH(N''dbo.ModelEvaluationResults'', N''DatasetProvenance'') IS NULL
		BEGIN
		    ;THROW 51024, N''dbo.ModelEvaluationResults exists with an incompatible shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.IdempotencyRecords'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.IdempotencyRecords
		(
			IdempotencyRecordID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_IdempotencyRecords PRIMARY KEY,
			UserId nvarchar(450) NOT NULL,
			OperationName nvarchar(100) NOT NULL,
			IdempotencyKey nvarchar(200) NOT NULL,
			PayloadHash varbinary(32) NOT NULL,
			State nvarchar(20) NOT NULL CONSTRAINT DF_IdempotencyRecords_State DEFAULT N''Started'',
			ResponseStatusCode int NULL,
			ResponseBody nvarchar(max) NULL,
			CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_IdempotencyRecords_CreatedUtc DEFAULT SYSUTCDATETIME(),
			CompletedUtc datetime2(7) NULL,
			CONSTRAINT CK_IdempotencyRecords_State CHECK (State IN (N''Started'', N''Completed'', N''Failed'')),
			CONSTRAINT CK_IdempotencyRecords_PayloadHash CHECK (DATALENGTH(PayloadHash) = 32),
			CONSTRAINT UQ_IdempotencyRecords_User_Operation_Key UNIQUE (UserId, OperationName, IdempotencyKey)
		);
	END;
	IF COL_LENGTH(N''dbo.IdempotencyRecords'', N''IdempotencyRecordID'') IS NULL OR COL_LENGTH(N''dbo.IdempotencyRecords'', N''UserId'') IS NULL OR COL_LENGTH(N''dbo.IdempotencyRecords'', N''OperationName'') IS NULL OR COL_LENGTH(N''dbo.IdempotencyRecords'', N''IdempotencyKey'') IS NULL OR ISNULL(COL_LENGTH(N''dbo.IdempotencyRecords'', N''PayloadHash''), -999) <> 32
		BEGIN
		    ;THROW 51025, N''dbo.IdempotencyRecords exists with an incompatible shape.'', 1;
		END;';

    -- Phase 10: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N''dbo.ClaimStatusHistory'') AND name=N''CK_ClaimStatusHistory_Transition'')
    ALTER TABLE dbo.ClaimStatusHistory DROP CONSTRAINT CK_ClaimStatusHistory_Transition;
ALTER TABLE dbo.ClaimStatusHistory WITH CHECK ADD CONSTRAINT CK_ClaimStatusHistory_Transition CHECK 
(
				(FromStatus IS NULL AND ToStatus IN (N''New'', N''Under Review'', N''Escalated'', N''Resolved'', N''Closed'', N''Pending Review'', N''Active Investigation'')) OR
				(FromStatus = N''New'' AND ToStatus IN (N''Under Review'', N''Escalated'', N''Closed'')) OR
				(FromStatus = N''Under Review'' AND ToStatus IN (N''Escalated'', N''Resolved'', N''Closed'')) OR
				(FromStatus = N''Escalated'' AND ToStatus IN (N''Under Review'', N''Resolved'', N''Closed'')) OR
				(FromStatus = N''Resolved'' AND ToStatus = N''Closed'') OR
                (FromStatus IN (N''Pending Review'', N''Active Investigation'') AND ToStatus IN (N''Under Review'', N''Escalated'', N''Resolved'', N''Closed''))
			);';

    -- Phase 11: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 6. Isolated public-record staging/reference structures. */
	IF OBJECT_ID(N''dbo.DataSources'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.DataSources
		(
			DataSourceID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DataSources PRIMARY KEY,
			SourceName nvarchar(200) NOT NULL,
			SourceUrl nvarchar(2048) NOT NULL,
			ProvenanceStatement nvarchar(max) NOT NULL,
			IsActive bit NOT NULL CONSTRAINT DF_DataSources_IsActive DEFAULT 1,
			CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_DataSources_CreatedUtc DEFAULT SYSUTCDATETIME(),
			UpdatedUtc datetime2(7) NOT NULL CONSTRAINT DF_DataSources_UpdatedUtc DEFAULT SYSUTCDATETIME(),
			CONSTRAINT UQ_DataSources_SourceName UNIQUE (SourceName)
		);
	END;
	IF COL_LENGTH(N''dbo.DataSources'', N''DataSourceID'') IS NULL OR COL_LENGTH(N''dbo.DataSources'', N''SourceName'') IS NULL OR COL_LENGTH(N''dbo.DataSources'', N''SourceUrl'') IS NULL OR COL_LENGTH(N''dbo.DataSources'', N''ProvenanceStatement'') IS NULL
		BEGIN
		    ;THROW 51026, N''dbo.DataSources exists with an incompatible shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.IngestionRuns'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.IngestionRuns
		(
			IngestionRunID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_IngestionRuns PRIMARY KEY,
			DataSourceID int NOT NULL,
			Status nvarchar(20) NOT NULL CONSTRAINT DF_IngestionRuns_Status DEFAULT N''Pending'',
			StartedUtc datetime2(7) NULL,
			CompletedUtc datetime2(7) NULL,
			LastCheckpoint nvarchar(500) NULL,
			RowsRead bigint NOT NULL CONSTRAINT DF_IngestionRuns_RowsRead DEFAULT 0,
			RowsAccepted bigint NOT NULL CONSTRAINT DF_IngestionRuns_RowsAccepted DEFAULT 0,
			RowsUpdated bigint NOT NULL CONSTRAINT DF_IngestionRuns_RowsUpdated DEFAULT 0,
			RowsRejected bigint NOT NULL CONSTRAINT DF_IngestionRuns_RowsRejected DEFAULT 0,
			ErrorCount bigint NOT NULL CONSTRAINT DF_IngestionRuns_ErrorCount DEFAULT 0,
			CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_IngestionRuns_CreatedUtc DEFAULT SYSUTCDATETIME(),
			CONSTRAINT CK_IngestionRuns_Status CHECK (Status IN (N''Pending'', N''Running'', N''Completed'', N''Failed'', N''Cancelled'')),
			CONSTRAINT CK_IngestionRuns_Counts CHECK (RowsRead >= 0 AND RowsAccepted >= 0 AND RowsUpdated >= 0 AND RowsRejected >= 0 AND ErrorCount >= 0)
		);
	END;
	IF COL_LENGTH(N''dbo.IngestionRuns'', N''IngestionRunID'') IS NULL OR COL_LENGTH(N''dbo.IngestionRuns'', N''DataSourceID'') IS NULL OR COL_LENGTH(N''dbo.IngestionRuns'', N''Status'') IS NULL OR COL_LENGTH(N''dbo.IngestionRuns'', N''LastCheckpoint'') IS NULL
		BEGIN
		    ;THROW 51027, N''dbo.IngestionRuns exists with an incompatible shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.IngestionQuarantineErrors'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.IngestionQuarantineErrors
		(
			QuarantineErrorID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_IngestionQuarantineErrors PRIMARY KEY,
			IngestionRunID bigint NOT NULL,
			ExternalRecordKey nvarchar(500) NOT NULL,
			ErrorCode nvarchar(100) NOT NULL,
			ErrorMessage nvarchar(max) NOT NULL,
			RawPayload nvarchar(max) NULL,
			CreatedUtc datetime2(7) NOT NULL CONSTRAINT DF_IngestionQuarantineErrors_CreatedUtc DEFAULT SYSUTCDATETIME(),
			ResolvedUtc datetime2(7) NULL
		);
	END;
	IF COL_LENGTH(N''dbo.IngestionQuarantineErrors'', N''QuarantineErrorID'') IS NULL OR COL_LENGTH(N''dbo.IngestionQuarantineErrors'', N''IngestionRunID'') IS NULL OR COL_LENGTH(N''dbo.IngestionQuarantineErrors'', N''ExternalRecordKey'') IS NULL
		BEGIN
		    ;THROW 51028, N''dbo.IngestionQuarantineErrors exists with an incompatible shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.ExternalProperties'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.ExternalProperties
		(
			ExternalPropertyID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExternalProperties PRIMARY KEY,
			DataSourceID int NOT NULL,
			ExternalPropertyKey nvarchar(200) NOT NULL,
			ParcelIdentifier nvarchar(100) NULL,
			Address nvarchar(255) NULL,
			City nvarchar(100) NULL,
			State char(2) NULL,
			County nvarchar(100) NULL,
			RawPayload nvarchar(max) NULL,
			FirstSeenUtc datetime2(7) NOT NULL CONSTRAINT DF_ExternalProperties_FirstSeenUtc DEFAULT SYSUTCDATETIME(),
			LastSeenUtc datetime2(7) NOT NULL CONSTRAINT DF_ExternalProperties_LastSeenUtc DEFAULT SYSUTCDATETIME(),
			CONSTRAINT UQ_ExternalProperties_Source_Key UNIQUE (DataSourceID, ExternalPropertyKey)
		);
	END;
	IF COL_LENGTH(N''dbo.ExternalProperties'', N''ExternalPropertyID'') IS NULL OR COL_LENGTH(N''dbo.ExternalProperties'', N''DataSourceID'') IS NULL OR COL_LENGTH(N''dbo.ExternalProperties'', N''ExternalPropertyKey'') IS NULL
		BEGIN
		    ;THROW 51029, N''dbo.ExternalProperties exists with an incompatible shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.ExternalPropertyDocuments'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.ExternalPropertyDocuments
		(
			ExternalPropertyDocumentID bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExternalPropertyDocuments PRIMARY KEY,
			DataSourceID int NOT NULL,
			ExternalDocumentKey nvarchar(200) NOT NULL,
			SourceDocumentType nvarchar(100) NOT NULL,
			RecordedDate date NULL,
			DocumentUrl nvarchar(2048) NULL,
			RawPayload nvarchar(max) NULL,
			FirstSeenUtc datetime2(7) NOT NULL CONSTRAINT DF_ExternalPropertyDocuments_FirstSeenUtc DEFAULT SYSUTCDATETIME(),
			LastSeenUtc datetime2(7) NOT NULL CONSTRAINT DF_ExternalPropertyDocuments_LastSeenUtc DEFAULT SYSUTCDATETIME(),
			LastIngestionRunID bigint NULL,
			CONSTRAINT UQ_ExternalPropertyDocuments_Source_Key UNIQUE (DataSourceID, ExternalDocumentKey)
		);
	END;
	IF COL_LENGTH(N''dbo.ExternalPropertyDocuments'', N''ExternalPropertyDocumentID'') IS NULL OR COL_LENGTH(N''dbo.ExternalPropertyDocuments'', N''DataSourceID'') IS NULL OR COL_LENGTH(N''dbo.ExternalPropertyDocuments'', N''ExternalDocumentKey'') IS NULL OR COL_LENGTH(N''dbo.ExternalPropertyDocuments'', N''SourceDocumentType'') IS NULL
		BEGIN
		    ;THROW 51030, N''dbo.ExternalPropertyDocuments exists with an incompatible shape.'', 1;
		END;

	IF OBJECT_ID(N''dbo.ExternalDocumentProperties'', N''U'') IS NULL
	BEGIN
		CREATE TABLE dbo.ExternalDocumentProperties
		(
			ExternalPropertyDocumentID bigint NOT NULL,
			ExternalPropertyID bigint NOT NULL,
			RelationshipType nvarchar(100) NOT NULL,
			SourceRelationshipKey nvarchar(200) NULL,
			CONSTRAINT PK_ExternalDocumentProperties PRIMARY KEY (ExternalPropertyDocumentID, ExternalPropertyID)
		);
	END;
	IF COL_LENGTH(N''dbo.ExternalDocumentProperties'', N''ExternalPropertyDocumentID'') IS NULL OR COL_LENGTH(N''dbo.ExternalDocumentProperties'', N''ExternalPropertyID'') IS NULL OR COL_LENGTH(N''dbo.ExternalDocumentProperties'', N''RelationshipType'') IS NULL
		BEGIN
		    ;THROW 51031, N''dbo.ExternalDocumentProperties exists with an incompatible shape.'', 1;
		END;';

    -- Phase 12: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'
CREATE TABLE #ExpectedColumns (TableName sysname, ColumnName sysname, TypeName sysname, MaxLength smallint, IsNullable bit, PrecisionValue tinyint NULL, ScaleValue tinyint NULL);
INSERT #ExpectedColumns VALUES
(N''SchemaVersions'', N''SchemaVersionID'', N''int'', 4, 0, NULL, NULL),
(N''SchemaVersions'', N''Version'', N''nvarchar'', 64, 0, NULL, NULL),
(N''SchemaVersions'', N''AppliedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''SchemaVersions'', N''AppliedBy'', N''nvarchar'', 256, 0, NULL, NULL),
(N''SchemaVersions'', N''ScriptName'', N''nvarchar'', 520, 0, NULL, NULL),
(N''SchemaVersions'', N''Description'', N''nvarchar'', 1000, 0, NULL, NULL),
(N''Properties'', N''PropertyID'', N''int'', 4, 0, NULL, NULL),
(N''Properties'', N''Address'', N''nvarchar'', 510, 0, NULL, NULL),
(N''Properties'', N''City'', N''nvarchar'', 200, 0, NULL, NULL),
(N''Properties'', N''State'', N''char'', 2, 0, NULL, NULL),
(N''Properties'', N''LegalDescription'', N''nvarchar'', -1, 1, NULL, NULL),
(N''Properties'', N''CreatedDate'', N''datetime2'', 8, 0, NULL, NULL),
(N''FilingTypes'', N''FilingTypeID'', N''int'', 4, 0, NULL, NULL),
(N''FilingTypes'', N''TypeName'', N''nvarchar'', 200, 0, NULL, NULL),
(N''LegalFilings'', N''FilingID'', N''int'', 4, 0, NULL, NULL),
(N''LegalFilings'', N''PropertyID'', N''int'', 4, 0, NULL, NULL),
(N''LegalFilings'', N''FilingTypeID'', N''int'', 4, 0, NULL, NULL),
(N''LegalFilings'', N''DateFiled'', N''date'', 3, 0, NULL, NULL),
(N''LegalFilings'', N''Status'', N''nvarchar'', 100, 0, NULL, NULL),
(N''LegalFilings'', N''ClaimantName'', N''nvarchar'', 510, 1, NULL, NULL),
(N''LegalFilings'', N''Notes'', N''nvarchar'', -1, 1, NULL, NULL),
(N''LegalFilings'', N''RiskScore'', N''float'', 8, 1, NULL, NULL),
(N''AuditLog'', N''AuditID'', N''bigint'', 8, 0, NULL, NULL),
(N''AuditLog'', N''TableName'', N''nvarchar'', 256, 0, NULL, NULL),
(N''AuditLog'', N''RecordID'', N''int'', 4, 0, NULL, NULL),
(N''AuditLog'', N''ColumnChanged'', N''nvarchar'', 200, 1, NULL, NULL),
(N''AuditLog'', N''OldValue'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AuditLog'', N''NewValue'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AuditLog'', N''ChangeDate'', N''datetime2'', 8, 1, NULL, NULL),
(N''AspNetUsers'', N''Id'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetUsers'', N''UserName'', N''nvarchar'', 512, 1, NULL, NULL),
(N''AspNetUsers'', N''NormalizedUserName'', N''nvarchar'', 512, 1, NULL, NULL),
(N''AspNetUsers'', N''Email'', N''nvarchar'', 512, 1, NULL, NULL),
(N''AspNetUsers'', N''NormalizedEmail'', N''nvarchar'', 512, 1, NULL, NULL),
(N''AspNetUsers'', N''EmailConfirmed'', N''bit'', 1, 0, NULL, NULL),
(N''AspNetUsers'', N''PasswordHash'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AspNetUsers'', N''SecurityStamp'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AspNetUsers'', N''ConcurrencyStamp'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AspNetUsers'', N''PhoneNumber'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AspNetUsers'', N''PhoneNumberConfirmed'', N''bit'', 1, 0, NULL, NULL),
(N''AspNetUsers'', N''TwoFactorEnabled'', N''bit'', 1, 0, NULL, NULL),
(N''AspNetUsers'', N''LockoutEnd'', N''datetimeoffset'', 10, 1, NULL, NULL),
(N''AspNetUsers'', N''LockoutEnabled'', N''bit'', 1, 0, NULL, NULL),
(N''AspNetUsers'', N''AccessFailedCount'', N''int'', 4, 0, NULL, NULL),
(N''AspNetUsers'', N''DisplayName'', N''nvarchar'', 400, 1, NULL, NULL),
(N''AspNetUsers'', N''CreatedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''AspNetUsers'', N''IsActive'', N''bit'', 1, 0, NULL, NULL),
(N''AspNetRoles'', N''Id'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetRoles'', N''Name'', N''nvarchar'', 512, 1, NULL, NULL),
(N''AspNetRoles'', N''NormalizedName'', N''nvarchar'', 512, 1, NULL, NULL),
(N''AspNetRoles'', N''ConcurrencyStamp'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AspNetUserClaims'', N''Id'', N''int'', 4, 0, NULL, NULL),
(N''AspNetUserClaims'', N''UserId'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetUserClaims'', N''ClaimType'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AspNetUserClaims'', N''ClaimValue'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AspNetUserLogins'', N''LoginProvider'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetUserLogins'', N''ProviderKey'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetUserLogins'', N''ProviderDisplayName'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AspNetUserLogins'', N''UserId'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetUserRoles'', N''UserId'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetUserRoles'', N''RoleId'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetUserTokens'', N''UserId'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetUserTokens'', N''LoginProvider'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetUserTokens'', N''Name'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetUserTokens'', N''Value'', N''nvarchar'', -1, 1, NULL, NULL),
(N''ModelVersions'', N''ModelVersionID'', N''bigint'', 8, 0, NULL, NULL),
(N''ModelVersions'', N''ModelName'', N''nvarchar'', 200, 0, NULL, NULL),
(N''ModelVersions'', N''Version'', N''nvarchar'', 100, 0, NULL, NULL),
(N''ModelVersions'', N''ArtifactUri'', N''nvarchar'', 4096, 1, NULL, NULL),
(N''ModelVersions'', N''ArtifactSha256'', N''binary'', 32, 1, NULL, NULL),
(N''ModelVersions'', N''DatasetProvenance'', N''nvarchar'', -1, 0, NULL, NULL),
(N''ModelVersions'', N''TrainedUtc'', N''datetime2'', 8, 1, NULL, NULL),
(N''ModelVersions'', N''RegisteredUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''ModelVersions'', N''IsActive'', N''bit'', 1, 0, NULL, NULL),
(N''ClaimStatusHistory'', N''StatusHistoryID'', N''bigint'', 8, 0, NULL, NULL),
(N''ClaimStatusHistory'', N''FilingID'', N''int'', 4, 0, NULL, NULL),
(N''ClaimStatusHistory'', N''FromStatus'', N''nvarchar'', 100, 1, NULL, NULL),
(N''ClaimStatusHistory'', N''ToStatus'', N''nvarchar'', 100, 0, NULL, NULL),
(N''ClaimStatusHistory'', N''ActorUserId'', N''nvarchar'', 900, 1, NULL, NULL),
(N''ClaimStatusHistory'', N''TransitionedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''ClaimStatusHistory'', N''Reason'', N''nvarchar'', 1000, 1, NULL, NULL),
(N''TriageAttempts'', N''TriageAttemptID'', N''bigint'', 8, 0, NULL, NULL),
(N''TriageAttempts'', N''SubmittedByUserId'', N''nvarchar'', 900, 0, NULL, NULL),
(N''TriageAttempts'', N''InputText'', N''nvarchar'', -1, 0, NULL, NULL),
(N''TriageAttempts'', N''PredictedFilingTypeID'', N''int'', 4, 1, NULL, NULL),
(N''TriageAttempts'', N''ClassificationScore'', N''decimal'', 5, 1, 9, 8),
(N''TriageAttempts'', N''HeuristicPriority'', N''tinyint'', 1, 1, NULL, NULL),
(N''TriageAttempts'', N''TriageRuleVersion'', N''nvarchar'', 100, 0, NULL, NULL),
(N''TriageAttempts'', N''ModelVersionID'', N''bigint'', 8, 1, NULL, NULL),
(N''TriageAttempts'', N''CreatedFilingID'', N''int'', 4, 1, NULL, NULL),
(N''TriageAttempts'', N''Outcome'', N''nvarchar'', 60, 0, NULL, NULL),
(N''TriageAttempts'', N''CreatedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''TriageAttempts'', N''CompletedUtc'', N''datetime2'', 8, 1, NULL, NULL),
(N''TriageAttempts'', N''ExtractedClaimantName'', N''nvarchar'', 400, 1, NULL, NULL),
(N''TriageAttempts'', N''ExtractedAddress'', N''nvarchar'', 510, 1, NULL, NULL),
(N''TriageAttempts'', N''ExtractedCity'', N''nvarchar'', 200, 1, NULL, NULL),
(N''TriageAttempts'', N''ExtractedState'', N''char'', 2, 1, NULL, NULL),
(N''TriageAttempts'', N''ExtractedNotes'', N''nvarchar'', -1, 1, NULL, NULL),
(N''TriageAttempts'', N''FailureCode'', N''nvarchar'', 200, 1, NULL, NULL),
(N''TriageAttempts'', N''FailureMessage'', N''nvarchar'', -1, 1, NULL, NULL),
(N''TriageFeedback'', N''TriageFeedbackID'', N''bigint'', 8, 0, NULL, NULL),
(N''TriageFeedback'', N''TriageAttemptID'', N''bigint'', 8, 0, NULL, NULL),
(N''TriageFeedback'', N''ReviewedByUserId'', N''nvarchar'', 900, 0, NULL, NULL),
(N''TriageFeedback'', N''WasCorrect'', N''bit'', 1, 0, NULL, NULL),
(N''TriageFeedback'', N''CorrectedFilingTypeID'', N''int'', 4, 1, NULL, NULL),
(N''TriageFeedback'', N''CorrectedIssueTags'', N''nvarchar'', -1, 1, NULL, NULL),
(N''TriageFeedback'', N''CorrectionNotes'', N''nvarchar'', -1, 1, NULL, NULL),
(N''TriageFeedback'', N''ReviewedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''ModelEvaluationResults'', N''ModelEvaluationResultID'', N''bigint'', 8, 0, NULL, NULL),
(N''ModelEvaluationResults'', N''ModelVersionID'', N''bigint'', 8, 0, NULL, NULL),
(N''ModelEvaluationResults'', N''DatasetName'', N''nvarchar'', 400, 0, NULL, NULL),
(N''ModelEvaluationResults'', N''DatasetVersion'', N''nvarchar'', 200, 0, NULL, NULL),
(N''ModelEvaluationResults'', N''DatasetProvenance'', N''nvarchar'', -1, 0, NULL, NULL),
(N''ModelEvaluationResults'', N''EvaluatedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''ModelEvaluationResults'', N''SampleCount'', N''bigint'', 8, 0, NULL, NULL),
(N''ModelEvaluationResults'', N''Accuracy'', N''decimal'', 5, 0, 9, 8),
(N''ModelEvaluationResults'', N''MacroF1'', N''decimal'', 5, 0, 9, 8),
(N''ModelEvaluationResults'', N''WeightedF1'', N''decimal'', 5, 0, 9, 8),
(N''ModelEvaluationResults'', N''PrecisionScore'', N''decimal'', 5, 1, 9, 8),
(N''ModelEvaluationResults'', N''RecallScore'', N''decimal'', 5, 1, 9, 8),
(N''ModelEvaluationResults'', N''Notes'', N''nvarchar'', -1, 1, NULL, NULL),
(N''IdempotencyRecords'', N''IdempotencyRecordID'', N''bigint'', 8, 0, NULL, NULL),
(N''IdempotencyRecords'', N''UserId'', N''nvarchar'', 900, 0, NULL, NULL),
(N''IdempotencyRecords'', N''OperationName'', N''nvarchar'', 200, 0, NULL, NULL),
(N''IdempotencyRecords'', N''IdempotencyKey'', N''nvarchar'', 400, 0, NULL, NULL),
(N''IdempotencyRecords'', N''PayloadHash'', N''varbinary'', 32, 0, NULL, NULL),
(N''IdempotencyRecords'', N''State'', N''nvarchar'', 40, 0, NULL, NULL),
(N''IdempotencyRecords'', N''ResponseStatusCode'', N''int'', 4, 1, NULL, NULL),
(N''IdempotencyRecords'', N''ResponseBody'', N''nvarchar'', -1, 1, NULL, NULL),
(N''IdempotencyRecords'', N''CreatedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''IdempotencyRecords'', N''CompletedUtc'', N''datetime2'', 8, 1, NULL, NULL),
(N''DataSources'', N''DataSourceID'', N''int'', 4, 0, NULL, NULL),
(N''DataSources'', N''SourceName'', N''nvarchar'', 400, 0, NULL, NULL),
(N''DataSources'', N''SourceUrl'', N''nvarchar'', 4096, 0, NULL, NULL),
(N''DataSources'', N''ProvenanceStatement'', N''nvarchar'', -1, 0, NULL, NULL),
(N''DataSources'', N''IsActive'', N''bit'', 1, 0, NULL, NULL),
(N''DataSources'', N''CreatedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''DataSources'', N''UpdatedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''IngestionRuns'', N''IngestionRunID'', N''bigint'', 8, 0, NULL, NULL),
(N''IngestionRuns'', N''DataSourceID'', N''int'', 4, 0, NULL, NULL),
(N''IngestionRuns'', N''Status'', N''nvarchar'', 40, 0, NULL, NULL),
(N''IngestionRuns'', N''StartedUtc'', N''datetime2'', 8, 1, NULL, NULL),
(N''IngestionRuns'', N''CompletedUtc'', N''datetime2'', 8, 1, NULL, NULL),
(N''IngestionRuns'', N''LastCheckpoint'', N''nvarchar'', 1000, 1, NULL, NULL),
(N''IngestionRuns'', N''RowsRead'', N''bigint'', 8, 0, NULL, NULL),
(N''IngestionRuns'', N''RowsAccepted'', N''bigint'', 8, 0, NULL, NULL),
(N''IngestionRuns'', N''RowsUpdated'', N''bigint'', 8, 0, NULL, NULL),
(N''IngestionRuns'', N''RowsRejected'', N''bigint'', 8, 0, NULL, NULL),
(N''IngestionRuns'', N''ErrorCount'', N''bigint'', 8, 0, NULL, NULL),
(N''IngestionRuns'', N''CreatedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''IngestionQuarantineErrors'', N''QuarantineErrorID'', N''bigint'', 8, 0, NULL, NULL),
(N''IngestionQuarantineErrors'', N''IngestionRunID'', N''bigint'', 8, 0, NULL, NULL),
(N''IngestionQuarantineErrors'', N''ExternalRecordKey'', N''nvarchar'', 1000, 0, NULL, NULL),
(N''IngestionQuarantineErrors'', N''ErrorCode'', N''nvarchar'', 200, 0, NULL, NULL),
(N''IngestionQuarantineErrors'', N''ErrorMessage'', N''nvarchar'', -1, 0, NULL, NULL),
(N''IngestionQuarantineErrors'', N''RawPayload'', N''nvarchar'', -1, 1, NULL, NULL),
(N''IngestionQuarantineErrors'', N''CreatedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''IngestionQuarantineErrors'', N''ResolvedUtc'', N''datetime2'', 8, 1, NULL, NULL),
(N''ExternalProperties'', N''ExternalPropertyID'', N''bigint'', 8, 0, NULL, NULL),
(N''ExternalProperties'', N''DataSourceID'', N''int'', 4, 0, NULL, NULL),
(N''ExternalProperties'', N''ExternalPropertyKey'', N''nvarchar'', 400, 0, NULL, NULL),
(N''ExternalProperties'', N''ParcelIdentifier'', N''nvarchar'', 200, 1, NULL, NULL),
(N''ExternalProperties'', N''Address'', N''nvarchar'', 510, 1, NULL, NULL),
(N''ExternalProperties'', N''City'', N''nvarchar'', 200, 1, NULL, NULL),
(N''ExternalProperties'', N''State'', N''char'', 2, 1, NULL, NULL),
(N''ExternalProperties'', N''County'', N''nvarchar'', 200, 1, NULL, NULL),
(N''ExternalProperties'', N''RawPayload'', N''nvarchar'', -1, 1, NULL, NULL),
(N''ExternalProperties'', N''FirstSeenUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''ExternalProperties'', N''LastSeenUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''ExternalPropertyDocuments'', N''ExternalPropertyDocumentID'', N''bigint'', 8, 0, NULL, NULL),
(N''ExternalPropertyDocuments'', N''DataSourceID'', N''int'', 4, 0, NULL, NULL),
(N''ExternalPropertyDocuments'', N''ExternalDocumentKey'', N''nvarchar'', 400, 0, NULL, NULL),
(N''ExternalPropertyDocuments'', N''SourceDocumentType'', N''nvarchar'', 200, 0, NULL, NULL),
(N''ExternalPropertyDocuments'', N''RecordedDate'', N''date'', 3, 1, NULL, NULL),
(N''ExternalPropertyDocuments'', N''DocumentUrl'', N''nvarchar'', 4096, 1, NULL, NULL),
(N''ExternalPropertyDocuments'', N''RawPayload'', N''nvarchar'', -1, 1, NULL, NULL),
(N''ExternalPropertyDocuments'', N''FirstSeenUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''ExternalPropertyDocuments'', N''LastSeenUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''ExternalPropertyDocuments'', N''LastIngestionRunID'', N''bigint'', 8, 1, NULL, NULL),
(N''ExternalDocumentProperties'', N''ExternalPropertyDocumentID'', N''bigint'', 8, 0, NULL, NULL),
(N''ExternalDocumentProperties'', N''ExternalPropertyID'', N''bigint'', 8, 0, NULL, NULL),
(N''ExternalDocumentProperties'', N''RelationshipType'', N''nvarchar'', 200, 0, NULL, NULL),
(N''ExternalDocumentProperties'', N''SourceRelationshipKey'', N''nvarchar'', 400, 1, NULL, NULL),
(N''AspNetRoleClaims'', N''Id'', N''int'', 4, 0, NULL, NULL),
(N''AspNetRoleClaims'', N''RoleId'', N''nvarchar'', 900, 0, NULL, NULL),
(N''AspNetRoleClaims'', N''ClaimType'', N''nvarchar'', -1, 1, NULL, NULL),
(N''AspNetRoleClaims'', N''ClaimValue'', N''nvarchar'', -1, 1, NULL, NULL),
(N''LegalFilings'', N''SubmittedByUserId'', N''nvarchar'', 900, 1, NULL, NULL),
(N''LegalFilings'', N''CreatedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''LegalFilings'', N''UpdatedUtc'', N''datetime2'', 8, 0, NULL, NULL),
(N''LegalFilings'', N''CreationSource'', N''nvarchar'', 40, 0, NULL, NULL),
(N''LegalFilings'', N''IssueTags'', N''nvarchar'', -1, 1, NULL, NULL),
(N''LegalFilings'', N''ClassificationScore'', N''decimal'', 5, 1, 9, 8),
(N''LegalFilings'', N''ModelVersionID'', N''bigint'', 8, 1, NULL, NULL),
(N''LegalFilings'', N''TriagePriority'', N''tinyint'', 1, 0, NULL, NULL),
(N''LegalFilings'', N''TriageRuleVersion'', N''nvarchar'', 100, 0, NULL, NULL),
(N''LegalFilings'', N''RowVersion'', N''timestamp'', 8, 0, NULL, NULL),
(N''LegalFilings'', N''IsDeleted'', N''bit'', 1, 0, NULL, NULL),
(N''LegalFilings'', N''DeletedUtc'', N''datetime2'', 8, 1, NULL, NULL),
(N''LegalFilings'', N''DeletedByUserId'', N''nvarchar'', 900, 1, NULL, NULL),
(N''LegalFilings'', N''IsSyntheticDemoData'', N''bit'', 1, 0, NULL, NULL),
(N''AuditLog'', N''ActorUserId'', N''nvarchar'', 900, 1, NULL, NULL),
(N''AuditLog'', N''Action'', N''nvarchar'', 60, 0, NULL, NULL);
IF EXISTS (
    SELECT 1 FROM #ExpectedColumns e
    LEFT JOIN sys.tables t ON t.name = e.TableName AND t.schema_id = SCHEMA_ID(N''dbo'')
    LEFT JOIN sys.columns c ON c.object_id = t.object_id AND c.name = e.ColumnName
    WHERE c.column_id IS NULL OR TYPE_NAME(c.system_type_id) <> e.TypeName
       OR c.max_length <> e.MaxLength OR c.is_nullable <> e.IsNullable
       OR (e.PrecisionValue IS NOT NULL AND (c.precision <> e.PrecisionValue OR c.scale <> e.ScaleValue))
)
BEGIN
    SELECT e.*, TYPE_NAME(c.system_type_id) AS ActualType, c.max_length AS ActualLength, c.is_nullable AS ActualNullable
    FROM #ExpectedColumns e
    LEFT JOIN sys.tables t ON t.name = e.TableName AND t.schema_id = SCHEMA_ID(N''dbo'')
    LEFT JOIN sys.columns c ON c.object_id = t.object_id AND c.name = e.ColumnName
    WHERE c.column_id IS NULL OR TYPE_NAME(c.system_type_id) <> e.TypeName
       OR c.max_length <> e.MaxLength OR c.is_nullable <> e.IsNullable
       OR (e.PrecisionValue IS NOT NULL AND (c.precision <> e.PrecisionValue OR c.scale <> e.ScaleValue));
    ;THROW 51041, N''Existing column definitions differ from the application contract. See mismatch results; all upgrade changes are rolled back.'', 1;
END;
DROP TABLE #ExpectedColumns;


/* Identity string IDs remain nvarchar(450), matching the existing model.
   Nonclustered composite keys allow up to 1700 bytes on SQL Server 2016+.
   CHECK constraints bound actual combined values, including trailing spaces.
   Existing oversized values cause a rollback, never truncation. */

IF EXISTS (SELECT 1 FROM dbo.AspNetUserLogins WHERE DATALENGTH([LoginProvider]) + DATALENGTH([ProviderKey]) > 1700)
BEGIN
    ;THROW 51040, N''AspNetUserLogins: combined key exceeds 1700 bytes. No values were truncated.'', 1;
END;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AspNetUserLogins'') AND is_primary_key = 1 AND type = 1)
BEGIN
    DECLARE @pk_AspNetUserLogins sysname = (SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N''dbo.AspNetUserLogins'') AND type = ''PK'');
    DECLARE @ddl_AspNetUserLogins nvarchar(max) = N''ALTER TABLE dbo.AspNetUserLogins DROP CONSTRAINT '' + QUOTENAME(@pk_AspNetUserLogins);
    EXEC sys.sp_executesql @ddl_AspNetUserLogins;
    ALTER TABLE dbo.AspNetUserLogins ADD CONSTRAINT PK_AspNetUserLogins PRIMARY KEY NONCLUSTERED ([LoginProvider], [ProviderKey]);
END;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N''dbo.AspNetUserLogins'') AND name = N''CK_AspNetUserLogins_KeyBytes'')
    ALTER TABLE dbo.AspNetUserLogins WITH CHECK ADD CONSTRAINT CK_AspNetUserLogins_KeyBytes CHECK (DATALENGTH([LoginProvider]) + DATALENGTH([ProviderKey]) <= 1700);

IF EXISTS (SELECT 1 FROM dbo.AspNetUserRoles WHERE DATALENGTH([UserId]) + DATALENGTH([RoleId]) > 1700)
BEGIN
    ;THROW 51040, N''AspNetUserRoles: combined key exceeds 1700 bytes. No values were truncated.'', 1;
END;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AspNetUserRoles'') AND is_primary_key = 1 AND type = 1)
BEGIN
    DECLARE @pk_AspNetUserRoles sysname = (SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N''dbo.AspNetUserRoles'') AND type = ''PK'');
    DECLARE @ddl_AspNetUserRoles nvarchar(max) = N''ALTER TABLE dbo.AspNetUserRoles DROP CONSTRAINT '' + QUOTENAME(@pk_AspNetUserRoles);
    EXEC sys.sp_executesql @ddl_AspNetUserRoles;
    ALTER TABLE dbo.AspNetUserRoles ADD CONSTRAINT PK_AspNetUserRoles PRIMARY KEY NONCLUSTERED ([UserId], [RoleId]);
END;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N''dbo.AspNetUserRoles'') AND name = N''CK_AspNetUserRoles_KeyBytes'')
    ALTER TABLE dbo.AspNetUserRoles WITH CHECK ADD CONSTRAINT CK_AspNetUserRoles_KeyBytes CHECK (DATALENGTH([UserId]) + DATALENGTH([RoleId]) <= 1700);

IF EXISTS (SELECT 1 FROM dbo.AspNetUserTokens WHERE DATALENGTH([UserId]) + DATALENGTH([LoginProvider]) + DATALENGTH([Name]) > 1700)
BEGIN
    ;THROW 51040, N''AspNetUserTokens: combined key exceeds 1700 bytes. No values were truncated.'', 1;
END;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AspNetUserTokens'') AND is_primary_key = 1 AND type = 1)
BEGIN
    DECLARE @pk_AspNetUserTokens sysname = (SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N''dbo.AspNetUserTokens'') AND type = ''PK'');
    DECLARE @ddl_AspNetUserTokens nvarchar(max) = N''ALTER TABLE dbo.AspNetUserTokens DROP CONSTRAINT '' + QUOTENAME(@pk_AspNetUserTokens);
    EXEC sys.sp_executesql @ddl_AspNetUserTokens;
    ALTER TABLE dbo.AspNetUserTokens ADD CONSTRAINT PK_AspNetUserTokens PRIMARY KEY NONCLUSTERED ([UserId], [LoginProvider], [Name]);
END;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N''dbo.AspNetUserTokens'') AND name = N''CK_AspNetUserTokens_KeyBytes'')
    ALTER TABLE dbo.AspNetUserTokens WITH CHECK ADD CONSTRAINT CK_AspNetUserTokens_KeyBytes CHECK (DATALENGTH([UserId]) + DATALENGTH([LoginProvider]) + DATALENGTH([Name]) <= 1700);
';

    -- Phase 13: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'
DECLARE @RepairFks nvarchar(max) = N'''';
SELECT @RepairFks = @RepairFks + N''ALTER TABLE '' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N''.'' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N'' DROP CONSTRAINT '' + QUOTENAME(name) + N'';''
FROM sys.foreign_keys
WHERE name IN (N''FK_LegalFilings_SubmittedByUser'',N''FK_LegalFilings_DeletedByUser'',N''FK_LegalFilings_ModelVersion'',N''FK_AuditLog_ActorUser'',N''FK_ClaimStatusHistory_ActorUser'',N''FK_TriageAttempts_PredictedFilingType'',N''FK_TriageAttempts_ModelVersion'',N''FK_TriageFeedback_CorrectedFilingType'',N''FK_ExternalPropertyDocuments_LastIngestionRun'')
AND OBJECT_SCHEMA_NAME(parent_object_id) = N''dbo''
AND delete_referential_action <> 0;
IF @RepairFks <> N'''' EXEC sys.sp_executesql @RepairFks;
/* 7. Foreign keys. All application/history/public relationships are restrictive or nullable SET NULL; no audit/history cascade exists. */
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_LegalFilings_SubmittedByUser'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT FK_LegalFilings_SubmittedByUser FOREIGN KEY (SubmittedByUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_LegalFilings_Properties'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT FK_LegalFilings_Properties FOREIGN KEY (PropertyID) REFERENCES dbo.Properties(PropertyID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_LegalFilings_FilingTypes'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT FK_LegalFilings_FilingTypes FOREIGN KEY (FilingTypeID) REFERENCES dbo.FilingTypes(FilingTypeID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_LegalFilings_DeletedByUser'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT FK_LegalFilings_DeletedByUser FOREIGN KEY (DeletedByUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_LegalFilings_ModelVersion'' AND parent_object_id = OBJECT_ID(N''dbo.LegalFilings''))
		ALTER TABLE dbo.LegalFilings ADD CONSTRAINT FK_LegalFilings_ModelVersion FOREIGN KEY (ModelVersionID) REFERENCES dbo.ModelVersions(ModelVersionID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_AuditLog_ActorUser'' AND parent_object_id = OBJECT_ID(N''dbo.AuditLog''))
		ALTER TABLE dbo.AuditLog ADD CONSTRAINT FK_AuditLog_ActorUser FOREIGN KEY (ActorUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_ClaimStatusHistory_LegalFilings'' AND parent_object_id = OBJECT_ID(N''dbo.ClaimStatusHistory''))
		ALTER TABLE dbo.ClaimStatusHistory ADD CONSTRAINT FK_ClaimStatusHistory_LegalFilings FOREIGN KEY (FilingID) REFERENCES dbo.LegalFilings(FilingID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_ClaimStatusHistory_ActorUser'' AND parent_object_id = OBJECT_ID(N''dbo.ClaimStatusHistory''))
		ALTER TABLE dbo.ClaimStatusHistory ADD CONSTRAINT FK_ClaimStatusHistory_ActorUser FOREIGN KEY (ActorUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_TriageAttempts_SubmittedByUser'' AND parent_object_id = OBJECT_ID(N''dbo.TriageAttempts''))
		ALTER TABLE dbo.TriageAttempts ADD CONSTRAINT FK_TriageAttempts_SubmittedByUser FOREIGN KEY (SubmittedByUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_TriageAttempts_PredictedFilingType'' AND parent_object_id = OBJECT_ID(N''dbo.TriageAttempts''))
		ALTER TABLE dbo.TriageAttempts ADD CONSTRAINT FK_TriageAttempts_PredictedFilingType FOREIGN KEY (PredictedFilingTypeID) REFERENCES dbo.FilingTypes(FilingTypeID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_TriageAttempts_ModelVersion'' AND parent_object_id = OBJECT_ID(N''dbo.TriageAttempts''))
		ALTER TABLE dbo.TriageAttempts ADD CONSTRAINT FK_TriageAttempts_ModelVersion FOREIGN KEY (ModelVersionID) REFERENCES dbo.ModelVersions(ModelVersionID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_TriageAttempts_CreatedFiling'' AND parent_object_id = OBJECT_ID(N''dbo.TriageAttempts''))
		ALTER TABLE dbo.TriageAttempts ADD CONSTRAINT FK_TriageAttempts_CreatedFiling FOREIGN KEY (CreatedFilingID) REFERENCES dbo.LegalFilings(FilingID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_TriageFeedback_TriageAttempt'' AND parent_object_id = OBJECT_ID(N''dbo.TriageFeedback''))
		ALTER TABLE dbo.TriageFeedback ADD CONSTRAINT FK_TriageFeedback_TriageAttempt FOREIGN KEY (TriageAttemptID) REFERENCES dbo.TriageAttempts(TriageAttemptID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_TriageFeedback_ReviewedByUser'' AND parent_object_id = OBJECT_ID(N''dbo.TriageFeedback''))
		ALTER TABLE dbo.TriageFeedback ADD CONSTRAINT FK_TriageFeedback_ReviewedByUser FOREIGN KEY (ReviewedByUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_TriageFeedback_CorrectedFilingType'' AND parent_object_id = OBJECT_ID(N''dbo.TriageFeedback''))
		ALTER TABLE dbo.TriageFeedback ADD CONSTRAINT FK_TriageFeedback_CorrectedFilingType FOREIGN KEY (CorrectedFilingTypeID) REFERENCES dbo.FilingTypes(FilingTypeID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_ModelEvaluationResults_ModelVersion'' AND parent_object_id = OBJECT_ID(N''dbo.ModelEvaluationResults''))
		ALTER TABLE dbo.ModelEvaluationResults ADD CONSTRAINT FK_ModelEvaluationResults_ModelVersion FOREIGN KEY (ModelVersionID) REFERENCES dbo.ModelVersions(ModelVersionID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_IdempotencyRecords_User'' AND parent_object_id = OBJECT_ID(N''dbo.IdempotencyRecords''))
		ALTER TABLE dbo.IdempotencyRecords ADD CONSTRAINT FK_IdempotencyRecords_User FOREIGN KEY (UserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_IngestionRuns_DataSource'' AND parent_object_id = OBJECT_ID(N''dbo.IngestionRuns''))
		ALTER TABLE dbo.IngestionRuns ADD CONSTRAINT FK_IngestionRuns_DataSource FOREIGN KEY (DataSourceID) REFERENCES dbo.DataSources(DataSourceID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_IngestionQuarantineErrors_IngestionRun'' AND parent_object_id = OBJECT_ID(N''dbo.IngestionQuarantineErrors''))
		ALTER TABLE dbo.IngestionQuarantineErrors ADD CONSTRAINT FK_IngestionQuarantineErrors_IngestionRun FOREIGN KEY (IngestionRunID) REFERENCES dbo.IngestionRuns(IngestionRunID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_ExternalProperties_DataSource'' AND parent_object_id = OBJECT_ID(N''dbo.ExternalProperties''))
		ALTER TABLE dbo.ExternalProperties ADD CONSTRAINT FK_ExternalProperties_DataSource FOREIGN KEY (DataSourceID) REFERENCES dbo.DataSources(DataSourceID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_ExternalPropertyDocuments_DataSource'' AND parent_object_id = OBJECT_ID(N''dbo.ExternalPropertyDocuments''))
		ALTER TABLE dbo.ExternalPropertyDocuments ADD CONSTRAINT FK_ExternalPropertyDocuments_DataSource FOREIGN KEY (DataSourceID) REFERENCES dbo.DataSources(DataSourceID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_ExternalPropertyDocuments_LastIngestionRun'' AND parent_object_id = OBJECT_ID(N''dbo.ExternalPropertyDocuments''))
		ALTER TABLE dbo.ExternalPropertyDocuments ADD CONSTRAINT FK_ExternalPropertyDocuments_LastIngestionRun FOREIGN KEY (LastIngestionRunID) REFERENCES dbo.IngestionRuns(IngestionRunID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_ExternalDocumentProperties_Document'' AND parent_object_id = OBJECT_ID(N''dbo.ExternalDocumentProperties''))
		ALTER TABLE dbo.ExternalDocumentProperties ADD CONSTRAINT FK_ExternalDocumentProperties_Document FOREIGN KEY (ExternalPropertyDocumentID) REFERENCES dbo.ExternalPropertyDocuments(ExternalPropertyDocumentID) ON DELETE NO ACTION;
	IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_ExternalDocumentProperties_Property'' AND parent_object_id = OBJECT_ID(N''dbo.ExternalDocumentProperties''))
		ALTER TABLE dbo.ExternalDocumentProperties ADD CONSTRAINT FK_ExternalDocumentProperties_Property FOREIGN KEY (ExternalPropertyID) REFERENCES dbo.ExternalProperties(ExternalPropertyID) ON DELETE NO ACTION;';

    -- Phase 14: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 8. Seed only the existing controlled filing-type reference data. */
	INSERT dbo.FilingTypes(TypeName)
	SELECT v.TypeName
	FROM (VALUES (N''Title Claim''), (N''Lien''), (N''Easement''), (N''Deed Dispute'')) v(TypeName)
	WHERE NOT EXISTS (SELECT 1 FROM dbo.FilingTypes f WHERE f.TypeName = v.TypeName);

	/* Legacy rows remain unassigned. They are visible only through administrator queries. */
	INSERT dbo.ClaimStatusHistory(FilingID, FromStatus, ToStatus, ActorUserId, TransitionedUtc, Reason)
	SELECT l.FilingID, NULL, l.Status, NULL, l.UpdatedUtc, N''Legacy status baseline''
	FROM dbo.LegalFilings l
	WHERE NOT EXISTS
	(
		SELECT 1
		FROM dbo.ClaimStatusHistory h
		WHERE h.FilingID = l.FilingID
	);';

    -- Phase 15: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 9. Indexes used by the actual owner/status/date and ingestion-key paths. */
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AspNetUsers'') AND name = N''UserNameIndex'')
		CREATE UNIQUE INDEX UserNameIndex ON dbo.AspNetUsers(NormalizedUserName) WHERE NormalizedUserName IS NOT NULL;
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AspNetUsers'') AND name = N''EmailIndex'')
		CREATE INDEX EmailIndex ON dbo.AspNetUsers(NormalizedEmail);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AspNetRoles'') AND name = N''RoleNameIndex'')
		CREATE UNIQUE INDEX RoleNameIndex ON dbo.AspNetRoles(NormalizedName) WHERE NormalizedName IS NOT NULL;
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AspNetUserClaims'') AND name = N''IX_AspNetUserClaims_UserId'')
		CREATE INDEX IX_AspNetUserClaims_UserId ON dbo.AspNetUserClaims(UserId);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AspNetUserLogins'') AND name = N''IX_AspNetUserLogins_UserId'')
		CREATE INDEX IX_AspNetUserLogins_UserId ON dbo.AspNetUserLogins(UserId);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AspNetUserRoles'') AND name = N''IX_AspNetUserRoles_RoleId'')
		CREATE INDEX IX_AspNetUserRoles_RoleId ON dbo.AspNetUserRoles(RoleId);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.FilingTypes'') AND name = N''UQ_FilingTypes_TypeName'')
		CREATE UNIQUE INDEX UQ_FilingTypes_TypeName ON dbo.FilingTypes(TypeName);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.ModelVersions'') AND name = N''UQ_ModelVersions_Name_Version'')
		CREATE UNIQUE INDEX UQ_ModelVersions_Name_Version ON dbo.ModelVersions(ModelName, Version);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.ModelEvaluationResults'') AND name = N''UQ_ModelEvaluationResults_Model_Dataset'')
		CREATE UNIQUE INDEX UQ_ModelEvaluationResults_Model_Dataset ON dbo.ModelEvaluationResults(ModelVersionID, DatasetName, DatasetVersion);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.IdempotencyRecords'') AND name = N''UQ_IdempotencyRecords_User_Operation_Key'')
		CREATE UNIQUE INDEX UQ_IdempotencyRecords_User_Operation_Key ON dbo.IdempotencyRecords(UserId, OperationName, IdempotencyKey);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.DataSources'') AND name = N''UQ_DataSources_SourceName'')
		CREATE UNIQUE INDEX UQ_DataSources_SourceName ON dbo.DataSources(SourceName);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.ExternalProperties'') AND name = N''UQ_ExternalProperties_Source_Key'')
		CREATE UNIQUE INDEX UQ_ExternalProperties_Source_Key ON dbo.ExternalProperties(DataSourceID, ExternalPropertyKey);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.ExternalPropertyDocuments'') AND name = N''UQ_ExternalPropertyDocuments_Source_Key'')
		CREATE UNIQUE INDEX UQ_ExternalPropertyDocuments_Source_Key ON dbo.ExternalPropertyDocuments(DataSourceID, ExternalDocumentKey);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.LegalFilings'') AND name = N''IX_LegalFilings_Property_Status'')
		CREATE INDEX IX_LegalFilings_Property_Status ON dbo.LegalFilings(PropertyID, Status);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.LegalFilings'') AND name = N''IX_LegalFilings_Type_Date'')
		CREATE INDEX IX_LegalFilings_Type_Date ON dbo.LegalFilings(FilingTypeID, DateFiled);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.LegalFilings'') AND name = N''IX_LegalFilings_SubmittedByUser_Status_CreatedUtc'')
		CREATE INDEX IX_LegalFilings_SubmittedByUser_Status_CreatedUtc ON dbo.LegalFilings(SubmittedByUserId, Status, CreatedUtc) WHERE IsDeleted = 0;
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.LegalFilings'') AND name = N''IX_LegalFilings_Status_CreatedUtc'')
		CREATE INDEX IX_LegalFilings_Status_CreatedUtc ON dbo.LegalFilings(Status, CreatedUtc) WHERE IsDeleted = 0;
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.AuditLog'') AND name = N''IX_AuditLog_Record'')
		CREATE INDEX IX_AuditLog_Record ON dbo.AuditLog(TableName, RecordID, ChangeDate);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.ClaimStatusHistory'') AND name = N''IX_ClaimStatusHistory_Filing_Time'')
		CREATE INDEX IX_ClaimStatusHistory_Filing_Time ON dbo.ClaimStatusHistory(FilingID, TransitionedUtc, StatusHistoryID);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.ClaimStatusHistory'') AND name = N''IX_ClaimStatusHistory_Actor_Time'')
		CREATE INDEX IX_ClaimStatusHistory_Actor_Time ON dbo.ClaimStatusHistory(ActorUserId, TransitionedUtc);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.TriageAttempts'') AND name = N''IX_TriageAttempts_User_CreatedUtc'')
		CREATE INDEX IX_TriageAttempts_User_CreatedUtc ON dbo.TriageAttempts(SubmittedByUserId, CreatedUtc);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.TriageFeedback'') AND name = N''UQ_TriageFeedback_TriageAttempt'')
		CREATE UNIQUE INDEX UQ_TriageFeedback_TriageAttempt ON dbo.TriageFeedback(TriageAttemptID);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.IngestionRuns'') AND name = N''IX_IngestionRuns_Source_CreatedUtc'')
		CREATE INDEX IX_IngestionRuns_Source_CreatedUtc ON dbo.IngestionRuns(DataSourceID, CreatedUtc);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.IngestionQuarantineErrors'') AND name = N''IX_IngestionQuarantineErrors_Run_Record'')
		CREATE INDEX IX_IngestionQuarantineErrors_Run_Record ON dbo.IngestionQuarantineErrors(IngestionRunID, ExternalRecordKey);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.ExternalProperties'') AND name = N''IX_ExternalProperties_Source_Parcel'')
		CREATE INDEX IX_ExternalProperties_Source_Parcel ON dbo.ExternalProperties(DataSourceID, ParcelIdentifier);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.ExternalPropertyDocuments'') AND name = N''IX_ExternalPropertyDocuments_DocumentType'')
		CREATE INDEX IX_ExternalPropertyDocuments_DocumentType ON dbo.ExternalPropertyDocuments(SourceDocumentType);
	IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N''dbo.ExternalDocumentProperties'') AND name = N''IX_ExternalDocumentProperties_Property'')
		CREATE INDEX IX_ExternalDocumentProperties_Property ON dbo.ExternalDocumentProperties(ExternalPropertyID);';

    -- Phase 16: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* Remove both known legacy duplicate audit writers. Deploy with the matching EF audit writer. */
	IF OBJECT_ID(N''dbo.trg_LegalFilings_Audit'', N''TR'') IS NOT NULL
		DROP TRIGGER dbo.trg_LegalFilings_Audit;
IF OBJECT_ID(N''dbo.trg_LegalFilings_AuditStatus'', N''TR'') IS NOT NULL DROP TRIGGER dbo.trg_LegalFilings_AuditStatus;';

    -- Phase 17: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 11. Read-only reporting objects. Every definition is executed as a separate DDL batch. */
	EXEC sys.sp_executesql N''CREATE OR ALTER VIEW dbo.vw_ClaimOperations AS
	SELECT l.FilingID, l.PropertyID, p.Address, p.City, p.State, l.FilingTypeID AS PrimaryFilingTypeID,
		   ft.TypeName AS PrimaryFilingType, l.DateFiled, l.Status, l.SubmittedByUserId,
		   l.CreatedUtc, l.UpdatedUtc, l.CreationSource, l.IssueTags,
		   l.RiskScore AS HeuristicRiskScore, l.ClassificationScore AS ModelClassificationScore,
		   l.ModelVersionID, l.TriagePriority, l.TriageRuleVersion,
		   l.IsDeleted, l.IsSyntheticDemoData
	FROM dbo.LegalFilings AS l
	INNER JOIN dbo.Properties AS p ON p.PropertyID = l.PropertyID
	INNER JOIN dbo.FilingTypes AS ft ON ft.FilingTypeID = l.FilingTypeID;'';

	EXEC sys.sp_executesql N''CREATE OR ALTER VIEW dbo.vw_ClaimStatusTransitions AS
	SELECT h.StatusHistoryID, h.FilingID, h.FromStatus, h.ToStatus, h.ActorUserId,
		   h.TransitionedUtc, h.Reason
	FROM dbo.ClaimStatusHistory AS h;'';

	EXEC sys.sp_executesql N''CREATE OR ALTER VIEW dbo.vw_TriageAttemptsOutcomes AS
	SELECT ta.TriageAttemptID, ta.SubmittedByUserId, ta.PredictedFilingTypeID,
		   ft.TypeName AS PredictedFilingType, ta.ClassificationScore AS ModelClassificationScore,
		   ta.HeuristicPriority, ta.TriageRuleVersion, ta.ModelVersionID,
		   ta.CreatedFilingID, ta.Outcome, ta.CreatedUtc, ta.CompletedUtc,
		   tf.TriageFeedbackID, tf.WasCorrect, tf.CorrectedFilingTypeID,
		   corrected.TypeName AS CorrectedFilingType, tf.ReviewedUtc
	FROM dbo.TriageAttempts AS ta
	LEFT JOIN dbo.FilingTypes AS ft ON ft.FilingTypeID = ta.PredictedFilingTypeID
	LEFT JOIN dbo.TriageFeedback AS tf ON tf.TriageAttemptID = ta.TriageAttemptID
	LEFT JOIN dbo.FilingTypes AS corrected ON corrected.FilingTypeID = tf.CorrectedFilingTypeID;'';

	EXEC sys.sp_executesql N''CREATE OR ALTER VIEW dbo.vw_ModelEvaluationResults AS
	SELECT me.ModelEvaluationResultID, me.ModelVersionID, mv.ModelName, mv.Version AS ModelVersion,
		   me.DatasetName, me.DatasetVersion, me.DatasetProvenance, me.EvaluatedUtc,
		   me.SampleCount, me.Accuracy, me.MacroF1, me.WeightedF1,
		   me.PrecisionScore, me.RecallScore, me.Notes
	FROM dbo.ModelEvaluationResults AS me
	INNER JOIN dbo.ModelVersions AS mv ON mv.ModelVersionID = me.ModelVersionID;'';

	EXEC sys.sp_executesql N''CREATE OR ALTER VIEW dbo.vw_PublicDocumentImportQuality AS
	SELECT r.IngestionRunID, r.DataSourceID, ds.SourceName, ds.SourceUrl,
		   r.Status, r.StartedUtc, r.CompletedUtc, r.LastCheckpoint,
		   r.RowsRead, r.RowsAccepted, r.RowsUpdated, r.RowsRejected, r.ErrorCount,
		   (SELECT COUNT_BIG(*) FROM dbo.ExternalPropertyDocuments d WHERE d.LastIngestionRunID = r.IngestionRunID) AS ExternalDocumentsSeen,
		   (SELECT COUNT_BIG(*) FROM dbo.IngestionQuarantineErrors q WHERE q.IngestionRunID = r.IngestionRunID) AS QuarantineErrorsRecorded
	FROM dbo.IngestionRuns AS r
	INNER JOIN dbo.DataSources AS ds ON ds.DataSourceID = r.DataSourceID;'';

	EXEC sys.sp_executesql N''CREATE OR ALTER VIEW dbo.vw_PublicDemoClaimExport AS
	SELECT N''''Synthetic application data'''' AS DataClassification,
		   l.FilingID AS DemoClaimID, p.Address, p.City, p.State,
		   ft.TypeName AS ApplicationFilingType, l.DateFiled, l.Status,
		   l.CreatedUtc, l.UpdatedUtc, l.TriagePriority,
		   l.RiskScore AS HeuristicRiskScore, l.ClassificationScore AS ModelClassificationScore
	FROM dbo.LegalFilings AS l
	INNER JOIN dbo.Properties AS p ON p.PropertyID = l.PropertyID
	INNER JOIN dbo.FilingTypes AS ft ON ft.FilingTypeID = l.FilingTypeID
	WHERE l.IsSyntheticDemoData = 1 AND l.IsDeleted = 0;'';

	EXEC sys.sp_executesql N''CREATE OR ALTER PROCEDURE dbo.sp_GetFilingRiskReport @PropertyID int = NULL AS
	BEGIN
		SET NOCOUNT ON;
		SELECT p.PropertyID, p.Address, p.City, p.State,
			   CAST(COALESCE(AVG(CAST(l.RiskScore AS float)), 0) AS float) AS AverageRiskScore,
			   SUM(CASE WHEN l.FilingID IS NOT NULL AND l.Status NOT IN (N''''Resolved'''', N''''Closed'''') THEN 1 ELSE 0 END) AS OpenClaimsCount,
			   COUNT(l.FilingID) AS FilingCount,
			   COUNT(DISTINCT l.FilingTypeID) AS FilingTypeCount
		FROM dbo.Properties AS p
		LEFT JOIN dbo.LegalFilings AS l ON l.PropertyID = p.PropertyID AND l.IsDeleted = 0
		WHERE @PropertyID IS NULL OR p.PropertyID = @PropertyID
		GROUP BY p.PropertyID, p.Address, p.City, p.State
		ORDER BY p.PropertyID;
	END;'';';

    -- Phase 18: separate compilation, shared transaction.
    EXEC sys.sp_executesql N'/* 12. Record the successful version only after every object has completed. */
	IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version = N''2.0.3'')
	BEGIN
		INSERT dbo.SchemaVersions(Version, ScriptName, Description)
		VALUES (N''2.0.3'', N''Upgrade_TitleClaimIntelligence.sql'', N''Identity ownership, claim intelligence, audit/history, public ingestion, reporting, and safe soft deletion.'');
	END;

	IF (SELECT COUNT_BIG(*) FROM dbo.Properties) < (SELECT RowCountBefore FROM #BeforeCounts WHERE TableName = N''Properties'') OR
	   (SELECT COUNT_BIG(*) FROM dbo.FilingTypes) < (SELECT RowCountBefore FROM #BeforeCounts WHERE TableName = N''FilingTypes'') OR
	   (SELECT COUNT_BIG(*) FROM dbo.LegalFilings) < (SELECT RowCountBefore FROM #BeforeCounts WHERE TableName = N''LegalFilings'') OR
	   (SELECT COUNT_BIG(*) FROM dbo.AuditLog) < (SELECT RowCountBefore FROM #BeforeCounts WHERE TableName = N''AuditLog'')
	BEGIN
		;THROW 51032, N''Legacy row-count preservation check failed; the transaction will be rolled back.'', 1;
	END;';


    COMMIT TRANSACTION;
    DROP TABLE #BeforeCounts;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    DROP TABLE IF EXISTS #BeforeCounts;
    ;THROW;
END CATCH;

-- Reaching this point means every schema phase committed successfully.
EXEC sys.sp_executesql N'/* 13. Post-upgrade validation. These result sets are evidence for the operator. */
SELECT Version, AppliedUtc, AppliedBy, ScriptName, Description
FROM dbo.SchemaVersions
WHERE Version = N''2.0.3'';

SELECT name, type_desc
FROM sys.objects
WHERE schema_id = SCHEMA_ID(N''dbo'')
  AND name IN
  (
	  N''Properties'', N''FilingTypes'', N''LegalFilings'', N''AuditLog'',
	  N''AspNetUsers'', N''AspNetRoles'', N''AspNetRoleClaims'', N''AspNetUserClaims'', N''AspNetUserLogins'', N''AspNetUserRoles'', N''AspNetUserTokens'',
	  N''ClaimStatusHistory'', N''TriageAttempts'', N''TriageFeedback'', N''ModelVersions'', N''ModelEvaluationResults'', N''IdempotencyRecords'',
	  N''DataSources'', N''IngestionRuns'', N''IngestionQuarantineErrors'', N''ExternalProperties'', N''ExternalPropertyDocuments'', N''ExternalDocumentProperties'',
	  N''vw_ClaimOperations'', N''vw_ClaimStatusTransitions'', N''vw_TriageAttemptsOutcomes'', N''vw_ModelEvaluationResults'', N''vw_PublicDocumentImportQuality'', N''vw_PublicDemoClaimExport'',
	  N''sp_GetFilingRiskReport''
  )
ORDER BY type_desc, name;

SELECT
	(SELECT COUNT_BIG(*) FROM dbo.Properties) AS Properties,
	(SELECT COUNT_BIG(*) FROM dbo.FilingTypes) AS FilingTypes,
	(SELECT COUNT_BIG(*) FROM dbo.LegalFilings) AS LegalFilings,
	(SELECT COUNT_BIG(*) FROM dbo.AuditLog) AS AuditLogRows,
	(SELECT COUNT_BIG(*) FROM dbo.ClaimStatusHistory) AS ClaimStatusHistoryRows,
	(SELECT COUNT_BIG(*) FROM dbo.AspNetUsers) AS IdentityUsers,
	(SELECT COUNT_BIG(*) FROM dbo.LegalFilings WHERE SubmittedByUserId IS NULL) AS UnassignedLegacyOrAdminRows,
	(SELECT COUNT_BIG(*) FROM dbo.LegalFilings WHERE ClassificationScore IS NOT NULL AND (ClassificationScore < 0 OR ClassificationScore > 1)) AS InvalidClassificationScores,
	(SELECT COUNT_BIG(*) FROM dbo.LegalFilings WHERE RiskScore IS NOT NULL AND (RiskScore < 0 OR RiskScore > 1)) AS InvalidHeuristicScores;

SELECT i.name AS IndexName, OBJECT_SCHEMA_NAME(i.object_id) AS SchemaName, OBJECT_NAME(i.object_id) AS TableName
FROM sys.indexes AS i
WHERE i.object_id IN
(
	OBJECT_ID(N''dbo.LegalFilings''), OBJECT_ID(N''dbo.ClaimStatusHistory''), OBJECT_ID(N''dbo.TriageAttempts''),
	OBJECT_ID(N''dbo.ExternalProperties''), OBJECT_ID(N''dbo.ExternalPropertyDocuments''), OBJECT_ID(N''dbo.IdempotencyRecords'')
)
  AND i.name IS NOT NULL
ORDER BY TableName, IndexName;

IF OBJECT_ID(N''dbo.trg_LegalFilings_Audit'', N''TR'') IS NOT NULL
	BEGIN
	    ;THROW 51033, N''The duplicate LegalFilings audit trigger is still present.'', 1;
	END;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version = N''2.0.3'')
	BEGIN
	    ;THROW 51034, N''Schema version 2.0.3 was not recorded.'', 1;
	END;
';
