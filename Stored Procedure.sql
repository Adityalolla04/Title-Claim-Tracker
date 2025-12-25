---------------------------------------------------------------------------------------------------
-- Stored Procedure: GetFilingsSummary
---------------------------------------------------------------------------------------------------
-- FIX: Use CREATE OR ALTER to ensure the database updates the procedure if it exists
CREATE OR ALTER PROCEDURE GetFilingsSummary 
    @FilingTypeFilter NVARCHAR(100) = NULL,
    @FilingStatusFilter NVARCHAR(50) = NULL,
    @CityFilter NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT 
        lf.FilingID, 
        p.Address, 
        p.City, 
        ft.TypeName AS FilingTypeName, 
        lf.DateFiled, 
        lf.Status, 
        lf.ClaimantName
    FROM 
        LegalFilings lf
    INNER JOIN 
        Properties p ON lf.PropertyID = p.PropertyID
    INNER JOIN 
        FilingTypes ft ON lf.FilingTypeID = ft.FilingTypeID
    WHERE
        -- Filtering logic from our previous successful steps
        (@FilingTypeFilter IS NULL OR @FilingTypeFilter = '' OR ft.TypeName = @FilingTypeFilter)
        AND (@FilingStatusFilter IS NULL OR @FilingStatusFilter = '' OR lf.Status = @FilingStatusFilter)
        AND (@CityFilter IS NULL OR @CityFilter = '' OR p.City = @CityFilter)
        
    ORDER BY 
        lf.DateFiled DESC;
END
GO