-- Read-only verification for the explicit ACRIS import.
-- Run in the intended application database after an approved import.
DECLARE @SourceName nvarchar(200) = N'NYC Open Data ACRIS';
DECLARE @DataSourceID int = (SELECT DataSourceID FROM dbo.DataSources WHERE SourceName = @SourceName);

SELECT DataSourceID, SourceName, SourceUrl, IsActive, CreatedUtc, UpdatedUtc
FROM dbo.DataSources
WHERE DataSourceID = @DataSourceID;

SELECT TOP (20) IngestionRunID, DataSourceID, Status, StartedUtc, CompletedUtc,
       RowsRead, RowsAccepted, RowsUpdated, RowsRejected, ErrorCount, LastCheckpoint
FROM dbo.IngestionRuns
WHERE DataSourceID = @DataSourceID
ORDER BY IngestionRunID DESC;

SELECT COUNT_BIG(*) AS DocumentCount
FROM dbo.ExternalPropertyDocuments
WHERE DataSourceID = @DataSourceID;

SELECT COUNT_BIG(*) AS PropertyCount
FROM dbo.ExternalProperties
WHERE DataSourceID = @DataSourceID;

SELECT COUNT_BIG(*) AS RelationshipCount
FROM dbo.ExternalDocumentProperties AS relationship
JOIN dbo.ExternalPropertyDocuments AS document
  ON document.ExternalPropertyDocumentID = relationship.ExternalPropertyDocumentID
WHERE document.DataSourceID = @DataSourceID;

SELECT COUNT_BIG(*) AS DuplicateDocumentKeys
FROM (
    SELECT ExternalDocumentKey
    FROM dbo.ExternalPropertyDocuments
    WHERE DataSourceID = @DataSourceID
    GROUP BY ExternalDocumentKey
    HAVING COUNT_BIG(*) > 1
) AS duplicates;

-- Public records must not have become application claims.
SELECT COUNT_BIG(*) AS UnexpectedApplicationClaims
FROM dbo.LegalFilings
WHERE IsSyntheticDemoData = 0
  AND CreationSource IN (N'Manual', N'Assisted')
  AND Notes LIKE N'%ACRIS%';