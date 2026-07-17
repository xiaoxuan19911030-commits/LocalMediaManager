param(
    [string]$InstallDir = 'D:\Local Media Manager Next',
    [string]$DataDir = 'D:\Local Media Manager Next Data',
    [string]$BackupRoot = 'D:\Local Media Manager Next Backups',
    [switch]$SkipBuild,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'

function Resolve-StrictPath([string]$Path) {
    return (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
}

function Assert-ChildPath([string]$Parent, [string]$Child) {
    $parentResolved = Resolve-StrictPath $Parent
    $childParent = Split-Path -Parent $Child
    if (-not (Test-Path -LiteralPath $childParent)) {
        New-Item -ItemType Directory -Path $childParent -Force | Out-Null
    }
    $childFull = [System.IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith($parentResolved, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside install directory: $childFull"
    }
}

function Copy-DirectoryClean([string]$Source, [string]$Destination, [string]$AllowedParent) {
    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Missing source directory: $Source"
    }

    Assert-ChildPath -Parent $AllowedParent -Child $Destination
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
}

function Stop-InstalledProcesses([string]$InstallPath) {
    $installFull = [System.IO.Path]::GetFullPath($InstallPath)
    $processes = Get-Process | Where-Object {
        try {
            $_.Path -and ([System.IO.Path]::GetFullPath($_.Path)).StartsWith($installFull, [System.StringComparison]::OrdinalIgnoreCase)
        } catch {
            $false
        }
    }

    foreach ($process in $processes) {
        if ($process.MainWindowHandle -ne 0) {
            [void]$process.CloseMainWindow()
        }
    }

    Start-Sleep -Seconds 2

    foreach ($process in $processes) {
        $fresh = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if ($fresh) {
            Stop-Process -Id $fresh.Id -Force
        }
    }
}

function Invoke-NativeCommand([string]$Name, [scriptblock]$Command) {
    $global:LASTEXITCODE = 0
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

function Build-ReleaseArtifacts([string]$RepoRoot) {
    Invoke-NativeCommand -Name 'Web build' -Command { pnpm.cmd build:web }
    Invoke-NativeCommand -Name 'Bridge publish' -Command { pnpm.cmd bridge:publish }
    Invoke-NativeCommand -Name 'Migration publish' -Command { pnpm.cmd migration:publish }

    $vsDev = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
    if (Test-Path -LiteralPath $vsDev) {
        $buildCommand = 'call "{0}" && set "PATH=%USERPROFILE%\.cargo\bin;%PATH%" && pnpm.cmd tauri:build' -f $vsDev
        Invoke-NativeCommand -Name 'Tauri release build' -Command { cmd.exe /d /s /c $buildCommand }
    } else {
        Invoke-NativeCommand -Name 'Tauri release build' -Command { pnpm.cmd tauri:build }
    }
}

$repoRoot = Resolve-StrictPath (Join-Path $PSScriptRoot '..')
$installFull = Resolve-StrictPath $InstallDir
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $BackupRoot "deploy-$timestamp"
$programBackupDir = Join-Path $backupDir 'program-before'
$dataBackupDir = Join-Path $backupDir 'user-data'

New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

Push-Location $repoRoot
try {
    Stop-InstalledProcesses -InstallPath $installFull

    if (Test-Path -LiteralPath $DataDir) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $dataBackupDir) -Force | Out-Null
        Copy-Item -LiteralPath $DataDir -Destination $dataBackupDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $programBackupDir -Force | Out-Null
    foreach ($programItem in @('local-media-manager.exe', 'resources\bridge', 'resources\migration')) {
        $sourceItem = Join-Path $installFull $programItem
        if (Test-Path -LiteralPath $sourceItem) {
            $targetItem = Join-Path $programBackupDir $programItem
            New-Item -ItemType Directory -Path (Split-Path -Parent $targetItem) -Force | Out-Null
            Copy-Item -LiteralPath $sourceItem -Destination $targetItem -Recurse -Force
        }
    }

    if (-not $SkipBuild) {
        Build-ReleaseArtifacts -RepoRoot $repoRoot
    }

    $releaseExe = Join-Path $repoRoot 'src-tauri\target\release\local-media-manager.exe'
    $bridgeResources = Join-Path $repoRoot 'src-tauri\resources\bridge'
    $migrationResources = Join-Path $repoRoot 'src-tauri\resources\migration'
    $distIndex = Join-Path $repoRoot 'dist\index.html'

    foreach ($required in @(
        $releaseExe,
        (Join-Path $bridgeResources 'LocalMediaManager.Bridge.exe'),
        (Join-Path $migrationResources 'LocalMediaManager.Migration.exe'),
        $distIndex
    )) {
        if (-not (Test-Path -LiteralPath $required)) {
            throw "Missing build artifact: $required"
        }
    }

    Copy-Item -LiteralPath $releaseExe -Destination (Join-Path $installFull 'local-media-manager.exe') -Force
    New-Item -ItemType Directory -Path (Join-Path $installFull 'resources') -Force | Out-Null
    Copy-DirectoryClean -Source $bridgeResources -Destination (Join-Path $installFull 'resources\bridge') -AllowedParent $installFull
    Copy-DirectoryClean -Source $migrationResources -Destination (Join-Path $installFull 'resources\migration') -AllowedParent $installFull

    $staleWebAssets = Get-ChildItem -LiteralPath $installFull -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @('index.html', 'assets', 'dist') } |
        Select-Object -ExpandProperty FullName

    $installedExe = Join-Path $installFull 'local-media-manager.exe'
    $installedBridge = Join-Path $installFull 'resources\bridge\LocalMediaManager.Bridge.exe'
    $installedMigration = Join-Path $installFull 'resources\migration\LocalMediaManager.Migration.exe'

    $summary = [ordered]@{
        InstalledExe = $installedExe
        InstalledBridge = $installedBridge
        InstalledMigration = $installedMigration
        InstallDir = $installFull
        DataDir = if (Test-Path -LiteralPath $DataDir) { Resolve-StrictPath $DataDir } else { $DataDir }
        BackupDir = $backupDir
        ReleaseExeLastWriteTime = (Get-Item -LiteralPath $installedExe).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
        BridgeLastWriteTime = (Get-Item -LiteralPath $installedBridge).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
        MigrationLastWriteTime = (Get-Item -LiteralPath $installedMigration).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
        StaleTopLevelWebAssets = @($staleWebAssets)
    }

    $summaryPath = Join-Path $backupDir 'deploy-summary.json'
    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    $summary | ConvertTo-Json -Depth 4

    if ($Launch) {
        Start-Process -FilePath $installedExe -WorkingDirectory $installFull | Out-Null
    }
} finally {
    Pop-Location
}
