@echo off
echo ================================================
echo GDMENUCardManager Build Script
echo ================================================
echo.

REM Read version from version.txt
set /p VERSION=<src\version.txt

echo Building version: %VERSION%
echo.

REM Format code
echo Formatting code...
dotnet format src\GDMENUCardManager.sln
if %ERRORLEVEL% neq 0 goto :error

REM Normalize XAML line endings (dotnet format only handles C#).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0normalize-xaml.ps1"
if %ERRORLEVEL% neq 0 goto :error
echo.

REM Clean previous builds
echo Cleaning previous builds...
if exist "_releases" rd /s /q "_releases"
mkdir "_releases"

REM Build for Windows x64 (WPF - framework-dependent)
echo.
echo ================================================
echo Building WPF for Windows x64...
echo ================================================
set OUTPUT_DIR=_releases\GDMENUCardManager.%VERSION%-win-x64
dotnet publish src\GDMENUCardManager\GDMENUCardManager.csproj -c Release -o "%OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error
xcopy /E /I /Y src\GDMENUCardManager.Core\tools "%OUTPUT_DIR%\tools\"
REM Never ship user-generated menu options files.
del /Q "%OUTPUT_DIR%\tools\openMenu\menu_data\DEFAULTS.INI" "%OUTPUT_DIR%\tools\openMenu\menu_data\BGM.ADP" 2>nul
copy /Y redump2cdi\windows-x86_64-msvc\redump2cdi.exe "%OUTPUT_DIR%\tools\"
copy /Y LICENSE "%OUTPUT_DIR%\"
copy /Y README.md "%OUTPUT_DIR%\"
cd "%OUTPUT_DIR%" && tar -a -c -f ..\GDMENUCardManager.%VERSION%-win-x64.zip * && cd ..\..
if %ERRORLEVEL% neq 0 echo Warning: Failed to create zip file for win-x64
echo Build completed for win-x64

REM Build for Windows x86 (WPF - framework-dependent)
REM x86 needs an explicit runtime.
echo.
echo ================================================
echo Building WPF for Windows x86...
echo ================================================
set OUTPUT_DIR=_releases\GDMENUCardManager.%VERSION%-win-x86
dotnet publish src\GDMENUCardManager\GDMENUCardManager.csproj -c Release -r win-x86 --self-contained false -o "%OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error
xcopy /E /I /Y src\GDMENUCardManager.Core\tools "%OUTPUT_DIR%\tools\"
REM Never ship user-generated menu options files.
del /Q "%OUTPUT_DIR%\tools\openMenu\menu_data\DEFAULTS.INI" "%OUTPUT_DIR%\tools\openMenu\menu_data\BGM.ADP" 2>nul
copy /Y redump2cdi\windows-x86-msvc\redump2cdi.exe "%OUTPUT_DIR%\tools\"
copy /Y LICENSE "%OUTPUT_DIR%\"
copy /Y README.md "%OUTPUT_DIR%\"
cd "%OUTPUT_DIR%" && tar -a -c -f ..\GDMENUCardManager.%VERSION%-win-x86.zip * && cd ..\..
if %ERRORLEVEL% neq 0 echo Warning: Failed to create zip file for win-x86
echo Build completed for win-x86

REM Purge intermediate build output before cross-platform builds to prevent
REM stale Windows-only native libs from leaking into non-Windows packages.
echo.
echo Cleaning intermediate output...
if exist "src\GDMENUCardManager.Core\bin" rd /s /q "src\GDMENUCardManager.Core\bin"
if exist "src\GDMENUCardManager.Core\obj" rd /s /q "src\GDMENUCardManager.Core\obj"
if exist "src\GDMENUCardManager.AvaloniaUI\bin" rd /s /q "src\GDMENUCardManager.AvaloniaUI\bin"
if exist "src\GDMENUCardManager.AvaloniaUI\obj" rd /s /q "src\GDMENUCardManager.AvaloniaUI\obj"

REM Build for linux-x64 (AvaloniaUI - self-contained)
echo.
echo ================================================
echo Building AvaloniaUI for linux-x64...
echo ================================================
set OUTPUT_DIR=_releases\GDMENUCardManager.%VERSION%-linux-x64
dotnet publish src\GDMENUCardManager.AvaloniaUI\GDMENUCardManager.AvaloniaUI.csproj -c Release --self-contained true -r linux-x64 -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true -o "%OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error
xcopy /E /I /Y src\GDMENUCardManager.Core\tools "%OUTPUT_DIR%\tools\"
REM Never ship user-generated menu options files.
del /Q "%OUTPUT_DIR%\tools\openMenu\menu_data\DEFAULTS.INI" "%OUTPUT_DIR%\tools\openMenu\menu_data\BGM.ADP" 2>nul
copy /Y redump2cdi\linux-x86_64\redump2cdi "%OUTPUT_DIR%\tools\"
copy /Y LICENSE "%OUTPUT_DIR%\"
copy /Y README.md "%OUTPUT_DIR%\"
cd _releases && tar -czf GDMENUCardManager.%VERSION%-linux-x64.tar.gz GDMENUCardManager.%VERSION%-linux-x64 && cd ..
echo Build completed for linux-x64

REM Build for osx-x64 (AvaloniaUI - self-contained)
echo.
echo ================================================
echo Building AvaloniaUI for osx-x64...
echo ================================================
set TEMP_OUTPUT_DIR=_releases\temp-osx-x64
set OUTPUT_DIR=_releases
dotnet publish src\GDMENUCardManager.AvaloniaUI\GDMENUCardManager.AvaloniaUI.csproj -c Release --self-contained true -r osx-x64 -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true -o "%TEMP_OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error
xcopy /E /I /Y src\GDMENUCardManager.Core\tools "%TEMP_OUTPUT_DIR%\tools\"
REM Never ship user-generated menu options files.
del /Q "%TEMP_OUTPUT_DIR%\tools\openMenu\menu_data\DEFAULTS.INI" "%TEMP_OUTPUT_DIR%\tools\openMenu\menu_data\BGM.ADP" 2>nul
copy /Y redump2cdi\macos-x86_64\redump2cdi "%TEMP_OUTPUT_DIR%\tools\"
copy /Y LICENSE "%TEMP_OUTPUT_DIR%\"
copy /Y README.md "%TEMP_OUTPUT_DIR%\"
echo Creating macOS .app bundle...
wsl bash create-macos-bundle.sh "_releases/temp-osx-x64" "%VERSION%" "_releases"
if %ERRORLEVEL% neq 0 goto :error
rd /s /q "%TEMP_OUTPUT_DIR%" 2>nul
echo Build completed for osx-x64

REM Build for osx-arm64 (AvaloniaUI - self-contained)
echo.
echo ================================================
echo Building AvaloniaUI for osx-arm64...
echo ================================================
set TEMP_OUTPUT_DIR=_releases\temp-osx-arm64
set OUTPUT_DIR=_releases
dotnet publish src\GDMENUCardManager.AvaloniaUI\GDMENUCardManager.AvaloniaUI.csproj -c Release --self-contained true -r osx-arm64 -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true -o "%TEMP_OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 goto :error
xcopy /E /I /Y src\GDMENUCardManager.Core\tools "%TEMP_OUTPUT_DIR%\tools\"
REM Never ship user-generated menu options files.
del /Q "%TEMP_OUTPUT_DIR%\tools\openMenu\menu_data\DEFAULTS.INI" "%TEMP_OUTPUT_DIR%\tools\openMenu\menu_data\BGM.ADP" 2>nul
copy /Y redump2cdi\macos-aarch64\redump2cdi "%TEMP_OUTPUT_DIR%\tools\"
copy /Y LICENSE "%TEMP_OUTPUT_DIR%\"
copy /Y README.md "%TEMP_OUTPUT_DIR%\"
echo Creating macOS .app bundle (arm64)...
wsl bash create-macos-bundle.sh "_releases/temp-osx-arm64" "%VERSION%" "_releases" "arm64"
if %ERRORLEVEL% neq 0 goto :error
rd /s /q "%TEMP_OUTPUT_DIR%" 2>nul
echo Build completed for osx-arm64

REM Remove intermediate build output after every successful package.
call cleanup-build-output.bat
if %ERRORLEVEL% neq 0 goto :error

echo.
echo ================================================
echo All builds completed successfully!
echo ================================================
echo.
echo Release files are in the _releases directory:
dir /B _releases\*.zip _releases\*.tar.gz 2>nul
echo.
echo NOTE: Windows builds require .NET 6 Desktop Runtime to be installed.
echo       Linux/macOS builds are self-contained and do not require runtime installation.
echo.
goto :end

:error
echo.
echo ================================================
echo Build failed! See errors above.
echo ================================================
pause
exit /b 1

:end
echo Build process finished.
pause
