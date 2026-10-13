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
[xml]$project=Get-Content src/TooltipExtras/TooltipExtras.csproj
$version=[string]$project.Project.PropertyGroup.Version
$output=Join-Path $ProjectRoot "artifacts/distribution/$version"
New-Item -ItemType Directory $output -Force | Out-Null
$staging=Join-Path $ProjectRoot ('.work/package-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $staging -Force | Out-Null
$bin=Join-Path $ProjectRoot 'artifacts/bin/TooltipExtras/Release/net10.0-windows'
foreach($name in @('TooltipExtras.dll','TooltipExtras.deps.json','TooltipExtras.json')){Copy-Item -LiteralPath (Join-Path $bin $name) -Destination $staging}
New-Item -ItemType Directory (Join-Path $staging 'images') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $bin 'images/icon.png') -Destination (Join-Path $staging 'images/icon.png')
foreach($name in @('LICENSE','THIRD_PARTY_NOTICES.md')){Copy-Item -LiteralPath (Join-Path $ProjectRoot $name) -Destination $staging}
New-Item -ItemType Directory (Join-Path $staging 'licenses') -Force | Out-Null
foreach($name in @('WhichPatchWasThat-MIT.txt','PriceInsight-MIT.txt')){Copy-Item -LiteralPath (Join-Path $ProjectRoot "docs/licenses/$name") -Destination (Join-Path $staging 'licenses')}
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'vendor/MinasisUI/LICENSE') -Destination (Join-Path $staging 'licenses/MinasisUI-MIT.txt')
Copy-Item -LiteralPath (Join-Path $staging 'images/icon.png') -Destination (Join-Path $output 'icon.png') -Force
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath (Join-Path $output 'TooltipExtras.zip') -Force
Get-FileHash (Join-Path $output 'TooltipExtras.zip') -Algorithm SHA256 | Select-Object Hash | ConvertTo-Json | Set-Content (Join-Path $output 'checksum.json') -Encoding utf8
$zipHash=(Get-FileHash (Join-Path $output 'TooltipExtras.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
$iconHash=(Get-FileHash (Join-Path $output 'icon.png') -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'),("$zipHash  TooltipExtras.zip`n$iconHash  icon.png`n"),[System.Text.UTF8Encoding]::new($false))
Write-Output "Distribution: $output"
