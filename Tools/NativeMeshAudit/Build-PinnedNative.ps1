param(
    [Parameter(Mandatory=$true)][string]$SourceRoot,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$SourceRoot = (Resolve-Path $SourceRoot).Path
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$pin = '221dfe2877fa3f749ada49c98687e99fac74d437'
if ((git -C $SourceRoot rev-parse HEAD) -ne $pin) { throw 'Unexpected native source revision' }
if (@(git -C $SourceRoot status --porcelain).Count) { throw 'Candidate requires an unchanged pinned source checkout' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual C++ build tools were not found' }
$msbuild = Join-Path $vs 'MSBuild/Current/Bin/MSBuild.exe'
$toolset = $null
foreach ($candidate in @('v145','v143')) {
    if (@(Get-ChildItem (Join-Path $vs 'MSBuild/Microsoft/VC') -Directory -Filter $candidate -Recurse | Where-Object { $_.FullName -match 'PlatformToolsets' }).Count) { $toolset = $candidate; break }
}
if (-not $toolset) { throw 'Neither v145 nor v143 platform toolset is installed' }
$version = (Get-Content (Join-Path $vs 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
$bin = Join-Path $vs "VC/Tools/MSVC/$version/bin/Hostx64/x86"
$project = Join-Path $SourceRoot 'Navigation/Navigation.vcxproj'
@{ repository='Likon69/Navigation-C-'; commit=$pin; source_tree=(git -C $SourceRoot rev-parse 'HEAD^{tree}'); toolset=$toolset; compiler_tools=$version; platform='Win32'; production_selected=$false } | ConvertTo-Json | Out-File (Join-Path $OutputDirectory 'build-provenance.json')
& $msbuild $project /m /t:Rebuild /p:Configuration=Release /p:Platform=Win32 "/p:PlatformToolset=$toolset" "/p:OutDir=$OutputDirectory/" "/p:IntDir=$OutputDirectory/obj/" /v:minimal *> (Join-Path $OutputDirectory 'native-build.txt')
if ($LASTEXITCODE -ne 0) { throw 'Pinned native candidate did not build' }
$dll = Join-Path $OutputDirectory 'Navigation.dll'
if (-not (Test-Path $dll)) { throw 'Build produced no native DLL' }
# Use a separate compile-only unit; never edit the upstream source to make ABI checks pass.
$envScript = Join-Path $vs 'VC/Auxiliary/Build/vcvars32.bat'
$assertions = Join-Path $PSScriptRoot 'AbiAssertions.cpp'
$native = Join-Path $SourceRoot 'Navigation'
$command = 'call "' + $envScript + '" >nul && cl.exe /nologo /c /std:c++17 /EHsc /DDT_POLYREF64 /I"' + $native + '" /I"' + $native + '\Detour\Include" /Fo"' + $OutputDirectory + '\abi.obj" "' + $assertions + '"'
& cmd.exe /d /s /c $command *> (Join-Path $OutputDirectory 'abi-assertions.txt')
if ($LASTEXITCODE -ne 0) { throw 'Native ABI assertions failed' }
& (Join-Path $bin 'dumpbin.exe') /exports $dll *> (Join-Path $OutputDirectory 'exports.txt')
if ($LASTEXITCODE -ne 0) { throw 'Export inspection failed' }
$imports = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../../Tripper/Navigation/NativeMethods.cs'))
$exportText = [IO.File]::ReadAllText((Join-Path $OutputDirectory 'exports.txt'))
$pattern = '(?s)\[DllImport\(DllName,([^\]]*)\)\](?:(?!\[DllImport).)*?extern\s+\w+\s+(\w+)\s*\('
$checked = @()
foreach ($match in [regex]::Matches($imports,$pattern)) {
    $entry = $match.Groups[2].Value
    $explicit = [regex]::Match($match.Groups[1].Value,'EntryPoint\s*=\s*"([^"]+)"')
    if ($explicit.Success) { $entry = $explicit.Groups[1].Value }
    if ($exportText -notmatch ('(?m)\s' + [regex]::Escape($entry) + '\s*(?:=|$)')) { throw "Managed import has no candidate export: $entry" }
    $checked += $entry
}
if ($checked.Count -lt 50) { throw 'Import scan unexpectedly small; do not claim ABI coverage' }
$checked | ConvertTo-Json | Out-File (Join-Path $OutputDirectory 'verified-exports.json')
Get-FileHash $dll -Algorithm SHA256 | Format-List | Out-File (Join-Path $OutputDirectory 'dll-sha256.txt')
if (@(git -C $SourceRoot status --porcelain).Count) { throw 'Native source was modified during candidate build' }
Write-Host "Built pinned Win32 candidate; $($checked.Count) managed entry points found. Source/binary are not selected for production."
