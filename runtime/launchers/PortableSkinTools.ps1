# Helpers for this single-role portable package. No credentials are read here.
$script:OCPortableRoot = $PSScriptRoot
function Read-OCPortableJson {
    param([string]$Path)
    $raw=Get-Content -LiteralPath $Path -Raw
    # Windows Terminal also accepts JSONC. Preserve strings (including URLs),
    # remove only comments/trailing commas, and keep an untouched backup on install.
    $stringsAndComments='("(?:\\.|[^"\\])*")|//[^\r\n]*|/\*[\s\S]*?\*/'
    $raw=[regex]::Replace($raw,$stringsAndComments,{param($match)if($match.Groups[1].Success){$match.Value}else{' '}})
    $stringsAndCommas='("(?:\\.|[^"\\])*")|,\s*(?=[}\]])'
    $raw=[regex]::Replace($raw,$stringsAndCommas,{param($match)if($match.Groups[1].Success){$match.Value}else{''}})
    return $raw|ConvertFrom-Json
}
function Write-OCPortableJson {
    param([string]$Path, $Value)
    $folder = Split-Path $Path -Parent
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $temporaryPath = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temporaryPath, ($Value | ConvertTo-Json -Depth 50) + "`n", (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $Path) { [IO.File]::Replace($temporaryPath, $Path, [NullString]::Value) }
    else { [IO.File]::Move($temporaryPath, $Path) }
}
function Get-OCPortableClaudeExecutable {
    $native = Join-Path $env:USERPROFILE '.local/bin/claude.exe'
    if (Test-Path -LiteralPath $native) { return $native }
    # Ignore the PowerShell wrapper named claude; resolve only an application.
    $command = Get-Command claude -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    throw '未找到 Claude Code。请先安装 Claude Code，再打开 Claude 皮肤；PowerShell 皮肤可以独立使用。'
}
function Get-OCPortableTerminalSettings {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Packages/Microsoft.WindowsTerminal_8wekyb3d8bbwe/LocalState/settings.json'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft/Windows Terminal/settings.json'),
        (Join-Path $env:LOCALAPPDATA 'Packages/Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe/LocalState/settings.json')
    )
    foreach ($path in $candidates) { if (Test-Path -LiteralPath $path) { return $path } }
    throw '未找到 Windows Terminal 配置。请先安装并打开一次 Windows Terminal，然后重新运行安装器。'
}
function Get-OCPortableTerminalExecutable {
    $command = Get-Command wt.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    $alias = Join-Path $env:LOCALAPPDATA 'Microsoft/WindowsApps/wt.exe'
    if (Test-Path -LiteralPath $alias) { return $alias }
    throw '未找到 wt.exe。请启用 Windows Terminal 的应用执行别名，或将 Windows Terminal 加入 PATH。'
}
function New-OCPortableClaudeSettings {
    return [ordered]@{
        theme = 'custom:oc-{{ROLE_ID}}'
        statusLine = [ordered]@{type='command';command=('"'+(Join-Path $script:OCPortableRoot 'bin/OCStatusLine.exe')+'"');padding=0}
    }
}
function Get-OCPortableClaudeSettings {
    param([switch]$PreviewOnly)
    $path = Join-Path $script:OCPortableRoot 'claude/{{ROLE_ID}}.json'
    if (-not $PreviewOnly) {
        # Regenerate after relocation; this is package-local appearance data only.
        $expected = New-OCPortableClaudeSettings
        $current = $null
        try { $current = Read-OCPortableJson $path } catch {}
        if (-not $current -or $current.statusLine.command -ne $expected.statusLine.command -or $current.theme -ne $expected.theme) {
            Write-OCPortableJson $path $expected
        }
    }
    return $path
}
