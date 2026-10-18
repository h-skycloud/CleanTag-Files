param([switch]$Register)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation = & $vswhere -latest -products '*' -property installationPath
if (-not $installation) { throw 'Visual Studio with Windows development tools is required.' }
Import-Module (Join-Path $installation 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $installation -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'
$msbuild = Join-Path $installation 'MSBuild\Current\Bin\MSBuild.exe'
& $msbuild -t:Restore src/Files.App.Server/Files.App.Server.csproj -p:Configuration=Debug -p:Platform=x64 -v:quiet -clp:ErrorsOnly
if ($LASTEXITCODE) { throw 'Server restore failed.' }
& $msbuild -restore src/Files.App/Files.App.csproj -p:Configuration=Debug -p:Platform=x64 -p:AppxPackageSigningEnabled=false -v:quiet -clp:ErrorsOnly
if ($LASTEXITCODE) { throw 'Files application build failed.' }
$output = Join-Path $PSScriptRoot 'src/Files.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64'
# A loose registration needs physical assets as well as the generated PRI.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src/Files.App/Assets') -Destination $output -Recurse -Force
$server = Join-Path $PSScriptRoot 'src/Files.App.Server/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/publish/Files.App.Server.exe'
if (-not (Test-Path -LiteralPath $server)) { throw 'Published COM server was not generated.' }
New-Item -ItemType Directory -Force -Path (Join-Path $output 'Files.App.Server') | Out-Null
Copy-Item -LiteralPath $server -Destination (Join-Path $output 'Files.App.Server/Files.App.Server.exe') -Force
if ($Register) {
    $manifest = Join-Path $output 'AppxManifest.xml'
    if (-not (Test-Path -LiteralPath $manifest)) { throw 'Development package layout was not generated.' }
    Add-AppxPackage -Register $manifest
}
