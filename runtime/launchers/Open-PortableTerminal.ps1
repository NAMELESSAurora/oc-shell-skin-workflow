[CmdletBinding()]
param([ValidateSet('PowerShell','Claude')][string]$Kind='PowerShell')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PortableSkinTools.ps1')
try{
    $path=Get-OCPortableTerminalSettings
    $settings=Read-OCPortableJson $path
    $name="$Kind - {{PROFILE_NAME}}"
    $profile=@($settings.profiles.list|Where-Object name -eq $name)|Select-Object -First 1
    if(-not$profile -or -not$profile.commandline.Contains($PSScriptRoot)){
        throw '请先双击“安装皮肤.cmd”。如果刚移动过解压文件夹，请重新安装以更新入口路径。'
    }
    if($Kind-eq'Claude'){Get-OCPortableClaudeExecutable|Out-Null}
    $terminal=Get-OCPortableTerminalExecutable
    & $terminal -w new new-tab -p $name
    if($LASTEXITCODE-ne0){throw 'Windows Terminal 未能打开该皮肤配置。请重新运行安装器。'}
}catch{Write-Host $_.Exception.Message -ForegroundColor Red;exit 1}
