param([ValidateSet('{{PROFILE_NAME}}')][string]$Character='{{PROFILE_NAME}}',[string]$Directory)
$env:OC_CLAUDE_CHARACTER=$Character.ToLowerInvariant()
$env:OC_CLAUDE_WINDOW_TITLE="PowerShell - $Character"
try {
    New-Item -ItemType Directory -Path "$PSScriptRoot/runtime" -Force | Out-Null
    [IO.File]::AppendAllText("$PSScriptRoot/runtime/launcher.log",((Get-Date).ToString('o')+" PowerShell $Character pid=$PID wt=$([bool]$env:WT_SESSION) redirected=$([Console]::IsOutputRedirected)`n"))
    if($Directory){Set-Location -LiteralPath $Directory}
    . "$PSScriptRoot/OCShell.ps1"
} catch {
    [IO.File]::AppendAllText("$PSScriptRoot/runtime/launcher.log",($_.ToString()+"`n"))
    Write-Error $_
}
