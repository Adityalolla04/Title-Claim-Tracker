/* READ ONLY, except session SET options. Run after the upgrade in the SAME target database. */
SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.SchemaVersions',N'U') IS NULL
BEGIN
    ;THROW 51100, N'SchemaVersions is missing. Check the selected database and upgrade result.', 1;
END;
EXEC sys.sp_executesql N'
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=N''2.0.3'')
BEGIN
    ;THROW 51101, N''Schema version 2.0.3 is not installed.'', 1;
END;
SELECT DB_NAME() AS DatabaseName, Version, AppliedUtc, ScriptName FROM dbo.SchemaVersions ORDER BY SchemaVersionID;
SELECT COUNT_BIG(*) AS Claims FROM dbo.LegalFilings;
SELECT COUNT_BIG(*) AS AuditRows FROM dbo.AuditLog;
SELECT COUNT_BIG(*) AS IdentityUsers FROM dbo.AspNetUsers;
SELECT COUNT_BIG(*) AS UnassignedClaims FROM dbo.LegalFilings WHERE SubmittedByUserId IS NULL;
SELECT COUNT_BIG(*) AS ClaimsWithoutHistory FROM dbo.LegalFilings l WHERE NOT EXISTS(SELECT 1 FROM dbo.ClaimStatusHistory h WHERE h.FilingID=l.FilingID);
SELECT FilingID, COUNT_BIG(*) AS RowsPerClaim FROM dbo.vw_ClaimOperations GROUP BY FilingID HAVING COUNT_BIG(*) <> 1;
SELECT TriageAttemptID, COUNT_BIG(*) AS RowsPerAttempt FROM dbo.vw_TriageAttemptsOutcomes GROUP BY TriageAttemptID HAVING COUNT_BIG(*) <> 1;
SELECT TOP (10) * FROM dbo.vw_PublicDemoClaimExport;
';
SELECT OBJECT_NAME(parent_object_id) AS TableName, name AS ForeignKeyName, delete_referential_action_desc
FROM sys.foreign_keys WHERE OBJECT_SCHEMA_NAME(parent_object_id)=N'dbo' ORDER BY TableName, ForeignKeyName;
SELECT OBJECT_NAME(object_id) AS TableName, name AS PrimaryKeyName, type_desc
FROM sys.indexes WHERE is_primary_key=1 AND OBJECT_NAME(object_id) IN (N'AspNetUserLogins',N'AspNetUserRoles',N'AspNetUserTokens');
SELECT name, is_disabled, is_not_trusted FROM sys.check_constraints WHERE name LIKE N'CK_AspNet%_KeyBytes';
SELECT name AS UnexpectedLegacyAuditTrigger FROM sys.triggers WHERE name IN (N'trg_LegalFilings_Audit',N'trg_LegalFilings_AuditStatus');

-- Existing labels/IDs must remain visible after the in-place upgrade.
EXEC sys.sp_executesql N'SELECT FilingTypeID, TypeName FROM dbo.FilingTypes ORDER BY FilingTypeID;
SELECT Status, COUNT_BIG(*) AS ClaimCount FROM dbo.LegalFilings GROUP BY Status;
SELECT COUNT_BIG(*) AS AuditRowsWithUnknownOriginalDate FROM dbo.AuditLog WHERE ChangeDate IS NULL;';
