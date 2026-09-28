/* Title Claim Intelligence 2.0.4 - private claim PDF attachments.
   Run the whole script in the selected existing database after 2.0.3.
   No USE, GO, SQLCMD variables, database creation, or startup migration. */
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
BEGIN
    ;THROW 52040, N'SchemaVersions is missing. Apply the canonical 2.0.3 upgrade first.', 1;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version = N'2.0.3')
BEGIN
    ;THROW 52041, N'Schema version 2.0.3 is required before applying this upgrade.', 1;
END;

IF OBJECT_ID(N'dbo.ClaimDocuments', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version = N'2.0.4')
BEGIN
    ;THROW 52042, N'ClaimDocuments already exists without the 2.0.4 version record; inspect the schema before proceeding.', 1;
END;

IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version = N'2.0.4')
   AND OBJECT_ID(N'dbo.ClaimDocuments', N'U') IS NULL
BEGIN
    ;THROW 52043, N'Schema version 2.0.4 is recorded but ClaimDocuments is missing; inspect the schema before proceeding.', 1;
END;

IF OBJECT_ID(N'dbo.ClaimDocuments', N'U') IS NOT NULL
   AND (COL_LENGTH(N'dbo.ClaimDocuments', N'ClaimDocumentID') IS NULL
        OR COL_LENGTH(N'dbo.ClaimDocuments', N'FilingID') IS NULL
        OR COL_LENGTH(N'dbo.ClaimDocuments', N'UploadedByUserId') IS NULL
        OR COL_LENGTH(N'dbo.ClaimDocuments', N'FileName') IS NULL
        OR COL_LENGTH(N'dbo.ClaimDocuments', N'ContentType') IS NULL
        OR COL_LENGTH(N'dbo.ClaimDocuments', N'FileSizeBytes') IS NULL
        OR COL_LENGTH(N'dbo.ClaimDocuments', N'Content') IS NULL
        OR COL_LENGTH(N'dbo.ClaimDocuments', N'UploadedUtc') IS NULL)
BEGIN
    ;THROW 52044, N'ClaimDocuments exists with an incompatible column shape; inspect the schema before proceeding.', 1;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.ClaimDocuments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ClaimDocuments
        (
            ClaimDocumentID bigint IDENTITY(1,1) NOT NULL,
            FilingID int NOT NULL,
            UploadedByUserId nvarchar(450) NOT NULL,
            FileName nvarchar(255) NOT NULL,
            ContentType nvarchar(100) NOT NULL CONSTRAINT DF_ClaimDocuments_ContentType DEFAULT (N'application/pdf'),
            FileSizeBytes int NOT NULL,
            Content varbinary(max) NOT NULL,
            UploadedUtc datetime2(7) NOT NULL CONSTRAINT DF_ClaimDocuments_UploadedUtc DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT PK_ClaimDocuments PRIMARY KEY CLUSTERED (ClaimDocumentID),
            CONSTRAINT CK_ClaimDocuments_ContentType CHECK (ContentType = N'application/pdf'),
            CONSTRAINT CK_ClaimDocuments_FileSizeBytes CHECK (FileSizeBytes BETWEEN 1 AND 10485760),
            CONSTRAINT FK_ClaimDocuments_LegalFilings FOREIGN KEY (FilingID) REFERENCES dbo.LegalFilings(FilingID),
            CONSTRAINT FK_ClaimDocuments_AspNetUsers FOREIGN KEY (UploadedByUserId) REFERENCES dbo.AspNetUsers(Id)
        );

        CREATE INDEX IX_ClaimDocuments_Filing_UploadedUtc
            ON dbo.ClaimDocuments(FilingID, UploadedUtc);
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version = N'2.0.4')
    BEGIN
        INSERT dbo.SchemaVersions(Version, ScriptName, Description)
        VALUES (N'2.0.4', N'Upgrade_ClaimDocuments_2.0.4.sql', N'Private PDF supporting documents attached to owned title claims.');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT Version, AppliedUtc, ScriptName, Description
FROM dbo.SchemaVersions
WHERE Version IN (N'2.0.3', N'2.0.4')
ORDER BY Version;
