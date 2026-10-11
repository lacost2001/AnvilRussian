param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $config = Get-Content release-config.json -Raw | ConvertFrom-Json
    $feedUrl = 'https://github.com/lacost2001/AnvilRussian/releases/latest/download/translation.xml'
    try { $response = Invoke-WebRequest -Uri $feedUrl -UseBasicParsing; [xml]$prior = $response.Content }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { $prior = $null }
        else { throw }
    }
    if ($prior) {
        if ([version]$config.version -le [version]$prior.TranslationUpdate.version) { throw 'Increase release-config.json version before publishing.' }
        $hashes = @($config.previousLocresHashes) + @($prior.TranslationUpdate.PreviousLocres | ForEach-Object { $_.sha256 }) + @($prior.TranslationUpdate.File | Where-Object name -eq 'Game.locres' | ForEach-Object { $_.sha256 })
        $config.previousLocresHashes = @($hashes | Sort-Object -Unique)
        $config | ConvertTo-Json -Depth 10 | Set-Content release-config.json -Encoding UTF8
    }
    & .\build.ps1
    if (-not $BuildOnly) {
        gh release create "v$($config.version)" dist/AnvilRussian.zip dist/AnvilRussian.exe dist/translation.xml dist/Roboto-LICENSE.txt dist/NOTICE.txt --repo lacost2001/AnvilRussian --title "Anvil Russian $($config.version) — RikiMarci" --generate-notes --latest
        if ($LASTEXITCODE -ne 0) { throw 'GitHub release failed' }
    }
} finally { Pop-Location }
