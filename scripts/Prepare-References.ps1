param([Parameter(Mandatory=$true)][string]$DalamudDirectory)
. (Join-Path $PSScriptRoot 'Environment.ps1')
$source=(Resolve-Path -LiteralPath $DalamudDirectory).Path
$target=Join-Path $ProjectRoot '.dependencies/Dalamud-15.0.3.6-estell.48'
New-Item -ItemType Directory $target -Force | Out-Null
foreach($name in @('Dalamud','Dalamud.Bindings.ImGui','FFXIVClientStructs','InteropGenerator.Runtime','Lumina','Lumina.Excel','Newtonsoft.Json','Serilog','Microsoft.Extensions.ObjectPool')) {
 $from=Join-Path $source "$name.dll"; $to=Join-Path $target "$name.dll"
 if(!(Test-Path -LiteralPath $from)){throw "Missing reference: $name"}
 if(Test-Path -LiteralPath $to){if((Get-FileHash $from).Hash -ne (Get-FileHash $to).Hash){throw "Existing reference mismatch: $name"}}
 else {Copy-Item -LiteralPath $from -Destination $to}
}
