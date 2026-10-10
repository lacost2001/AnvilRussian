$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    python build_translation.py
    if ($LASTEXITCODE -ne 0) { throw 'Translation build failed' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:winexe /out:dist\AnvilRussian.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /resource:dist\translation.xml,translation.xml /resource:Previous.locres,Previous.locres RussianPatcher.cs Updater.cs UpdateTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compiler failed' }
    $testPath = Join-Path $PSScriptRoot 'dist'
    $process = Start-Process -FilePath (Join-Path $testPath 'AnvilRussian.exe') -ArgumentList ('--self-test "' + $testPath + '"') -WindowStyle Hidden -RedirectStandardError (Join-Path $testPath 'test-errors.txt') -Wait -PassThru
    Get-Content (Join-Path $testPath 'test-errors.txt')
    Get-ChildItem $testPath -Filter '*selftest.txt' | ForEach-Object { Get-Content $_.FullName }
    if ($process.ExitCode -ne 0) { throw ('Self-test failed: ' + $process.ExitCode) }
    Copy-Item Roboto-LICENSE.txt dist\Roboto-LICENSE.txt -Force
    Copy-Item NOTICE.txt dist\NOTICE.txt -Force
    Compress-Archive -LiteralPath dist\AnvilRussian.exe,dist\Roboto-LICENSE.txt,dist\NOTICE.txt -DestinationPath dist\AnvilRussian.zip -Force
    Get-FileHash dist\AnvilRussian.exe,dist\translation.xml,dist\AnvilRussian.zip | Format-Table
} finally { Pop-Location }
