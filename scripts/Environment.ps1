$ErrorActionPreference = 'Stop'
$script:ProjectRoot = Split-Path $PSScriptRoot -Parent
$locations = @{
 DOTNET_CLI_HOME='.work/dotnet'; NUGET_PACKAGES='.work/nuget/packages'; NUGET_HTTP_CACHE_PATH='.work/nuget/http';
 NUGET_PLUGINS_CACHE_PATH='.work/nuget/plugins'; NUGET_SCRATCH='.work/nuget/scratch'; TEMP='.work/tmp'; TMP='.work/tmp';
 APPDATA='.work/appdata'; LOCALAPPDATA='.work/localappdata'; DOTNET_BUNDLE_EXTRACT_BASE_DIR='.work/bundles'; XDG_CACHE_HOME='.work/cache'
}
foreach($item in $locations.GetEnumerator()) {
 $path=Join-Path $ProjectRoot $item.Value; New-Item -ItemType Directory $path -Force | Out-Null
 [Environment]::SetEnvironmentVariable($item.Key,$path,'Process')
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'; $env:DOTNET_NOLOGO='1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'; $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
$env:MSBUILDDISABLENODEREUSE='0'; $env:DOTNET_CLI_USE_MSBUILD_SERVER='0'
$env:NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED='false'
Set-Location -LiteralPath $ProjectRoot
