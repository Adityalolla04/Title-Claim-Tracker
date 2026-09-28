CREATE DATABASE TitleClaimTracker
GO

-- 2. TABLE CREATION AND NORMALIZATION
---------------------------------------------------------------------------------------------------

-- 2.1. AuditLog Table (Must be created before the Trigger)
CREATE TABLE AuditLog (
    AuditID INT PRIMARY KEY IDENTITY,
    TableName NVARCHAR(128) NOT NULL,
    RecordID INT NOT NULL,
    ColumnChanged NVARCHAR(100),
    OldValue NVARCHAR(MAX),
    NewValue NVARCHAR(MAX),
    ChangeDate DATETIME DEFAULT GETDATE()
);
GO

-- 2.2. Properties Table (Primary entity)
CREATE TABLE Properties (
    PropertyID INT PRIMARY KEY IDENTITY(1001, 1),
    Address NVARCHAR(255) NOT NULL,
    City NVARCHAR(100) NOT NULL,
    [State] CHAR(2) NOT NULL,
    LegalDescription NVARCHAR(MAX),
    CreatedDate DATETIME DEFAULT GETDATE() NOT NULL
);
GO

-- 2.3. FilingTypes Table (Lookup Table for 1st Normal Form)
CREATE TABLE FilingTypes (
    FilingTypeID INT PRIMARY KEY IDENTITY(1, 1),
    TypeName NVARCHAR(100) UNIQUE NOT NULL
);
GO

-- Insert initial lookup data
INSERT INTO FilingTypes (TypeName) VALUES 
('Title Claim'), 
('Deed Dispute'), 
('Easement Issue'), 
('Lien Removal'),
('Zoning Violation');
GO

-- 2.4. LegalFilings Table (Main Transactional Table with Foreign Keys)
CREATE TABLE LegalFilings (
    FilingID INT PRIMARY KEY IDENTITY(5001, 1),
    PropertyID INT NOT NULL,
    FilingTypeID INT NOT NULL,
    DateFiled DATE NOT NULL,
    Status NVARCHAR(50) NOT NULL CHECK (Status IN ('New', 'Pending Review', 'Active Investigation', 'Closed')),
    ClaimantName NVARCHAR(255),
    Notes NVARCHAR(MAX),
    
    -- Foreign Key Constraints enforce data integrity
    CONSTRAINT FK_LegalFilings_Properties FOREIGN KEY (PropertyID) REFERENCES Properties(PropertyID),
    CONSTRAINT FK_LegalFilings_FilingTypes FOREIGN KEY (FilingTypeID) REFERENCES FilingTypes(FilingTypeID)
);
GO

-- 3. TRIGGER CREATION
---------------------------------------------------------------------------------------------------

-- Trigger: trg_LegalFilings_AuditStatus
-- Automates logging status changes to the AuditLog table.
CREATE TRIGGER trg_LegalFilings_AuditStatus
ON LegalFilings
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Only proceed if the Status column was actually updated
    IF UPDATE(Status)
    BEGIN
        INSERT INTO AuditLog (TableName, RecordID, ColumnChanged, OldValue, NewValue)
        SELECT 
            'LegalFilings',
            i.FilingID,
            'Status',
            d.Status, -- 'd' is the 'deleted' table (before update)
            i.Status  -- 'i' is the 'inserted' table (after update)
        FROM 
            inserted i
        INNER JOIN 
            deleted d ON i.FilingID = d.FilingID
        WHERE
            i.Status <> d.Status; -- Only log if the status value actually changed
    END
END;
GO

