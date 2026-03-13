@echo off
REM Check if postgresql-x64-18 service is running, start if not
sc query postgresql-x64-18 | find "RUNNING" >nul
if errorlevel 1 (
    echo Starting postgresql-x64-18 service...
    net start postgresql-x64-18
)
REM Continue with your original commands
A:
cd PostgreSQL\bin
psql -U postgres