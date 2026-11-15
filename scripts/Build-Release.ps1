# Builds both mods in Release and checks the zips. Needs the local game install (see ombarella.csproj GameDir).
# Usage: pwsh scripts/Build-Release.ps1 [-GameDir 'F:\SPT_4.1\']
param([string]$GameDir, [string]$StagingRoot = 'D:\Staging Area\Ombarella')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet test tests/Ombarella.Tests -c Release -nologo -v:q
    if ($LASTEXITCODE) { throw 'tests failed' }

    $args = @('build', 'ombarella.sln', '-c', 'Release', '-nologo', '-v:q', '-clp:ErrorsOnly')
    if ($GameDir) { $args += "-p:GameDir=$GameDir" }
    dotnet @args
    if ($LASTEXITCODE) { throw 'build failed' }

    $ver = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.ModVersion | Where-Object { $_ } | Select-Object -First 1
    foreach ($z in 'Ombarella.zip', 'Ombarella.Fika.zip') {
        $p = Join-Path $root "bin\Release\$z"
        if (-not (Test-Path $p)) { throw "missing $p" }
        $f = Get-Item $p
        '{0}  {1:N0} KB' -f $f.FullName, ($f.Length / 1KB)
    }
    New-Item -ItemType Directory -Force $StagingRoot | Out-Null
    foreach ($z in 'Ombarella.zip', 'Ombarella.Fika.zip') { Copy-Item (Join-Path $root "bin\Release\$z") $StagingRoot -Force }
    "Zips copied to $StagingRoot"
    "Version $ver. Next: git tag v$ver; git push origin v$ver; attach both zips to the draft release."
}
finally { Pop-Location }
