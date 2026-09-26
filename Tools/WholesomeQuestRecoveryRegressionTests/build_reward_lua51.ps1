# Test dependency only. Never installs into the host, game or user's machine.
# Official Lua 5.1.5 package and checksum: https://www.lua.org/ftp/
#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Stock Lua test provisioning is restricted to the GitHub-hosted runner.'
}
$temporary = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar)
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (-not $destinationPath.StartsWith($temporary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Lua test output must be below RUNNER_TEMP.'
}
if (Test-Path -LiteralPath $destinationPath) { throw 'Refusing to reuse or overwrite an existing Lua test directory.' }
[IO.Directory]::CreateDirectory($destinationPath) | Out-Null
$archive = Join-Path $destinationPath 'lua-5.1.5.tar.gz'
$expected = '2640fc56a795f29d28ef15e13c34a47e223960b0240e8cb0a82d9b0738695333'
Invoke-WebRequest -Uri 'https://www.lua.org/ftp/lua-5.1.5.tar.gz' -OutFile $archive -MaximumRetryCount 2 -RetryIntervalSec 2
$actual = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw 'Official Lua 5.1.5 source checksum mismatch.' }
Write-Output ('LUA51_SOURCE_SHA256=' + $actual)
& tar -xzf $archive -C $destinationPath
if ($LASTEXITCODE -ne 0) { throw 'Verified Lua source extraction failed.' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($vs)) { throw 'GitHub runner MSVC x86 tools were not found.' }
Import-Module (Join-Path $vs 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch=x86 -host_arch=x64' | Out-Null
$source = Join-Path $destinationPath 'lua-5.1.5\src'
$inputs = @(Get-ChildItem -LiteralPath $source -Filter '*.c' -File |
    Where-Object { $_.Name -notin @('lua.c','luac.c','print.c') } | Sort-Object Name | ForEach-Object { $_.FullName })
if ($inputs.Count -lt 20) { throw 'Incomplete Lua library source inventory.' }
Push-Location -LiteralPath $destinationPath
try {
    & cl.exe /nologo /O2 /MD /DLUA_BUILD_AS_DLL /LD /Felua51.dll @inputs /link /MACHINE:X86
    if ($LASTEXITCODE -ne 0) { throw 'Stock Lua 5.1 x86 compilation failed.' }
} finally { Pop-Location }
$dll = Join-Path $destinationPath 'lua51.dll'
$bytes = [IO.File]::ReadAllBytes($dll)
$pe = [BitConverter]::ToInt32($bytes, 60)
if ([BitConverter]::ToUInt32($bytes, $pe) -ne 0x4550 -or [BitConverter]::ToUInt16($bytes, $pe + 4) -ne 0x14c) {
    throw 'The stock Lua test library is not an x86 PE image.'
}
Write-Output ('LUA51_DLL_SHA256=' + (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash.ToLowerInvariant())
Write-Output ('LUA51_DLL=' + $dll)
