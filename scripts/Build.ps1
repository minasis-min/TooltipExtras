param([string]$DevOutputDirectory, [switch]$Package)
. (Join-Path $PSScriptRoot 'Environment.ps1')
$references=Get-Content reference-lock.json -Raw | ConvertFrom-Json
foreach($reference in $references) {
 $path=Join-Path $ProjectRoot ('.dependencies/Dalamud-15.0.3.6-estell.48/'+$reference.File)
 if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $reference.SHA256){throw "Reference mismatch: $($reference.File)"}
}
$origin=Get-Content vendor/MinasisUI/origin.json -Raw | ConvertFrom-Json
foreach($file in $origin.Files.PSObject.Properties) {
 if((Get-FileHash ('vendor/MinasisUI/'+$file.Name)).Hash -ne $file.Value){throw "MinasisUI source mismatch: $($file.Name)"}
}
New-Item -ItemType Directory '.work/logs' -Force | Out-Null
dotnet build src/TooltipExtras -c Release --disable-build-servers -p:UseSharedCompilation=false -p:NuGetAudit=false -v minimal 2>&1 | Tee-Object '.work/logs/build.log'
if($LASTEXITCODE -ne 0){throw 'Build failed'}
dotnet run --project tests/TooltipExtras.Checks -c Release --disable-build-servers -p:UseSharedCompilation=false -p:NuGetAudit=false 2>&1 | Tee-Object '.work/logs/checks.log'
if($LASTEXITCODE -ne 0){throw 'Checks failed'}
$bin=Join-Path $ProjectRoot 'artifacts/bin/TooltipExtras/Release/net10.0-windows'
$bundle=@()
foreach($name in @('TooltipExtras.dll','TooltipExtras.deps.json','TooltipExtras.json','images/icon.png')) {
 $bundle+=@{Source=(Join-Path $bin $name); Relative=$name}
}
foreach($name in @('LICENSE','THIRD_PARTY_NOTICES.md')) {
 $bundle+=@{Source=(Join-Path $ProjectRoot $name); Relative=$name}
}
foreach($name in @('WhichPatchWasThat-MIT.txt','PriceInsight-MIT.txt')) {
 $bundle+=@{Source=(Join-Path $ProjectRoot "docs/licenses/$name"); Relative="licenses/$name"}
}
$bundle+=@{Source=(Join-Path $ProjectRoot 'vendor/MinasisUI/LICENSE'); Relative='licenses/MinasisUI-MIT.txt'}

# Fixed Dev location, matching the PCO workflow. Never touch data/settings files.
if ([string]::IsNullOrWhiteSpace($DevOutputDirectory)) { $DevOutputDirectory = Join-Path $ProjectRoot 'artifacts/plugin' }
$dev = [System.IO.Path]::GetFullPath($DevOutputDirectory)
New-Item -ItemType Directory -Path $dev -Force | Out-Null
foreach ($file in $bundle) {
    $destination = Join-Path $dev $file.Relative
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $file.Source -Destination $destination -Force
}
Write-Output "Development DLL: $(Join-Path $dev 'TooltipExtras.dll')"
if (!$Package) { Write-Output 'Dev-only build: ZIP creation skipped.'; return }

# Distribution packaging is explicit. Its temporary copy never accumulates.
[xml]$project=Get-Content src/TooltipExtras/TooltipExtras.csproj
$version=[string]$project.Project.PropertyGroup.Version
$output=Join-Path $ProjectRoot "artifacts/distribution/$version"
New-Item -ItemType Directory $output -Force | Out-Null
$workRoot=[System.IO.Path]::GetFullPath((Join-Path $ProjectRoot '.work'))
$staging=Join-Path $workRoot ('package-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $staging -ErrorAction Stop | Out-Null
try {
 foreach($file in $bundle) {
  $destination=Join-Path $staging $file.Relative
  New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
  Copy-Item -LiteralPath $file.Source -Destination $destination
 }
 Copy-Item -LiteralPath (Join-Path $staging 'images/icon.png') -Destination (Join-Path $output 'icon.png') -Force
 Compress-Archive -Path (Join-Path $staging '*') -DestinationPath (Join-Path $output 'TooltipExtras.zip') -Force
 Get-FileHash (Join-Path $output 'TooltipExtras.zip') -Algorithm SHA256 | Select-Object Hash | ConvertTo-Json | Set-Content (Join-Path $output 'checksum.json') -Encoding utf8
 $zipHash=(Get-FileHash (Join-Path $output 'TooltipExtras.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
 $iconHash=(Get-FileHash (Join-Path $output 'icon.png') -Algorithm SHA256).Hash.ToLowerInvariant()
 [System.IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'),("$zipHash  TooltipExtras.zip`n$iconHash  icon.png`n"),[System.Text.UTF8Encoding]::new($false))
 Write-Output "Distribution: $output"
}
finally {
 $owned=Get-Item -LiteralPath $staging -ErrorAction Stop
 if([System.IO.Path]::GetDirectoryName($owned.FullName) -ne $workRoot -or $owned.Name -notmatch '^package-[0-9a-f]{32}$' -or ($owned.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
  throw 'Refusing cleanup of an unexpected staging path.'
 }
 Remove-Item -LiteralPath $owned.FullName -Recurse -Force
}
