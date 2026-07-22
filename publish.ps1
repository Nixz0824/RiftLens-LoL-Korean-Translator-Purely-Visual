param(
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$outputName = if ($SelfContained) { 'RiftLens-standalone' } else { 'RiftLens-portable' }
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$output = [IO.Path]::GetFullPath((Join-Path $artifactsRoot $outputName))
if (-not $output.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish output is outside the artifacts directory: $output"
}
if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
$arguments = @(
    'publish',
    (Join-Path $projectRoot 'RiftLens\RiftLens.csproj'),
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--output', $output,
    '--nologo',
    '--configfile', (Join-Path $projectRoot 'NuGet.Config'),
    "--self-contained=$($SelfContained.IsPresent.ToString().ToLowerInvariant())"
)
if ($SelfContained) {
    $arguments += '-p:PublishSingleFile=true'
    $arguments += '-p:IncludeNativeLibrariesForSelfExtract=true'
    $arguments += '-p:EnableCompressionInSingleFile=true'
    $arguments += '-p:DebugType=None'
}
& $dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$documents = @('README.md', 'README.en.md', 'LICENSE', 'PRIVACY.md', 'THIRD_PARTY_NOTICES.md', 'SECURITY.md')
foreach ($document in $documents) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination (Join-Path $output $document) -Force
}
$licenseOutput = Join-Path $output 'licenses'
New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'licenses\*') -Destination $licenseOutput -Force
Write-Host "Publish completed: $output"
