@echo off
setlocal

set CONTAINER=openlethe-db-1
set DBUSER=openlethe
set DBNAME=openlethe
set ACCOUNT_ID=b4764405-3878-4607-896d-d95fcbfe4e2a
set OUTPUT=RailwaySaveInfo.json

echo Exporting RailwaySaveInfo...

(
    echo SELECT "RailwaySaveInfo"::text
    echo FROM public.accounts
    echo WHERE "Id" = '%ACCOUNT_ID%';
) > "%TEMP%\openlethe_query.sql"

docker exec -i -e PGPASSWORD=openlethe %CONTAINER% ^
    psql -U %DBUSER% -d %DBNAME% -t -A < "%TEMP%\openlethe_query.sql" > "%OUTPUT%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ERROR: Export failed.
    pause
    exit /b 1
)

echo.
echo Export complete:
echo %CD%\%OUTPUT%
echo.
echo Open it with VS Code:
echo code "%OUTPUT%"
echo.

pause