$ErrorActionPreference = "Stop"

$AccountId = "b4764405-3878-4607-896d-d95fcbfe4e2a"
$JsonFile = Join-Path $PSScriptRoot "RailwaySaveInfo.json"
$BackupDir = Join-Path $PSScriptRoot "backups"

Write-Host "=========================================="
Write-Host "      OpenLethe RailwaySaveInfo Import"
Write-Host "=========================================="
Write-Host ""

# -------------------------------------------------
# Check JSON exists
# -------------------------------------------------

if (-not (Test-Path -LiteralPath $JsonFile)) {
    Write-Host "ERROR: RailwaySaveInfo.json was not found."
    Read-Host "Press Enter to exit"
    exit 1
}

# -------------------------------------------------
# Validate JSON
# -------------------------------------------------

Write-Host "Validating JSON..."

try {
    $JsonText = Get-Content -LiteralPath $JsonFile -Raw
    $null = $JsonText | ConvertFrom-Json
    Write-Host "JSON is valid."
}
catch {
    Write-Host ""
    Write-Host "ERROR: RailwaySaveInfo.json is invalid."
    Write-Host $_.Exception.Message
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""

# -------------------------------------------------
# Create backup directory
# -------------------------------------------------

if (-not (Test-Path -LiteralPath $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir | Out-Null
}

# -------------------------------------------------
# Backup current DB value
# -------------------------------------------------

$Timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$BackupFile = Join-Path $BackupDir "RailwaySaveInfo_$Timestamp.json"

Write-Host "Creating database backup..."

$BackupSql = @"
SELECT "RailwaySaveInfo"::text
FROM public.accounts
WHERE "Id" = '$AccountId';
"@

$BackupResult = $BackupSql | docker compose exec -T db psql -U openlethe -d openlethe -t -A

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Could not create database backup."
    Read-Host "Press Enter to exit"
    exit 1
}

$BackupResult | Set-Content -LiteralPath $BackupFile -Encoding UTF8

Write-Host "Backup created:"
Write-Host $BackupFile
Write-Host ""

# -------------------------------------------------
# Confirmation
# -------------------------------------------------

Write-Host "WARNING:"
Write-Host "This will replace the account's RailwaySaveInfo"
Write-Host "with the contents of:"
Write-Host ""
Write-Host "    $JsonFile"
Write-Host ""
Write-Host "Your current database value has been backed up."
Write-Host ""

$Confirm = Read-Host "Continue with database update? [Y/N]"

if ($Confirm -notmatch "^[Yy]$") {
    Write-Host ""
    Write-Host "Import cancelled."
    Write-Host "Database was NOT changed."
    Read-Host "Press Enter to exit"
    exit 0
}

Write-Host ""
Write-Host "Importing JSON..."

# -------------------------------------------------
# Copy JSON into PostgreSQL container
# -------------------------------------------------

docker cp $JsonFile "openlethe-db-1:/tmp/RailwaySaveInfo.json"

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Could not copy JSON into PostgreSQL container."
    Write-Host "Database was NOT changed."
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "JSON copied successfully."

# -------------------------------------------------
# Perform UPDATE
# -------------------------------------------------

$ImportSql = @"
UPDATE public.accounts
SET "RailwaySaveInfo" = pg_read_file('/tmp/RailwaySaveInfo.json')::jsonb
WHERE "Id" = '$AccountId';
"@

$ImportResult = $ImportSql | docker compose exec -T db psql -U openlethe -d openlethe -v ON_ERROR_STOP=1

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "=========================================="
    Write-Host "ERROR: DATABASE UPDATE FAILED"
    Write-Host "=========================================="
    Write-Host ""
    Write-Host "Your original data is still available at:"
    Write-Host $BackupFile
    Write-Host ""
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "Database update completed:"
$ImportResult

# -------------------------------------------------
# Verify
# -------------------------------------------------

Write-Host ""
Write-Host "Verifying database..."

$VerifySql = @"
SELECT jsonb_typeof("RailwaySaveInfo")
FROM public.accounts
WHERE "Id" = '$AccountId';
"@

$VerifyResult = $VerifySql | docker compose exec -T db psql -U openlethe -d openlethe -t -A

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "WARNING: Verification failed."
    Write-Host "Backup:"
    Write-Host $BackupFile
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Stored JSON type: $VerifyResult"

if ($VerifyResult.Trim() -ne "object") {
    Write-Host ""
    Write-Host "WARNING: RailwaySaveInfo is not a JSON object."
    Write-Host "Your backup is still available at:"
    Write-Host $BackupFile
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host ""
Write-Host "=========================================="
Write-Host "Import completed successfully."
Write-Host "=========================================="
Write-Host ""
Write-Host "Backup:"
Write-Host $BackupFile
Write-Host ""

Read-Host "Press Enter to exit"