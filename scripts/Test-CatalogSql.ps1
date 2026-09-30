param([string]$Server = '(localdb)\MSSQLLocalDB')

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$apiDll = Join-Path $repo 'src/Catalog.Api/bin/Debug/net8.0/Catalog.Api.dll'
if (-not (Test-Path -LiteralPath $apiDll)) { throw 'Build Catalog.Api in Debug before running this test.' }
$suffix = [Guid]::NewGuid().ToString('N')
$legacyDb = "CatalogExtractionTest_Legacy_$suffix"
$serviceDb = "CatalogExtractionTest_Service_$suffix"
$oldServiceDb = "CatalogExtractionTest_OldService_$suffix"
$outputDir = Join-Path $repo "artifacts/sql-$suffix"
New-Item -ItemType Directory -Path $outputDir | Out-Null
$snapshot = Join-Path $outputDir 'legacy.json'
$export = Join-Path $outputDir 'service.json'
$apiProcess = $null
$secondProcess = $null
$createdDatabases = [System.Collections.Generic.List[string]]::new()
$savedEnvironment = @{}
foreach ($name in @('ConnectionStrings__LegacyCatalog', 'ConnectionStrings__CatalogDatabase', 'CatalogApi__ApiKey', 'UseOnlyInMemoryDatabase', 'SeedDemoData')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Invoke-TestSql([string]$Database, [string]$Query) {
    & sqlcmd -S $Server -E -b -l 10 -d $Database -Q $Query
    if ($LASTEXITCODE -ne 0) { throw "SQL test operation failed in $Database" }
}

function Start-TestApi([int]$Port, [string]$Label) {
    $process = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @("`"$apiDll`"", '--urls', "http://127.0.0.1:$Port") `
        -WorkingDirectory (Join-Path $repo 'src/Catalog.Api') -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $outputDir "$Label.log") -RedirectStandardError (Join-Path $outputDir "$Label-error.log")
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($process.HasExited) { throw "Catalog exited; inspect $outputDir" }
        try {
            Invoke-RestMethod "http://127.0.0.1:$Port/health" -TimeoutSec 1 | Out-Null
            return $process
        } catch { Start-Sleep -Milliseconds 250 }
    }
    Stop-Process -Id $process.Id
    throw 'Catalog did not become healthy.'
}

try {
    foreach ($database in @($legacyDb, $serviceDb, $oldServiceDb)) {
        Invoke-TestSql 'master' "IF DB_ID(N'$database') IS NOT NULL THROW 50000, 'Test database already exists', 1; CREATE DATABASE [$database];"
        $createdDatabases.Add($database)
    }
    Invoke-TestSql $legacyDb @'
CREATE TABLE CatalogBrands (Id int PRIMARY KEY, Brand nvarchar(100) NOT NULL);
CREATE TABLE CatalogTypes (Id int PRIMARY KEY, Type nvarchar(100) NOT NULL);
CREATE TABLE Catalog (Id int PRIMARY KEY, Name nvarchar(50) NOT NULL, Description nvarchar(max) NOT NULL,
    Price decimal(18,2) NOT NULL, PictureUri nvarchar(max), CatalogBrandId int NOT NULL, CatalogTypeId int NOT NULL);
CREATE SEQUENCE catalog_hilo START WITH 1 INCREMENT BY 10;
CREATE SEQUENCE catalog_brand_hilo START WITH 1 INCREMENT BY 10;
CREATE SEQUENCE catalog_type_hilo START WITH 1 INCREMENT BY 10;
INSERT CatalogBrands VALUES (40, N'Imported brand');
INSERT CatalogTypes VALUES (50, N'Imported type');
INSERT Catalog VALUES (321, N'Existing product', N'Preserve this product', 12.34, N'/images/products/1.png', 40, 50);
'@
    $env:ConnectionStrings__LegacyCatalog = "Server=$Server;Database=$legacyDb;Integrated Security=true;TrustServerCertificate=true"
    $env:ConnectionStrings__CatalogDatabase = "Server=$Server;Database=$serviceDb;Integrated Security=true;TrustServerCertificate=true"
    $env:UseOnlyInMemoryDatabase = 'false'
    $env:SeedDemoData = 'false'
    $env:CatalogApi__ApiKey = [Guid]::NewGuid().ToString('N')
    & dotnet $apiDll --export-legacy $snapshot
    if ($LASTEXITCODE -ne 0) { throw 'Legacy export failed.' }
    # Rehearse upgrading an old EnsureCreated database without altering it in place.
    Invoke-TestSql $oldServiceDb @'
CREATE TABLE Brands (Id int PRIMARY KEY, Name nvarchar(max) NOT NULL);
CREATE TABLE Types (Id int PRIMARY KEY, Name nvarchar(max) NOT NULL);
CREATE TABLE Items (Id int PRIMARY KEY, Name nvarchar(50) NOT NULL, Description nvarchar(max) NOT NULL,
    Price decimal(18,2) NOT NULL, PictureUri nvarchar(max) NOT NULL, CatalogBrandId int NOT NULL, CatalogTypeId int NOT NULL);
INSERT Brands VALUES (40, N'Imported brand');
INSERT Types VALUES (50, N'Imported type');
INSERT Items VALUES (321, N'Existing product', N'Preserve this product', 12.34, N'/images/products/1.png', 40, 50);
'@
    $newConnection = $env:ConnectionStrings__CatalogDatabase
    $env:ConnectionStrings__CatalogDatabase = "Server=$Server;Database=$oldServiceDb;Integrated Security=true;TrustServerCertificate=true"
    & dotnet $apiDll --migrate 2>&1 | Out-File (Join-Path $outputDir 'expected-old-schema.log')
    if ($LASTEXITCODE -eq 0) { throw 'Old schema was silently adopted.' }
    $oldExport = Join-Path $outputDir 'old-service.json'
    & dotnet $apiDll --export $oldExport
    if ($LASTEXITCODE -ne 0) { throw 'Old service export failed.' }
    if ((Get-Content -Raw $snapshot) -cne (Get-Content -Raw $oldExport)) { throw 'Old schema data was changed.' }
    $env:ConnectionStrings__CatalogDatabase = $newConnection
    & dotnet $apiDll --import $snapshot
    if ($LASTEXITCODE -ne 0) { throw 'Catalog import failed.' }
    & dotnet $apiDll --export $export
    if ($LASTEXITCODE -ne 0) { throw 'Catalog export failed.' }
    if ((Get-Content -Raw $snapshot) -cne (Get-Content -Raw $export)) { throw 'Imported catalog differs from exported legacy data.' }

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $apiProcess = Start-TestApi $port 'first-start'
    $headers = @{ 'X-Catalog-Key' = $env:CatalogApi__ApiKey }
    $item = Invoke-RestMethod "http://127.0.0.1:$port/catalog/items/321" -Headers $headers
    if ($item.price -ne 12.34) { throw 'Imported product price is incorrect.' }
    $body = @{ name = 'Created after import'; description = 'Persistence check'; price = 20; catalogBrandId = 40; catalogTypeId = 50 } | ConvertTo-Json
    $headers['Idempotency-Key'] = 'sql-restart-test'
    $created = Invoke-RestMethod "http://127.0.0.1:$port/catalog/items" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
    if ($created.id -le 321) { throw 'SQL identity was not advanced past the imported IDs.' }
    Stop-Process -Id $apiProcess.Id
    $apiProcess.WaitForExit()
    $apiProcess = Start-TestApi $port 'second-start'
    $persisted = Invoke-RestMethod "http://127.0.0.1:$port/catalog/items/$($created.id)" -Headers $headers
    if ($persisted.name -ne 'Created after import') { throw 'Catalog data did not survive restart.' }
    $replayed = Invoke-RestMethod "http://127.0.0.1:$port/catalog/items" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
    if ($replayed.id -ne $created.id) { throw 'Idempotency result did not survive restart.' }
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $secondPort = $listener.LocalEndpoint.Port
    $listener.Stop()
    $secondProcess = Start-TestApi $secondPort 'concurrent-instance'
    $client = [Net.Http.HttpClient]::new()
    try {
        foreach ($scenario in @('same-key', 'duplicate-name')) {
            $tasks = @()
            $messages = @()
            foreach ($targetPort in @($port, $secondPort)) {
                $message = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post, "http://127.0.0.1:$targetPort/catalog/items")
                $message.Headers.Add('X-Catalog-Key', $env:CatalogApi__ApiKey)
                $requestKey = if ($scenario -eq 'same-key') { 'concurrent-key' } else { [Guid]::NewGuid().ToString('N') }
                $message.Headers.Add('Idempotency-Key', $requestKey)
                $concurrentBody = @{ name = "Concurrent $scenario"; description = 'Concurrency test'; price = 10; catalogBrandId = 40; catalogTypeId = 50 } | ConvertTo-Json
                $message.Content = [Net.Http.StringContent]::new($concurrentBody, [Text.Encoding]::UTF8, 'application/json')
                $messages += $message
                $tasks += $client.SendAsync($message)
            }
            $responses = @($tasks | ForEach-Object { $_.GetAwaiter().GetResult() })
            try {
                $codes = @($responses | ForEach-Object { [int]$_.StatusCode } | Sort-Object)
                if ($scenario -eq 'same-key') {
                    if (($codes -join ',') -ne '201,201') { throw "Same-key concurrency failed: $codes" }
                    $ids = @($responses | ForEach-Object { ($_.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json).id } | Select-Object -Unique)
                    if ($ids.Count -ne 1) { throw 'Same key created multiple products.' }
                } elseif (($codes -join ',') -ne '201,409') { throw "Database uniqueness failed: $codes" }
            } finally {
                $responses | ForEach-Object { $_.Dispose() }
                $messages | ForEach-Object { $_.Dispose() }
            }
        }
    } finally { $client.Dispose() }
    # Include an update and deletion in the rollback rehearsal.
    $headers.Remove('Idempotency-Key')
    $updatedBody = @{ name = 'Updated before rollback'; description = 'Rollback update'; price = 25; catalogBrandId = 40; catalogTypeId = 50 } | ConvertTo-Json
    Invoke-RestMethod "http://127.0.0.1:$port/catalog/items/$($created.id)" -Method Put -Headers $headers -ContentType 'application/json' -Body $updatedBody | Out-Null
    Invoke-RestMethod "http://127.0.0.1:$port/catalog/items/321" -Method Delete -Headers $headers | Out-Null
    Stop-Process -Id $apiProcess.Id
    $apiProcess.WaitForExit()
    Stop-Process -Id $secondProcess.Id
    $secondProcess.WaitForExit()
    & dotnet $apiDll --verify-legacy (Join-Path $outputDir 'must-not-match.json') 2>&1 | Out-File (Join-Path $outputDir 'expected-mismatch.log')
    if ($LASTEXITCODE -eq 0) { throw 'Rollback verification accepted stale legacy data.' }
    Invoke-TestSql $legacyDb 'ALTER TABLE Catalog ADD CONSTRAINT TestPriceLimit CHECK (Price <= 20);'
    & dotnet $apiDll --restore-legacy (Join-Path $outputDir 'failed-rollback-backup.json') 2>&1 | Out-File (Join-Path $outputDir 'expected-rollback-failure.log')
    if ($LASTEXITCODE -eq 0) { throw 'Rollback should fail when the target rejects data.' }
    $afterFailure = Join-Path $outputDir 'legacy-after-failure.json'
    & dotnet $apiDll --export-legacy $afterFailure
    if ($LASTEXITCODE -ne 0) { throw 'Failed to verify rollback atomicity.' }
    if ((Get-Content -Raw $snapshot) -cne (Get-Content -Raw $afterFailure)) { throw 'Failed rollback partially changed the legacy database.' }
    Invoke-TestSql $legacyDb 'ALTER TABLE Catalog DROP CONSTRAINT TestPriceLimit;'
    & dotnet $apiDll --restore-legacy (Join-Path $outputDir 'legacy-backup.json')
    if ($LASTEXITCODE -ne 0) { throw 'Rollback reconciliation failed.' }
    & dotnet $apiDll --verify-legacy (Join-Path $outputDir 'verified-rollback.json')
    if ($LASTEXITCODE -ne 0) { throw 'Reconciled data differs.' }
    Invoke-TestSql $legacyDb @'
DECLARE @item bigint = NEXT VALUE FOR catalog_hilo;
DECLARE @brand bigint = NEXT VALUE FOR catalog_brand_hilo;
DECLARE @type bigint = NEXT VALUE FOR catalog_type_hilo;
IF @item <= (SELECT MAX(Id) FROM Catalog) OR @brand <= (SELECT MAX(Id) FROM CatalogBrands) OR @type <= (SELECT MAX(Id) FROM CatalogTypes)
    THROW 50000, 'HiLo allocation overlaps restored IDs', 1;
'@
    Invoke-TestSql $serviceDb "IF (SELECT COUNT(*) FROM __EFMigrationsHistory)=0 THROW 50000, 'Migrations missing', 1;"
    Write-Output 'PASS: migrations, ID-preserving import, persistent idempotency, cross-instance concurrency, uniqueness and verified rollback.'
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id; $apiProcess.WaitForExit() }
    if ($null -ne $secondProcess -and -not $secondProcess.HasExited) { Stop-Process -Id $secondProcess.Id; $secondProcess.WaitForExit() }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    foreach ($database in $createdDatabases) {
        if ($database -notmatch '^CatalogExtractionTest_(Legacy|Service|OldService)_[a-f0-9]{32}$') { throw 'Unexpected test database name; refusing cleanup.' }
        Invoke-TestSql 'master' "IF DB_ID(N'$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END"
    }
}
