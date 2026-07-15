$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    pnpm.cmd install --frozen-lockfile=$false
    dotnet build 'backend/LocalMediaManager.Bridge/LocalMediaManager.Bridge.csproj' -c Debug
    pnpm.cmd build:web
    pnpm.cmd bridge:publish
    pnpm.cmd migration:publish

    $vsDev = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
    if (Test-Path -LiteralPath $vsDev) {
        $buildCommand = 'call "{0}" && set "PATH=%USERPROFILE%\.cargo\bin;%PATH%" && pnpm.cmd tauri:build' -f $vsDev
        & cmd.exe /d /s /c $buildCommand
    } else {
        pnpm.cmd tauri:build
    }
} finally {
    Pop-Location
}
