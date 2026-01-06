# Read version from version.txt - use Resolve-Path to handle special characters
$versionFile = Join-Path $PSScriptRoot ".." | Join-Path -ChildPath "version.txt"

if (-not (Test-Path -LiteralPath $versionFile)) {
    Write-Error "Version file not found at: $versionFile"
    exit 1
}

$version = (Get-Content -LiteralPath $versionFile | Out-String).Trim()

# Generate Constants.cs content
$constantsContent = @"
// AUTO-GENERATED FILE - Version is read from ../version.txt during build
// Do not manually edit the Version constant - update ../version.txt instead

namespace GDMENUCardManager.Core
{
    public static class Constants
    {
        public const string NameTextFile = "name.txt";
        public const string SerialTextFile = "serial.txt";
        public const string FolderTextFile = "folder.txt";
        public const string TypeTextFile = "type.txt";
        public const string DiscTextFile = "disc.txt";
        //private const string InfoTextFile = "info.txt";
        public const string MenuConfigTextFile = "GDEMU.ini";
        public const string GdiShrinkBlacklistFile = "gdishrink_blacklist.txt";
        public const string PS1GameDBFile = "gamedb.json";
        public const string DefaultImageFileName = "disc";
        public const string Version = "$version";
    }
}

"@

# Write to Constants.cs
$constantsFile = Join-Path $PSScriptRoot "Constants.cs"
[System.IO.File]::WriteAllText($constantsFile, $constantsContent, [System.Text.Encoding]::UTF8)

Write-Host "Updated version to $version in Constants.cs"
