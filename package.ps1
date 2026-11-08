param([switch]$PrepareOnly, [switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$artifacts = Join-Path $projectRoot 'artifacts'
$publish = Join-Path $artifacts 'app'
$runtime = Join-Path $artifacts 'python'
$pythonVersion = '3.14.0'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
if (-not (Test-Path (Join-Path $runtime 'python.exe'))) {
    $archive = Join-Path $artifacts "python-$pythonVersion-embed-amd64.zip"
    Invoke-WebRequest "https://www.python.org/ftp/python/$pythonVersion/python-$pythonVersion-embed-amd64.zip" -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $runtime -Force
    $pth = Join-Path $runtime 'python314._pth'
    Set-Content -LiteralPath $pth -Value @('python314.zip', '.', 'Lib\site-packages', 'import site') -Encoding ascii
}
$packages = Join-Path $runtime 'Lib\site-packages'
if (-not (Test-Path (Join-Path $packages 'mijiaAPI'))) {
    $requirements = Join-Path $projectRoot 'backend\requirements.lock.txt'
    if (-not (Test-Path $requirements)) { $requirements = Join-Path $projectRoot 'backend\requirements.txt' }
    & python -m pip install --disable-pip-version-check --only-binary=:all: --platform win_amd64 --python-version 3.14 --implementation cp --abi cp314 --target $packages -r $requirements
    if ($LASTEXITCODE -ne 0) { throw 'Python dependency installation failed.' }
}
if ($PrepareOnly) { Write-Host "Python runtime ready: $runtime"; exit 0 }
# License texts are committed assets. Check their application version before packaging.
$licenseManifest = Join-Path $projectRoot 'src\MiHome.WinUI\Assets\Licenses\index.json'
$componentLicenses = Get-Content -LiteralPath $licenseManifest -Raw -Encoding UTF8 | ConvertFrom-Json
[xml]$appProject = Get-Content -LiteralPath (Join-Path $projectRoot 'src\MiHome.WinUI\MiHome.WinUI.csproj') -Raw
$appLicense = $componentLicenses | Where-Object { $_.name -eq '米家 WinUI' }
if ($appLicense.version -ne $appProject.Project.PropertyGroup.Version) {
    throw 'Regenerate license assets with tools/collect_licenses.py before packaging.'
}
# Publish into an empty output directory so removed assets cannot enter a release.
$publishTarget = [IO.Path]::GetFullPath($publish)
$expectedTarget = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\app'))
if ($publishTarget -ne $expectedTarget) { throw 'Unexpected publish directory.' }
if (Test-Path -LiteralPath $publishTarget) { Remove-Item -LiteralPath $publishTarget -Recurse -Force }
& dotnet publish (Join-Path $projectRoot 'src\MiHome.WinUI\MiHome.WinUI.csproj') -c Release -p:Platform=x64 --self-contained true -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'WinUI build failed.' }
foreach ($resource in @('MiHome.WinUI.pri', 'App.xbf', 'MainWindow.xbf')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $resource))) {
        throw "Required WinUI resource missing from publish output: $resource"
    }
}
New-Item -ItemType Directory -Path (Join-Path $publish 'runtime'), (Join-Path $publish 'backend') -Force | Out-Null
Copy-Item -Path (Join-Path $runtime '*') -Destination (Join-Path $publish 'runtime') -Recurse -Force
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'backend') -File |
    Copy-Item -Destination (Join-Path $publish 'backend') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publish -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $publish -Force
# Debug symbols and pip launchers can embed paths from the build computer.
& python (Join-Path $projectRoot 'tools\prepare_release.py') $publish
if ($LASTEXITCODE -ne 0) { throw 'Release file preparation failed.' }
if (-not $SkipInstaller) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $artifacts 'build-tools\inno-setup6\ISCC.exe')
    )
    $compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $compiler) { throw 'Inno Setup 6 is required to create the installer. Published app is available in artifacts\app.' }
    & $compiler /Qp (Join-Path $projectRoot 'installer\setup.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
}
Write-Host "Output: $artifacts"
