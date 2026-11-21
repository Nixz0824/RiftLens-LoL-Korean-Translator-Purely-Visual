$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$systemDotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if ($systemDotnet) { $systemDotnet } elseif (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet build (Join-Path $projectRoot 'RiftLens\RiftLens.csproj') --nologo --configfile (Join-Path $projectRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build (Join-Path $projectRoot 'RiftLens.SelfTest\RiftLens.SelfTest.csproj') --nologo --configfile (Join-Path $projectRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet run --project (Join-Path $projectRoot 'RiftLens.SelfTest\RiftLens.SelfTest.csproj') --no-build --no-restore
exit $LASTEXITCODE
