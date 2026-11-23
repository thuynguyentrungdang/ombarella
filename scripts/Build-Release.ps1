# Builds both mods in Release, stages the zips + SHA256SUMS.txt, optionally tags/pushes/attaches to the draft release.
# Needs the local game install (see ombarella.csproj GameDir).
# Usage: pwsh scripts/Build-Release.ps1 [-GameDir 'F:\SPT_4.1\'] [-Publish]
#   -Publish: requires clean tree + gh CLI. Tags v<ModVersion>, pushes tag (CI creates DRAFT release from CHANGELOG),
#             waits for the draft, uploads zips + checksums. Leaves it a draft: publish by hand on GitHub.
param([string]$GameDir, [string]$StagingRoot = 'D:\Staging Area\Ombarella', [switch]$Publish)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $ver = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.ModVersion | Where-Object { $_ } | Select-Object -First 1
    $tag = "v$ver"
    if ($Publish) {
        if (git status --porcelain) { throw 'working tree not clean: commit first (zips must match the tagged commit)' }
        if (-not (Select-String -Path CHANGELOG.md -SimpleMatch "## [$tag]" -Quiet)) { throw "CHANGELOG.md has no '## [$tag]' entry" }
        if (git tag -l $tag) { throw "tag $tag already exists" }
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh CLI not found' }
    }

    dotnet test tests/Ombarella.Tests -c Release -nologo -v:q
    if ($LASTEXITCODE) { throw 'tests failed' }

    $args = @('build', 'ombarella.sln', '-c', 'Release', '-nologo', '-v:q', '-clp:ErrorsOnly')
    if ($GameDir) { $args += "-p:GameDir=$GameDir" }
    dotnet @args
    if ($LASTEXITCODE) { throw 'build failed' }

    $zips = 'Ombarella.zip', 'Ombarella.Fika.zip' | ForEach-Object { Join-Path $root "bin\Release\$_" }
    foreach ($p in $zips) {
        if (-not (Test-Path $p)) { throw "missing $p" }
        $f = Get-Item $p
        '{0}  {1:N0} KB' -f $f.FullName, ($f.Length / 1KB)
    }
    $sums = Join-Path $root 'bin\Release\SHA256SUMS.txt'
    ($zips | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower(), (Split-Path $_ -Leaf) }) | Set-Content $sums -Encoding ascii
    New-Item -ItemType Directory -Force $StagingRoot | Out-Null
    foreach ($p in $zips + $sums) { Copy-Item $p $StagingRoot -Force }
    "Zips + SHA256SUMS.txt copied to $StagingRoot"

    if (-not $Publish) {
        "Version $ver. Next: rerun with -Publish, or: git tag $tag; git push origin $tag; attach zips to the draft release."
        return
    }
    git tag $tag
    # PS 5.1 turns git's stderr progress into a terminating error under 'Stop'; judge by exit code instead
    $ErrorActionPreference = 'Continue'
    git push origin $tag 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git push origin $tag failed" }
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE) { throw 'tag push failed' }
    $found = $false
    for ($i = 0; $i -lt 24 -and -not $found; $i++) {
        Start-Sleep 10
        gh release view $tag --json isDraft *> $null
        $found = ($LASTEXITCODE -eq 0)
    }
    if (-not $found) { throw "draft release $tag not created by CI after 4 min: check the Actions tab, then: gh release upload $tag bin\Release\*.zip --clobber" }
    gh release upload $tag @zips $sums --clobber
    if ($LASTEXITCODE) { throw 'upload failed' }
    "Uploaded to draft $tag. Review + publish on GitHub."
}
finally { Pop-Location }
