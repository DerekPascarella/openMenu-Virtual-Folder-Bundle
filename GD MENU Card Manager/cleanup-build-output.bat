@echo off
setlocal

pushd "%~dp0" || exit /b 1

for %%D in (
    "src\GDMENUCardManager.Core\bin"
    "src\GDMENUCardManager.Core\obj"
    "src\GDMENUCardManager\bin"
    "src\GDMENUCardManager\obj"
    "src\GDMENUCardManager.AvaloniaUI\bin"
    "src\GDMENUCardManager.AvaloniaUI\obj"
) do (
    if exist "%%~D" rd /s /q "%%~D"
    if exist "%%~D" (
        echo ERROR: Failed to remove %%~D
        popd
        exit /b 1
    )
)

popd
exit /b 0
