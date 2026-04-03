@echo off
echo ================================================
echo GDMENUCardManager - Build Linux x64 Only
echo ================================================
echo.

REM Read version from version.txt
set /p VERSION=<src\version.txt
set OUTPUT_DIR=_releases\GDMENUCardManager.%VERSION%-linux-x64

echo Building version: %VERSION%
echo.

REM Format code
echo Formatting code...
dotnet format src\GDMENUCardManager.sln
if %ERRORLEVEL% neq 0 (
    echo ERROR: Format failed
    pause
    exit /b 1
)
echo.

REM Clean previous build
if exist "%OUTPUT_DIR%" rd /s /q "%OUTPUT_DIR%"
if not exist "_releases" mkdir "_releases"

echo Building AvaloniaUI project for Linux (self-contained)...
echo.

REM Build the AvaloniaUI project (self-contained for Linux)
dotnet publish src\GDMENUCardManager.AvaloniaUI\GDMENUCardManager.AvaloniaUI.csproj -c Release --self-contained true -r linux-x64 -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT_DIR%"

if %ERRORLEVEL% neq 0 (
    echo ERROR: Build failed
    pause
    exit /b 1
)

REM Copy additional files
echo.
echo Copying additional files...

REM Copy tools directory from Core project
xcopy /E /I /Y src\GDMENUCardManager.Core\tools "%OUTPUT_DIR%\tools\"

REM Copy redump2cdi tool for CUE/BIN conversion
copy /Y redump2cdi\linux-x86_64\redump2cdi "%OUTPUT_DIR%\tools\"

REM Copy LICENSE and README
copy /Y LICENSE "%OUTPUT_DIR%\"
copy /Y README.md "%OUTPUT_DIR%\"

echo.
echo ================================================
echo Build completed successfully!
echo ================================================
echo.
echo Output directory: %OUTPUT_DIR%
echo.
echo This build is self-contained and does not require .NET runtime installation.
echo.
echo To run on Linux:
echo   chmod +x %OUTPUT_DIR%/GDMENUCardManager
echo   ./%OUTPUT_DIR%/GDMENUCardManager
echo.
pause
