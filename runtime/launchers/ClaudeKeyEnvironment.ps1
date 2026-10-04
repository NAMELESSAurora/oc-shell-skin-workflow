# Refresh only the child CLI's credential environment after a manager switch.
# Secrets never become command arguments or normal command output.
function Enter-OCClaudeKeyEnvironment {
    $taskVault = Join-Path $env:LOCALAPPDATA '{{CONFIG_NAMESPACE}}/claude-keys/profiles.json'
    if (-not (Test-Path -LiteralPath $taskVault)) { return $null }
    try { $taskLibrary = Get-Content -LiteralPath $taskVault -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop }
    catch { throw 'Claude 密钥库无法读取，未启动 CLI。请在挂件的密钥管理器中检查。' }
    if (-not $taskLibrary.Active) { return $null }
    $taskConfigRoot = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
    try { $taskConfig = Get-Content -LiteralPath (Join-Path $taskConfigRoot 'settings.json') -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop }
    catch { throw 'Claude settings.json 无法读取，未启动 CLI。请检查配置文件。' }
    $taskSaved = @{}
    foreach ($taskName in @('ANTHROPIC_BASE_URL','ANTHROPIC_AUTH_TOKEN','ANTHROPIC_API_KEY')) {
        $taskSaved[$taskName] = [Environment]::GetEnvironmentVariable($taskName,'Process')
        $taskProperty = if ($null -ne $taskConfig.env) { $taskConfig.env.PSObject.Properties[$taskName] } else { $null }
        [Environment]::SetEnvironmentVariable($taskName, $(if ($taskProperty) { [string]$taskProperty.Value } else { [NullString]::Value }), 'Process')
    }
    return $taskSaved
}
function Exit-OCClaudeKeyEnvironment {
    param($Previous)
    if ($null -eq $Previous) { return }
    foreach ($taskName in $Previous.Keys) {
        $taskValue = if ($null -eq $Previous[$taskName]) { [NullString]::Value } else { $Previous[$taskName] }
        [Environment]::SetEnvironmentVariable($taskName,$taskValue,'Process')
    }
}
