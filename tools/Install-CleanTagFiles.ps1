param([string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs/CleanTagFiles'), [switch]$Launch)
$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$metadata = Get-Content -LiteralPath (Join-Path $packageRoot 'release.json') -Raw | ConvertFrom-Json
if ($metadata.Product -ne 'CleanTag Files') { throw 'Not a CleanTag Files release.' }
if (Get-ChildItem -LiteralPath $packageRoot -Recurse -Attributes ReparsePoint) { throw 'Release must not contain reparse points.' }
$listed = @{}
foreach ($entry in $metadata.Files) {
    $source = [IO.Path]::GetFullPath((Join-Path $packageRoot $entry.Path))
    if (-not $source.StartsWith($packageRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid release path.' }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.SHA256) { throw 'Release integrity check failed.' }
    $listed[$source] = $true
}
foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File -Recurse) {
    if ($file.FullName -ne (Join-Path $packageRoot 'release.json') -and -not $listed.ContainsKey($file.FullName)) { throw 'Release contains an unlisted file.' }
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'app/AppxManifest.xml') -Raw
if ($manifest.Package.Identity.Name -ne 'CleanTag.Files.Experimental' -or $manifest.Package.Identity.Publisher -ne 'CN=CleanTag') { throw 'Unexpected package identity.' }
$developerMode = Get-ItemPropertyValue -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
if ($developerMode -ne 1) { throw 'This unsigned developer distribution requires Windows Developer Mode. The installer does not change Windows security settings.' }
$existing = Get-AppxPackage -Name 'CleanTag.Files.Experimental'
if ($existing) {
    $active = @(Get-Process Files -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($existing.InstallLocation + '\', [StringComparison]::OrdinalIgnoreCase) })
    if ($active.Count) { throw 'Close CleanTag Files before installing.' }
}
$target = [IO.Path]::GetFullPath((Join-Path $InstallDirectory ($metadata.PackageVersion + '-' + $metadata.Commit.Substring(0, 8))))
if ($target.StartsWith($packageRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or $packageRoot.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Install outside the extracted release directory.' }
if (Test-Path -LiteralPath $target) { throw 'This release directory already exists. Keep it for rollback or choose a new installation directory.' }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -LiteralPath (Join-Path $packageRoot 'app') -Destination $target -Recurse
Add-AppxPackage -Register (Join-Path $target 'app/AppxManifest.xml')
$installed = Get-AppxPackage -Name 'CleanTag.Files.Experimental'
if (-not $installed -or $installed.InstallLocation -ne (Join-Path $target 'app')) { throw 'Package registration could not be verified.' }
Write-Host "Installed CleanTag Files $($metadata.Version). Package identity is retained for metadata compatibility."
if ($Launch) { Start-Process explorer.exe -ArgumentList ('shell:AppsFolder\' + $installed.PackageFamilyName + '!App') -WindowStyle Hidden }
