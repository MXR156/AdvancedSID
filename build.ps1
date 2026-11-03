<#
.SYNOPSIS
    Builds AdvancedSID.exe (single, self-contained file - no .NET install needed on the target).
    Requires the .NET 10 SDK (or newer): https://dotnet.microsoft.com/download/dotnet/10.0
    Output: .\publish\AdvancedSID.exe

.PARAMETER SkipTests
    Skip the unit tests and just build the EXE.
#>
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET SDK is not installed. Get the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0'
    }

    $sdks = & dotnet --list-sdks
    if (-not ($sdks | Where-Object { [int]($_ -split '\.')[0] -ge 10 })) {
        throw "AdvancedSID needs the .NET 10 SDK or newer. Installed SDKs:`n$($sdks -join "`n")`nGet it from https://dotnet.microsoft.com/download/dotnet/10.0"
    }

    if (-not $SkipTests) {
        dotnet test tests/AdvancedSID.Core.Tests -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed (use -SkipTests to build anyway)' }
    }

    dotnet publish src/AdvancedSID/AdvancedSID.csproj -c $Configuration -r win-x64 -o publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

    Write-Host "`nBuilt: $(Resolve-Path publish\AdvancedSID.exe)" -ForegroundColor Green
}
finally {
    Pop-Location
}
