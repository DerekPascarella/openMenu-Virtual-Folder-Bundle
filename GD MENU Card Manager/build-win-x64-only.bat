@echo off
echo ================================================
echo GDMENUCardManager - Build Windows x64 Only
echo ================================================
echo.

REM Read version from version.txt
set /p VERSION=<src\version.txt
set OUTPUT_DIR=_releases\GDMENUCardManager.%VERSION%-win-x64

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

REM Normalize XAML line endings (dotnet format only handles C#).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0normalize-xaml.ps1"
if %ERRORLEVEL% neq 0 (
    echo ERROR: XAML normalization failed
    pause
    exit /b 1
)
echo.

REM Clean previous build
if exist "%OUTPUT_DIR%" rd /s /q "%OUTPUT_DIR%"
if not exist "_releases" mkdir "_releases"

echo Building WPF project for Windows (framework-dependent)...
echo.

REM Build the WPF project (framework-dependent, not self-contained)
dotnet publish src\GDMENUCardManager\GDMENUCardManager.csproj -c Release -o "%OUTPUT_DIR%"

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
REM Never ship user-generated menu options files.
del /Q "%OUTPUT_DIR%\tools\openMenu\menu_data\DEFAULTS.INI" "%OUTPUT_DIR%\tools\openMenu\menu_data\BGM.ADP" 2>nul

REM Copy redump2cdi tool for CUE/BIN conversion
copy /Y redump2cdi\windows-x86_64-msvc\redump2cdi.exe "%OUTPUT_DIR%\tools\"

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
echo IMPORTANT: This build requires .NET 6 Desktop Runtime to be installed.
echo.
echo You can now run the application:
echo %OUTPUT_DIR%\GDMENUCardManager.exe
echo.
pause
