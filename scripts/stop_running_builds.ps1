param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$targets = @(
    Get-Process | Where-Object {
        $_.Path -and
        ([System.IO.Path]::GetDirectoryName($_.Path) -eq $resolvedOutputDir) -and
        (($_.Path -like "*\ValorantAfk.exe") -or ($_.Path -like "*\ValorantAfkBot.exe") -or ($_.Path -like "*\anti-afk.exe"))
    }
)

if ($targets.Count -eq 0) {
    exit 0
}

$targets | Stop-Process -Force

foreach ($target in $targets) {
    try {
        Wait-Process -Id $target.Id -Timeout 5 -ErrorAction Stop
    }
    catch {
    }
}
