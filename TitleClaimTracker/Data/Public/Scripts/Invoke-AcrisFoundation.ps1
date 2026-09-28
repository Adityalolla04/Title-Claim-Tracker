[CmdletBinding()]
param(
    [ValidateSet('Download', 'Validate', 'Import')]
    [string]$Mode = 'Validate',
    [int]$MasterLimit = 100000,
    [int]$PageSize = 1000,
    [string]$OutputDirectory,
    [string]$ConnectionString,
    [switch]$ConfirmImport,
    [string]$ExpectedServer = 'AdityaSrivatsav\SQLEXPRESS01',
    [string]$ExpectedDatabase = 'TitleClaimTracker'
)

$ErrorActionPreference = 'Stop'
if ($MasterLimit -lt 1 -or $MasterLimit -gt 100000) { throw 'MasterLimit must be between 1 and 100000.' }
if ($PageSize -lt 1 -or $PageSize -gt 5000) { throw 'PageSize must be between 1 and 5000.' }

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'outputs\acris'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$rawRoot = Join-Path $OutputDirectory 'raw'
$manifestRoot = Join-Path $OutputDirectory 'manifests'
$manifestPath = Join-Path $manifestRoot 'acris-snapshot.json'
New-Item -ItemType Directory -Force -Path $rawRoot, $manifestRoot | Out-Null

$datasets = @{
    Master = @{ Id = 'bnx9-e6tj'; Name = 'ACRIS Real Property Master'; Api = 'https://data.cityofnewyork.us/resource/bnx9-e6tj.json'; Metadata = 'https://data.cityofnewyork.us/api/views/bnx9-e6tj' }
    Legals = @{ Id = '8h5j-fqxa'; Name = 'ACRIS Real Property Legals'; Api = 'https://data.cityofnewyork.us/resource/8h5j-fqxa.json'; Metadata = 'https://data.cityofnewyork.us/api/views/8h5j-fqxa' }
    Codes = @{ Id = '7isb-wh4c'; Name = 'ACRIS Document Control Codes'; Api = 'https://data.cityofnewyork.us/resource/7isb-wh4c.json'; Metadata = 'https://data.cityofnewyork.us/api/views/7isb-wh4c' }
}

function Get-ApiHeaders {
    $headers = @{ Accept = 'application/json'; 'User-Agent' = 'TitleClaimIntelligence-data-foundation/1.0' }
    if ($env:SOCRATA_APP_TOKEN) { $headers['X-App-Token'] = $env:SOCRATA_APP_TOKEN }
    return $headers
}

function Invoke-SocrataJson([string]$Url) {
    $attempt = 0
    while ($true) {
        try {
            $response = Invoke-WebRequest -Uri $Url -Headers (Get-ApiHeaders) -Method Get -UseBasicParsing
            $rows = ConvertFrom-Json -InputObject $response.Content
            foreach ($row in $rows) { Write-Output $row }
            return
        }
        catch {
            $attempt++
            if ($attempt -ge 4) { throw }
            Start-Sleep -Seconds ([int][Math]::Pow(2, $attempt))
        }
    }
}

function Get-Field($Row, [string[]]$Names) {
    foreach ($name in $Names) {
        $property = $Row.PSObject.Properties[$name]
        if ($null -ne $property -and $null -ne $property.Value -and "$($property.Value)" -ne '') { return "$($property.Value)" }
    }
    return $null
}

function Read-RawRows([string]$Path) {
    if (-not (Test-Path $Path)) { throw "Missing raw source file: $Path" }
    $text = Get-Content -Raw -LiteralPath $Path
    if ([string]::IsNullOrWhiteSpace($text)) { return @() }
    return (ConvertFrom-Json -InputObject $text)
}

function Save-Raw([string]$Path, [string]$Url) {
    $response = Invoke-WebRequest -Uri $Url -Headers (Get-ApiHeaders) -UseBasicParsing
    [System.IO.File]::WriteAllText($Path, $response.Content, [System.Text.UTF8Encoding]::new($false))
    return $response.Content
}

function Download-Snapshot {
    $downloadedUtc = [DateTime]::UtcNow.ToString('o')
    $masterPath = Join-Path $rawRoot 'acris-master.json'
    $legalsPath = Join-Path $rawRoot 'acris-legals.json'
    $codesPath = Join-Path $rawRoot 'acris-document-codes.json'
    $metadata = [ordered]@{}
    foreach ($name in @('Master', 'Legals', 'Codes')) {
        try { $metadata[$name] = Invoke-SocrataJson $datasets[$name].Metadata }
        catch { $metadata[$name] = [ordered]@{ unavailable = $_.Exception.Message } }
    }

    $masterRows = @()
    for ($offset = 0; $offset -lt $MasterLimit; $offset += $PageSize) {
        $limit = [Math]::Min($PageSize, $MasterLimit - $offset)
        $url = "$($datasets.Master.Api)?`$limit=$limit&`$offset=$offset&`$order=:id ASC"
        $page = @(Invoke-SocrataJson $url)
        $masterRows += $page
        if ($page.Count -lt $limit) { break }
    }
    if ($masterRows.Count -eq 0) { throw 'The Master request returned no rows.' }
    [System.IO.File]::WriteAllText($masterPath, (ConvertTo-Json -InputObject $masterRows -Depth 10), [System.Text.UTF8Encoding]::new($false))

    $documentIds = @($masterRows | ForEach-Object { Get-Field $_ @('document_id', 'doc_id') } | Where-Object { $_ } | Select-Object -Unique)
    if ($documentIds.Count -ne $masterRows.Count) { throw 'Master rows contain missing or duplicate document IDs.' }
    $legalRows = @()
    for ($start = 0; $start -lt $documentIds.Count; $start += 50) {
        $ids = $documentIds[$start..([Math]::Min($start + 49, $documentIds.Count - 1))] | ForEach-Object { "'$(($_ -replace '''', ''''''))'" }
        $where = [uri]::EscapeDataString("document_id in (" + ($ids -join ',') + ")")
        for ($offset = 0; ; $offset += 5000) {
            $url = "$($datasets.Legals.Api)?`$where=$where&`$limit=5000&`$offset=$offset&`$order=:id ASC"
            $page = @(Invoke-SocrataJson $url)
            $legalRows += $page
            if ($page.Count -lt 5000) { break }
        }
    }
    [System.IO.File]::WriteAllText($legalsPath, (ConvertTo-Json -InputObject $legalRows -Depth 10), [System.Text.UTF8Encoding]::new($false))
    $codeUrl = "$($datasets.Codes.Api)?`$limit=5000&`$order=1"
    $codeText = Save-Raw $codesPath $codeUrl
    $manifest = [ordered]@{
        manifestVersion = '1.0'; retrievedUtc = $downloadedUtc; mode = 'Download';
        selection = [ordered]@{ masterLimit = $MasterLimit; pageSize = $PageSize; masterOrder = ':id ASC'; legalSelection = 'document_id in exact Master snapshot, paged by :id'; codeLimit = 5000 };
        sourceFiles = @(
            [ordered]@{ datasetId = $datasets.Master.Id; name = $datasets.Master.Name; file = 'Raw/acris-master.json'; rowCount = $masterRows.Count; sha256 = (Get-FileHash $masterPath -Algorithm SHA256).Hash },
            [ordered]@{ datasetId = $datasets.Legals.Id; name = $datasets.Legals.Name; file = 'Raw/acris-legals.json'; rowCount = $legalRows.Count; sha256 = (Get-FileHash $legalsPath -Algorithm SHA256).Hash },
            [ordered]@{ datasetId = $datasets.Codes.Id; name = $datasets.Codes.Name; file = 'Raw/acris-document-codes.json'; rowCount = @($codeText | ConvertFrom-Json).Count; sha256 = (Get-FileHash $codesPath -Algorithm SHA256).Hash }
        ); metadata = $metadata; appTokenUsed = [bool]$env:SOCRATA_APP_TOKEN; imported = $false
    }
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host "Downloaded $($masterRows.Count) Master rows and $($legalRows.Count) matching Legals rows."
    Write-Host "ACRIS run outputs: $OutputDirectory"
}

function Validate-Snapshot {
    $master = @(Read-RawRows (Join-Path $rawRoot 'acris-master.json'))
    $legals = @(Read-RawRows (Join-Path $rawRoot 'acris-legals.json'))
    $codes = @(Read-RawRows (Join-Path $rawRoot 'acris-document-codes.json'))
    if (-not (Test-Path $manifestPath)) { throw "Missing snapshot manifest: $manifestPath" }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $requestedMasterRows = [int]$manifest.selection.masterLimit
    $manifestMasterRows = [int](($manifest.sourceFiles | Where-Object datasetId -eq $datasets.Master.Id | Select-Object -First 1).rowCount)
    $masterIds = @($master | ForEach-Object { Get-Field $_ @('document_id', 'doc_id') })
    $legalIds = @($legals | ForEach-Object { Get-Field $_ @('document_id', 'doc_id') })
    $missingMasterIds = @($master | Where-Object { -not (Get-Field $_ @('document_id', 'doc_id')) }).Count
    $duplicateMasterIds = @($masterIds | Group-Object | Where-Object Count -gt 1).Count
    $orphanLegals = @($legalIds | Where-Object { $_ -notin $masterIds }).Count
    $missingLegalIds = @($legals | Where-Object { -not (Get-Field $_ @('document_id', 'doc_id')) }).Count
    $checks = [ordered]@{
        masterRows = $master.Count; legalRows = $legals.Count; codeRows = $codes.Count;
        requestedMasterRows = $requestedMasterRows; manifestMasterRows = $manifestMasterRows;
        masterCountMatchesRequest = ($master.Count -eq $requestedMasterRows); masterCountMatchesManifest = ($master.Count -eq $manifestMasterRows);
        missingMasterIds = $missingMasterIds; duplicateMasterIds = $duplicateMasterIds;
        missingLegalIds = $missingLegalIds; orphanLegalRows = $orphanLegals;
        masterToLegalScopePass = ($orphanLegals -eq 0); requiredIdentifiersPass = ($missingMasterIds -eq 0 -and $missingLegalIds -eq 0); duplicateKeyPass = ($duplicateMasterIds -eq 0)
    }
    $checks.status = if ($checks.masterCountMatchesRequest -and $checks.masterCountMatchesManifest -and $checks.masterToLegalScopePass -and $checks.requiredIdentifiersPass -and $checks.duplicateKeyPass) { 'Passed' } else { 'Failed' }
    $checks.validatedUtc = [DateTime]::UtcNow.ToString('o')
    $checks | ConvertTo-Json | Set-Content (Join-Path $manifestRoot 'validation-report.json') -Encoding UTF8
    $checks | Format-List
    if ($checks.status -ne 'Passed') { throw "Public snapshot validation failed. See $(Join-Path $manifestRoot 'validation-report.json')." }
}

function Add-Parameter($Command, [string]$Name, $Value, [System.Data.SqlDbType]$Type, [int]$Size = 0) {
    $parameter = $Command.Parameters.Add($Name, $Type)
    if ($Size -gt 0) { $parameter.Size = $Size }
    $parameter.Value = if ($null -eq $Value) { [DBNull]::Value } else { $Value }
}

function Import-Snapshot {
    if (-not $ConfirmImport) { throw 'Import is disabled without -ConfirmImport. Review the validation report and target database first.' }
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) { throw 'Import requires an explicit -ConnectionString. Use the intended existing database only after approval.' }
    Validate-Snapshot
    Add-Type -AssemblyName System.Data
    $master = @(Read-RawRows (Join-Path $rawRoot 'acris-master.json'))
    $legals = @(Read-RawRows (Join-Path $rawRoot 'acris-legals.json'))
    $sourceUrl = $datasets.Master.Api
    $connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
    try {
        $connection.Open()
        $targetCommand = $connection.CreateCommand()
        $targetCommand.CommandText = "SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)), DB_NAME();"
        $targetReader = $targetCommand.ExecuteReader()
        if (-not $targetReader.Read()) { throw 'Could not confirm the SQL Server and database target.' }
        $actualServer = $targetReader.GetString(0)
        $actualDatabase = $targetReader.GetString(1)
        $targetReader.Dispose()
        $targetCommand.Dispose()
        if ($actualServer -ine $ExpectedServer) { throw "Import target server mismatch: expected '$ExpectedServer', connected to '$actualServer'." }
        if ($actualDatabase -ine $ExpectedDatabase) { throw "Import target database mismatch: expected '$ExpectedDatabase', connected to '$actualDatabase'." }
        Write-Host "Confirmed import target: $actualServer / $actualDatabase"
    } catch {
        $connection.Dispose()
        throw
    }
    $transaction = $connection.BeginTransaction()
    try {
        $sourceCommand = $connection.CreateCommand(); $sourceCommand.Transaction = $transaction
        $sourceCommand.CommandText = "IF EXISTS (SELECT 1 FROM dbo.DataSources WHERE SourceName = @name) SELECT DataSourceID FROM dbo.DataSources WHERE SourceName = @name ELSE BEGIN INSERT dbo.DataSources(SourceName, SourceUrl, ProvenanceStatement, IsActive) VALUES(@name,@url,@provenance,1); SELECT CAST(SCOPE_IDENTITY() AS int); END"
        Add-Parameter $sourceCommand '@name' 'NYC Open Data ACRIS' ([System.Data.SqlDbType]::NVarChar) 200
        Add-Parameter $sourceCommand '@url' $sourceUrl ([System.Data.SqlDbType]::NVarChar) 2048
        Add-Parameter $sourceCommand '@provenance' 'Official NYC Open Data ACRIS snapshot; document records are not application claims.' ([System.Data.SqlDbType]::NVarChar) -1
        $dataSourceId = [int]$sourceCommand.ExecuteScalar()
        $runCommand = $connection.CreateCommand(); $runCommand.Transaction = $transaction; $runCommand.CommandText = "INSERT dbo.IngestionRuns(DataSourceID,Status,StartedUtc,LastCheckpoint) VALUES(@source,'Running',SYSUTCDATETIME(),@checkpoint); SELECT CAST(SCOPE_IDENTITY() AS bigint);"
        Add-Parameter $runCommand '@source' $dataSourceId ([System.Data.SqlDbType]::Int); Add-Parameter $runCommand '@checkpoint' 'ACRIS foundation snapshot' ([System.Data.SqlDbType]::NVarChar) 500
        $runId = [long]$runCommand.ExecuteScalar()
        $accepted = 0
        $rejected = 0
        foreach ($row in $master) {
            $documentId = Get-Field $row @('document_id', 'doc_id'); $type = Get-Field $row @('doc_type_general', 'doc_type', 'document_type'); $dateText = Get-Field $row @('recorded_datetime', 'recorded_date')
            $recorded = $null; if ($dateText) { try { $recorded = [DateTime]::Parse($dateText).Date } catch { $recorded = $null } }
            if (-not $type) { $type = 'Unknown' }
            $command = $connection.CreateCommand(); $command.Transaction = $transaction; $command.CommandText = "MERGE dbo.ExternalPropertyDocuments AS target USING (SELECT @source AS DataSourceID, @key AS ExternalDocumentKey) AS source ON target.DataSourceID=source.DataSourceID AND target.ExternalDocumentKey=source.ExternalDocumentKey WHEN MATCHED THEN UPDATE SET SourceDocumentType=@type,RecordedDate=@date,RawPayload=@raw,LastSeenUtc=SYSUTCDATETIME(),LastIngestionRunID=@run WHEN NOT MATCHED THEN INSERT(DataSourceID,ExternalDocumentKey,SourceDocumentType,RecordedDate,RawPayload,LastIngestionRunID) VALUES(@source,@key,@type,@date,@raw,@run);"
            Add-Parameter $command '@source' $dataSourceId ([System.Data.SqlDbType]::Int); Add-Parameter $command '@key' $documentId ([System.Data.SqlDbType]::NVarChar) 200; Add-Parameter $command '@type' $type ([System.Data.SqlDbType]::NVarChar) 100; Add-Parameter $command '@date' $recorded ([System.Data.SqlDbType]::Date); Add-Parameter $command '@raw' ($row | ConvertTo-Json -Compress) ([System.Data.SqlDbType]::NVarChar) -1; Add-Parameter $command '@run' $runId ([System.Data.SqlDbType]::BigInt)
            $command.ExecuteNonQuery(); $accepted++
        }
        foreach ($row in $legals) {
            $documentId = Get-Field $row @('document_id', 'doc_id')
            $borough = Get-Field $row @('borough', 'boro')
            $block = Get-Field $row @('block')
            $lot = Get-Field $row @('lot')
            if (-not $documentId -or -not $borough -or -not $block -or -not $lot) {
                $rejected++
                $quarantine = $connection.CreateCommand(); $quarantine.Transaction = $transaction; $quarantine.CommandText = "INSERT dbo.IngestionQuarantineErrors(IngestionRunID,ExternalRecordKey,ErrorCode,ErrorMessage,RawPayload) VALUES(@run,@key,'MissingParcelKey','ACRIS Legals row is missing document_id, borough, block, or lot.',@raw)"
                if (-not $documentId) { $documentId = 'missing-document-id' }
                Add-Parameter $quarantine '@run' $runId ([System.Data.SqlDbType]::BigInt); Add-Parameter $quarantine '@key' $documentId ([System.Data.SqlDbType]::NVarChar) 500; Add-Parameter $quarantine '@raw' ($row | ConvertTo-Json -Compress) ([System.Data.SqlDbType]::NVarChar) -1; $quarantine.ExecuteNonQuery() | Out-Null
                continue
            }
            $propertyKey = "$borough-$block-$lot"
            $addressParts = @((Get-Field $row @('street_number')), (Get-Field $row @('street_name'))) | Where-Object { $_ }
            $address = if ($addressParts.Count -gt 0) { $addressParts -join ' ' } else { $null }
            $unit = Get-Field $row @('apartment_number', 'unit')
            if ($unit) { $address = "$address Unit $unit" }
            $propertyCommand = $connection.CreateCommand(); $propertyCommand.Transaction = $transaction; $propertyCommand.CommandText = "MERGE dbo.ExternalProperties AS target USING (SELECT @source AS DataSourceID, @key AS ExternalPropertyKey) AS source ON target.DataSourceID=source.DataSourceID AND target.ExternalPropertyKey=source.ExternalPropertyKey WHEN MATCHED THEN UPDATE SET Address=COALESCE(@address,Address),LastSeenUtc=SYSUTCDATETIME(),RawPayload=@raw WHEN NOT MATCHED THEN INSERT(DataSourceID,ExternalPropertyKey,ParcelIdentifier,Address,City,State,County,RawPayload) VALUES(@source,@key,@parcel,@address,@city,@state,@county,@raw); SELECT ExternalPropertyID FROM dbo.ExternalProperties WHERE DataSourceID=@source AND ExternalPropertyKey=@key;"
            Add-Parameter $propertyCommand '@source' $dataSourceId ([System.Data.SqlDbType]::Int); Add-Parameter $propertyCommand '@key' $propertyKey ([System.Data.SqlDbType]::NVarChar) 200; Add-Parameter $propertyCommand '@parcel' $propertyKey ([System.Data.SqlDbType]::NVarChar) 100; Add-Parameter $propertyCommand '@address' $address ([System.Data.SqlDbType]::NVarChar) 255; Add-Parameter $propertyCommand '@city' 'New York' ([System.Data.SqlDbType]::NVarChar) 100; Add-Parameter $propertyCommand '@state' 'NY' ([System.Data.SqlDbType]::Char) 2; Add-Parameter $propertyCommand '@county' 'New York' ([System.Data.SqlDbType]::NVarChar) 100; Add-Parameter $propertyCommand '@raw' ($row | ConvertTo-Json -Compress) ([System.Data.SqlDbType]::NVarChar) -1
            $propertyId = [long]$propertyCommand.ExecuteScalar()
            $relationshipCommand = $connection.CreateCommand(); $relationshipCommand.Transaction = $transaction; $relationshipCommand.CommandText = "INSERT INTO dbo.ExternalDocumentProperties(ExternalPropertyDocumentID,ExternalPropertyID,RelationshipType,SourceRelationshipKey) SELECT d.ExternalPropertyDocumentID,@property,@relationship,@relationshipKey FROM dbo.ExternalPropertyDocuments d WHERE d.DataSourceID=@source AND d.ExternalDocumentKey=@document AND NOT EXISTS (SELECT 1 FROM dbo.ExternalDocumentProperties r WHERE r.ExternalPropertyDocumentID=d.ExternalPropertyDocumentID AND r.ExternalPropertyID=@property);"
            Add-Parameter $relationshipCommand '@property' $propertyId ([System.Data.SqlDbType]::BigInt); Add-Parameter $relationshipCommand '@relationship' 'ACRIS Legals reference' ([System.Data.SqlDbType]::NVarChar) 100; Add-Parameter $relationshipCommand '@relationshipKey' $propertyKey ([System.Data.SqlDbType]::NVarChar) 200; Add-Parameter $relationshipCommand '@source' $dataSourceId ([System.Data.SqlDbType]::Int); Add-Parameter $relationshipCommand '@document' $documentId ([System.Data.SqlDbType]::NVarChar) 200; $relationshipCommand.ExecuteNonQuery() | Out-Null
            $accepted++
        }
        $finish = $connection.CreateCommand(); $finish.Transaction = $transaction; $finish.CommandText = "UPDATE dbo.IngestionRuns SET Status='Completed',CompletedUtc=SYSUTCDATETIME(),RowsRead=@read,RowsAccepted=@accepted,RowsRejected=@rejected WHERE IngestionRunID=@run"
        Add-Parameter $finish '@read' ($master.Count + $legals.Count) ([System.Data.SqlDbType]::BigInt); Add-Parameter $finish '@accepted' $accepted ([System.Data.SqlDbType]::BigInt); Add-Parameter $finish '@rejected' $rejected ([System.Data.SqlDbType]::BigInt); Add-Parameter $finish '@run' $runId ([System.Data.SqlDbType]::BigInt); $finish.ExecuteNonQuery()
        $transaction.Commit()
        Write-Host "Imported/upserted $($master.Count) document rows and processed $($legals.Count) matching Legals rows into DataSourceID $dataSourceId, IngestionRunID $runId. $rejected Legals rows were quarantined for missing parcel keys."
    } catch { $transaction.Rollback(); throw } finally { $connection.Dispose() }
}

switch ($Mode) {
    'Download' { Download-Snapshot }
    'Validate' { Validate-Snapshot }
    'Import' { Import-Snapshot }
}
