[CmdletBinding()]
param(
    [ValidateSet('{{PROFILE_NAME}}')][string]$Character = '{{PROFILE_NAME}}',
    [string]$Directory,
    [string]$WindowTitle,
    [switch]$NoCompanion,
    [switch]$CheckOnly,
    [Parameter(ValueFromRemainingArguments=$true)][string[]]$ClaudeArguments
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$taskRoot = $PSScriptRoot
. (Join-Path $taskRoot 'ClaudeKeyEnvironment.ps1')
. (Join-Path $taskRoot 'PortableSkinTools.ps1')
$key = $Character.ToLowerInvariant()
$claude = Get-OCPortableClaudeExecutable
if ($Directory) { Set-Location -LiteralPath $Directory }
$env:OC_CLAUDE_CHARACTER = $key
$settings = Get-OCPortableClaudeSettings -PreviewOnly:$CheckOnly
$title = if($WindowTitle){$WindowTitle}else{"Claude - $Character"}
$env:OC_CLAUDE_TITLE = $title
$Host.UI.RawUI.WindowTitle = $title
if ($CheckOnly) {
    [pscustomobject]@{Character=$Character;Claude=$claude;Settings=$settings;Directory=(Get-Location).Path;Companion=Test-Path -LiteralPath "$taskRoot/bin/OCCompanion.exe"} | ConvertTo-Json
    exit 0
}
$companion = $null
if (-not $NoCompanion -and $env:WT_SESSION) {
    $companion = Start-Process -FilePath "$taskRoot/bin/OCCompanion.exe" -ArgumentList @('--character',$key,'--parent',"$PID",'--title',('"' + $title + '"')) -WindowStyle Hidden -PassThru
}
try {
    $taskPreviousCredentialEnvironment = Enter-OCClaudeKeyEnvironment
    & $claude --settings $settings @ClaudeArguments
    $claudeExit = $LASTEXITCODE
} finally {
    Exit-OCClaudeKeyEnvironment $taskPreviousCredentialEnvironment
    if ($companion -and -not $companion.HasExited) { Stop-Process -Id $companion.Id -ErrorAction SilentlyContinue }
}
if ($claudeExit -ne 0) { Write-Host "Claude Code exited with code $claudeExit." -ForegroundColor Yellow }
Write-Host "`n$Character terminal - run claude to continue, or exit to close." -ForegroundColor Magenta
