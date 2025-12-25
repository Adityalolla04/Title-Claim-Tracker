SET NOCOUNT ON;
PRINT 'Starting bulk data insertion (100 rows total)...';

-- Variables for Loop and Data Generation
DECLARE @i INT = 1;
DECLARE @MaxRows INT = 100;
DECLARE @BaseDate DATE = '2025-01-01';
DECLARE @City NVARCHAR(100);
DECLARE @State CHAR(2);
DECLARE @Claimant NVARCHAR(255);
DECLARE @Address NVARCHAR(255);
DECLARE @FilingTypeID INT;
DECLARE @Status NVARCHAR(50);
DECLARE @DateFiled DATE;
DECLARE @PropertyID INT;

-- Data arrays for randomization
DECLARE @Cities TABLE (Name NVARCHAR(100), State CHAR(2));
INSERT INTO @Cities (Name, State) VALUES
('Dallas', 'TX'), ('Austin', 'TX'), ('Houston', 'TX'), ('San Antonio', 'TX'),
('Fort Worth', 'TX'), ('Frisco', 'TX'), ('Plano', 'TX');

DECLARE @FilingTypes TABLE (ID INT, Name NVARCHAR(100));
-- Note: Assuming FilingTypes table already contains IDs 1-5 based on previous creation script
INSERT INTO @FilingTypes (ID, Name) VALUES (1, 'Title Claim'), (2, 'Deed Dispute'), (3, 'Easement Issue'), (4, 'Lien Removal'), (5, 'Zoning Violation');

-- Status values used for the CHECK constraint
DECLARE @Statuses TABLE (Name NVARCHAR(50));
INSERT INTO @Statuses (Name) VALUES 
('New'), ('Pending Review'), ('Active Investigation'), ('Closed');

-- 1. Populate PROPERTIES table (~50 rows)
-- We insert roughly half the total rows into Properties, aiming for ~50 properties
WHILE @i <= 50
BEGIN
    -- Randomly select City/State
    SELECT TOP 1 @City = Name, @State = State FROM @Cities ORDER BY NEWID();

    -- Generate a unique address
    SET @Address = CAST(FLOOR(RAND() * 900 + 100) AS NVARCHAR) + 
                   CASE WHEN @i % 3 = 0 THEN ' Elm St'
                        WHEN @i % 3 = 1 THEN ' Oak Ave'
                        ELSE ' Maple Dr' END;
    
    INSERT INTO Properties (Address, City, State, LegalDescription, CreatedDate)
    VALUES (@Address, @City, @State, 
            'Lot ' + CAST(@i AS NVARCHAR) + ' of the ' + @City + ' Subdivision.', 
            DATEADD(day, -ABS(CHECKSUM(NEWID()) % 365), GETDATE()));

    SET @i = @i + 1;
END
PRINT '...Properties table populated with 50 rows.';

-- Reset counter for Filings
SET @i = 1;

-- 2. Populate LEGALFILINGS table (~100 rows)
-- We insert 100 filings, linking them back to the 50 properties (average 2 filings per property)
WHILE @i <= @MaxRows
BEGIN
    -- Select a random PropertyID from the existing 50 properties
    SELECT TOP 1 @PropertyID = PropertyID FROM Properties ORDER BY NEWID();

    -- Randomly select Filing Type ID
    SELECT TOP 1 @FilingTypeID = ID FROM @FilingTypes ORDER BY NEWID();

    -- Randomly select Status
    SELECT TOP 1 @Status = Name FROM @Statuses ORDER BY NEWID();

    -- Generate a random Date Filed (within the last 300 days)
    SET @DateFiled = DATEADD(day, -ABS(CHECKSUM(NEWID()) % 300), GETDATE());

    -- Generate a random Claimant Name
    SET @Claimant = 
        CASE WHEN @i % 5 = 0 THEN 'ABC Title Co.'
             WHEN @i % 5 = 1 THEN 'John Smith'
             WHEN @i % 5 = 2 THEN 'Jane Doe'
             WHEN @i % 5 = 3 THEN 'City of ' + @City
             ELSE 'Federal Bank' END;

    INSERT INTO LegalFilings (PropertyID, FilingTypeID, DateFiled, Status, ClaimantName, Notes)
    VALUES (@PropertyID, @FilingTypeID, @DateFiled, @Status, @Claimant, 
            'Generated filing ' + CAST(@i AS NVARCHAR) + ' for automated testing purposes.');

    SET @i = @i + 1;
END
PRINT '...LegalFilings table populated with 100 rows.';
PRINT 'Bulk data insertion complete.';
GO

-- 3. Verification Queries (FIXED SYNTAX)
-- Verify the row counts (Aliases enclosed in [] to fix syntax error)
SELECT 'Properties Count' AS [TableName], COUNT(*) AS [RowCount] FROM Properties;
SELECT 'LegalFilings Count' AS [TableName], COUNT(*) AS [RowCount] FROM LegalFilings;
SELECT 'AuditLog Count (Should be 0)' AS [TableName], COUNT(*) AS [RowCount] FROM AuditLog;
GO

-- Test the Stored Procedure with filters
EXEC GetFilingsSummary @FilingStatusFilter = 'Pending Review', @CityFilter = 'Dallas';
GO


--1. Fetching Data
Select * from LegalFilings
Select * from filingTypes
Select * from Properties
Select * from AuditLog

--2 Fetching with the joins
SELECT
    LF.FilingID,
    LF.DateFiled,
    LF.Status,
    LF.ClaimantName,
    LF.Notes,
    
    P.PropertyID,
    P.Address,
    P.City,
    P.State,
    P.LegalDescription,
    
    FT.FilingTypeID,
    FT.TypeName AS FilingTypeName
FROM
    LegalFilings LF
INNER JOIN
    Properties P ON LF.PropertyID = P.PropertyID
INNER JOIN
    FilingTypes FT ON LF.FilingTypeID = FT.FilingTypeID
ORDER BY
    LF.DateFiled DESC;

Select * from LegalFilings where FilingID = 5007