param([string]$Server = '(localdb)\MSSQLLocalDB')

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$apiDll = Join-Path $repo 'src/Catalog.Api/bin/Debug/net8.0/Catalog.Api.dll'
if (-not (Test-Path -LiteralPath $apiDll)) { throw 'Build Catalog.Api in Debug before running this test.' }
$suffix = [Guid]::NewGuid().ToString('N')
$legacyDb = "CatalogExtractionTest_Legacy_$suffix"
$serviceDb = "CatalogExtractionTest_Service_$suffix"
$outputDir = Join-Path $repo "artifacts/sql-$suffix"
New-Item -ItemType Directory -Path $outputDir | Out-Null
$snapshot = Join-Path $outputDir 'legacy.json'
$export = Join-Path $outputDir 'service.json'
$apiProcess = $null
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
    foreach ($database in @($legacyDb, $serviceDb)) {
        Invoke-TestSql 'master' "IF DB_ID(N'$database') IS NOT NULL THROW 50000, 'Test database already exists', 1; CREATE DATABASE [$database];"
        $createdDatabases.Add($database)
    }
    Invoke-TestSql $legacyDb @'
CREATE TABLE CatalogBrands (Id int PRIMARY KEY, Brand nvarchar(100) NOT NULL);
CREATE TABLE CatalogTypes (Id int PRIMARY KEY, Type nvarchar(100) NOT NULL);
CREATE TABLE Catalog (Id int PRIMARY KEY, Name nvarchar(50) NOT NULL, Description nvarchar(max) NOT NULL,
    Price decimal(18,2) NOT NULL, PictureUri nvarchar(max), CatalogBrandId int NOT NULL, CatalogTypeId int NOT NULL);
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
    $created = Invoke-RestMethod "http://127.0.0.1:$port/catalog/items" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
    if ($created.id -le 321) { throw 'SQL identity was not advanced past the imported IDs.' }
    Stop-Process -Id $apiProcess.Id
    $apiProcess.WaitForExit()
    $apiProcess = Start-TestApi $port 'second-start'
    $persisted = Invoke-RestMethod "http://127.0.0.1:$port/catalog/items/$($created.id)" -Headers $headers
    if ($persisted.name -ne 'Created after import') { throw 'Catalog data did not survive restart.' }
    Write-Output 'PASS: SQL export/import preserved IDs and data; API writes used new IDs and survived process restart.'
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id; $apiProcess.WaitForExit() }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    foreach ($database in $createdDatabases) {
        if ($database -notmatch '^CatalogExtractionTest_(Legacy|Service)_[a-f0-9]{32}$') { throw 'Unexpected test database name; refusing cleanup.' }
        Invoke-TestSql 'master' "IF DB_ID(N'$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END"
    }
}
