@echo off

cd /d "%~dp0Server"

echo.
echo ================================================
echo PUBLISHING SERVER
echo ================================================
echo.

dotnet publish

echo.
echo ================================================
echo REMOVING UNNECESSARY FILES
echo ================================================
echo.

cd bin\Release\net8.0\publish

REM Remove debug symbols
del /q Server.pdb
del /q *.pdb

REM Remove Windows exe
del /q Server.exe

REM Remove CodeAnalysis compilation tools (6MB+)
del /q Microsoft.CodeAnalysis*.dll
del /q Humanizer.dll
del /q Mono.TextTemplating.dll
del /q System.CodeDom.dll
del /q System.Composition*.dll

REM Remove EntityFramework Design (migrations tooling)
del /q Microsoft.EntityFrameworkCore.Design.dll
del /q Microsoft.Extensions.DependencyModel.dll

REM Remove Swagger UI (3MB)
del /q Swashbuckle.AspNetCore.SwaggerUI.dll

REM Remove localization folders (language resource folders)
for /d %%D in (cs de es fr it ja ko pl pt-BR ru tr zh-Hans zh-Hant) do (
    if exist "%%D" rmdir /s /q "%%D"
)

REM Remove old leftovers
if exist bin rmdir /s /q bin
if exist publish rmdir /s /q publish
del /q web.config

echo.
echo ================================================
echo UPLOADING OPTIMIZED BUILD TO EC2
echo ================================================
echo.

cd /d "%~dp0Server"

echo Securing private key permissions...
set "KEY_PATH=%~dp0Server-Key.pem"

if not exist "%KEY_PATH%" (
    echo ERROR: Key file not found at "%KEY_PATH%"
    goto :end
)

REM Restrict key ACL so OpenSSH accepts it on Windows
icacls "%KEY_PATH%" /inheritance:r >nul
icacls "%KEY_PATH%" /remove:g "Users" "Authenticated Users" "Everyone" >nul 2>&1
icacls "%KEY_PATH%" /grant:r "%USERNAME%:R" >nul

scp -i "%~dp0Server-Key.pem" -r bin\Release\net8.0\publish\* ec2-user@34.238.14.248:/home/ec2-user/serverSpace

echo.
echo ================================================
echo DEPLOYMENT COMPLETE
echo ================================================
echo.

:end
pause
