/* READ ONLY. Select your intended database in SSMS and execute this file.
   No SQLCMD mode is needed. Save the result sets before upgrading. */
SET NOCOUNT ON;
SELECT DB_NAME() AS SelectedDatabase,
       SERVERPROPERTY('ProductVersion') AS ProductVersion,
       SERVERPROPERTY('ProductLevel') AS ProductLevel,
       SERVERPROPERTY('Edition') AS Edition,
       compatibility_level
FROM sys.databases WHERE database_id=DB_ID();

SELECT t.name AS TableName, c.name AS ColumnName, TYPE_NAME(c.system_type_id) AS SqlType,
       c.max_length AS MaxBytes, c.precision, c.scale, c.is_nullable, c.is_identity
FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
WHERE t.schema_id=SCHEMA_ID(N'dbo')
ORDER BY t.name, c.column_id;

SELECT t.name AS TableName, SUM(p.rows) AS ApproximateRows
FROM sys.tables t JOIN sys.partitions p ON p.object_id=t.object_id AND p.index_id IN (0,1)
WHERE t.schema_id=SCHEMA_ID(N'dbo') GROUP BY t.name ORDER BY t.name;

SELECT OBJECT_NAME(parent_object_id) AS TableName, name, definition, is_disabled, is_not_trusted
FROM sys.check_constraints WHERE OBJECT_SCHEMA_NAME(parent_object_id)=N'dbo';
SELECT OBJECT_NAME(parent_id) AS TableName, name AS TriggerName, is_disabled
FROM sys.triggers WHERE parent_class=1;
SELECT name AS FileName, type_desc, size*8.0/1024 AS SizeMB, max_size FROM sys.database_files;

IF OBJECT_ID(N'dbo.LegalFilings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.LegalFilings',N'Status') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT Status, COUNT_BIG(*) AS ClaimCount FROM dbo.LegalFilings GROUP BY Status;';
IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SchemaVersions',N'Version') IS NOT NULL
    EXEC sys.sp_executesql N'SELECT * FROM dbo.SchemaVersions;';

SELECT CASE
    WHEN OBJECT_ID(N'dbo.Properties',N'U') IS NULL AND OBJECT_ID(N'dbo.LegalFilings',N'U') IS NULL THEN N'Fresh or partial database: inspect table inventory.'
    WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AuditLog') AND name=N'AuditID' AND system_type_id=56)
      OR COL_LENGTH(N'dbo.FilingTypes',N'TypeName')=200 THEN N'Original root TableCreation.sql schema detected. Use the cumulative 2.0.3 upgrade, which preserves legacy names/statuses and widens fields without truncation.'
    ELSE N'Potentially compatible prototype schema. The upgrade still validates every expected column.'
END AS SchemaAssessment;
