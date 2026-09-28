SELECT
    -- Filing Details (from LegalFilings)
    L.FilingID,
    L.DateFiled,
    L.Status,
    L.ClaimantName,
    L.Notes,
    
    -- Property Details (from Properties)
    P.PropertyID,
    P.Address,
    P.City,
    P.State,
    
    -- Filing Type Details (from FilingTypes)
    F.TypeName AS FilingTypeName
FROM
    LegalFilings L
INNER JOIN
    Properties P 
ON
    L.PropertyID = P.PropertyID  -- Join 1: Links filing to its property
INNER JOIN
    FilingTypes F
ON
    L.FilingTypeID = F.FilingTypeID where l.FilingID = 5001

	USE TitleClaimTracker;
GO

-- 1. Verify the Filing Status actually changed
SELECT FilingID, Status 
FROM LegalFilings 
WHERE FilingID = 6004; -- Replace with the ID you changed

-- 2. Verify the Audit Log caught the change
SELECT TOP 5 * FROM AuditLog 
ORDER BY AuditID DESC;