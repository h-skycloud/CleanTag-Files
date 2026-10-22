param([string]$Version = '0.3.0-preview.2', [string]$Destination = (Join-Path $PSScriptRoot '../artifacts/release'))
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $repository 'src/Files.App/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64'
if (-not (Test-Path -LiteralPath (Join-Path $output 'AppxManifest.xml'))) { throw 'Build Release with build-cleantag.ps1 first.' }
$commit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE) { throw 'Git revision unavailable.' }
$root = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $root) { throw 'Choose a new release directory.' }
New-Item -ItemType Directory -Path $root | Out-Null
$app = Join-Path $root 'app'
New-Item -ItemType Directory -Path $app | Out-Null
foreach ($child in Get-ChildItem -LiteralPath $output) { Copy-Item -LiteralPath $child.FullName -Destination $app -Recurse }
foreach ($file in Get-ChildItem -LiteralPath $app -File -Recurse) {
    if ($file.Extension -in @('.pdb', '.iobj', '.ipdb', '.exp', '.lib', '.log', '.appxrecipe')) { Remove-Item -LiteralPath $file.FullName }
}
foreach ($name in @('README.md', 'README.upstream.md', 'LICENSE-MIT', 'LICENSE-MPL', 'CLEANTAG-INTEGRATION.md', 'CLEANTAG-GEZINTI.md')) { Copy-Item -LiteralPath (Join-Path $repository $name) -Destination $root }
Copy-Item -LiteralPath (Join-Path $repository 'src/CleanTag.Analysis/LICENSE') -Destination (Join-Path $root 'LICENSE-CleanTag-MIT.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-CleanTagFiles.ps1') -Destination $root
$hashes = @(Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object {
    @{ Path = [IO.Path]::GetRelativePath($root, $_.FullName); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
[xml]$manifest = Get-Content -LiteralPath (Join-Path $app 'AppxManifest.xml') -Raw
@{ Product = 'CleanTag Files'; Version = $Version; Commit = $commit; PackageVersion = $manifest.Package.Identity.Version; Distribution = 'Unsigned developer layout'; Files = $hashes } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'release.json') -Encoding utf8
$zip = Join-Path (Split-Path -Parent $root) "CleanTag-Files-$Version-windows-x64.zip"
if (Test-Path -LiteralPath $zip) { throw 'Release archive already exists.' }
Compress-Archive -LiteralPath @((Get-ChildItem -LiteralPath $root).FullName) -DestinationPath $zip -CompressionLevel Optimal
(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($zip) | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Host $zip
