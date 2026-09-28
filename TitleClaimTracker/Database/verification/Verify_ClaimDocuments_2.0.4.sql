/* Read-only verification for the private claim PDF attachment extension. */
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version = N'2.0.4')
BEGIN
    ;THROW 52045, N'Schema version 2.0.4 is not recorded.', 1;
END;

IF OBJECT_ID(N'dbo.ClaimDocuments', N'U') IS NULL
BEGIN
    ;THROW 52046, N'dbo.ClaimDocuments is missing.', 1;
END;

IF COL_LENGTH(N'dbo.ClaimDocuments', N'ClaimDocumentID') IS NULL
   OR COL_LENGTH(N'dbo.ClaimDocuments', N'FilingID') IS NULL
   OR COL_LENGTH(N'dbo.ClaimDocuments', N'UploadedByUserId') IS NULL
   OR COL_LENGTH(N'dbo.ClaimDocuments', N'FileName') IS NULL
   OR COL_LENGTH(N'dbo.ClaimDocuments', N'ContentType') IS NULL
   OR COL_LENGTH(N'dbo.ClaimDocuments', N'FileSizeBytes') IS NULL
   OR COL_LENGTH(N'dbo.ClaimDocuments', N'Content') IS NULL
   OR COL_LENGTH(N'dbo.ClaimDocuments', N'UploadedUtc') IS NULL
BEGIN
    ;THROW 52047, N'dbo.ClaimDocuments does not match the expected column contract.', 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ClaimDocuments') AND name = N'PK_ClaimDocuments')
   OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ClaimDocuments') AND name = N'CK_ClaimDocuments_ContentType')
   OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ClaimDocuments') AND name = N'CK_ClaimDocuments_FileSizeBytes')
   OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.ClaimDocuments') AND name = N'FK_ClaimDocuments_LegalFilings')
   OR NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.ClaimDocuments') AND name = N'FK_ClaimDocuments_AspNetUsers')
   OR NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ClaimDocuments') AND name = N'IX_ClaimDocuments_Filing_UploadedUtc')
BEGIN
    ;THROW 52048, N'dbo.ClaimDocuments is missing an expected key, check, foreign key, or index.', 1;
END;

SELECT Version, AppliedUtc, ScriptName, Description
FROM dbo.SchemaVersions
WHERE Version IN (N'2.0.3', N'2.0.4')
ORDER BY Version;

SELECT c.name AS ColumnName, t.name AS SqlType, c.max_length AS MaxLengthBytes, c.is_nullable AS IsNullable
FROM sys.columns AS c
JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.ClaimDocuments')
ORDER BY c.column_id;

SELECT name AS ConstraintOrIndexName, type_desc AS ObjectType
FROM sys.objects
WHERE parent_object_id = OBJECT_ID(N'dbo.ClaimDocuments')
  AND name IN
  (
      N'PK_ClaimDocuments', N'CK_ClaimDocuments_ContentType', N'CK_ClaimDocuments_FileSizeBytes',
      N'FK_ClaimDocuments_LegalFilings', N'FK_ClaimDocuments_AspNetUsers'
  )
UNION ALL
SELECT name, N'INDEX'
FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.ClaimDocuments')
  AND name = N'IX_ClaimDocuments_Filing_UploadedUtc';
