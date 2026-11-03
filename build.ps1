<#
.SYNOPSIS
    Builds AdvancedSID.exe (single, self-contained file - no .NET install needed on the target).
    Requires the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
    Output: .\publish\AdvancedSID.exe
#>
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet test tests/AdvancedSID.Core.Tests -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }

    dotnet publish src/AdvancedSID/AdvancedSID.csproj -c $Configuration -r win-x64 -o publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

    Write-Host "`nBuilt: $(Resolve-Path publish\AdvancedSID.exe)" -ForegroundColor Green
}
finally {
    Pop-Location
}
