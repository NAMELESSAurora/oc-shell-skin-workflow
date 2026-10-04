# Shared by Windows PowerShell 5.1 and PowerShell 7. No external modules required.
$script:OCShellRoot = $PSScriptRoot
. (Join-Path $PSScriptRoot 'ClaudeKeyEnvironment.ps1')
. (Join-Path $PSScriptRoot 'PortableSkinTools.ps1')
$script:OCSchemes = Get-Content -LiteralPath "$PSScriptRoot/oc-schemes.json" -Raw | ConvertFrom-Json
$script:OCCompanionProcess = $null
$script:OCGitCache = @{}
$script:OCWindowTitle = if($env:OC_CLAUDE_WINDOW_TITLE){$env:OC_CLAUDE_WINDOW_TITLE}else{'PowerShell - {{PROFILE_NAME}}'}
function Stop-OwnOCCompanion {
    $ownedIds=@()
    if($script:OCCompanionProcess){$ownedIds+=$script:OCCompanionProcess.Id}
    try{ $instance=Get-Content -LiteralPath "$script:OCShellRoot/runtime/state-$PID.json" -Raw -ErrorAction Stop | ConvertFrom-Json; if($instance.parent-eq$PID){$ownedIds+=[int]$instance.helperPid} }catch{}
    foreach($ownedId in ($ownedIds | Select-Object -Unique)){
        try{ $owned=Get-Process -Id $ownedId -ErrorAction Stop; if($owned.Path-eq (Join-Path $script:OCShellRoot 'bin/OCCompanion.exe')){Stop-Process -Id $ownedId -ErrorAction SilentlyContinue} }catch{}
    }
    $script:OCCompanionProcess=$null
}
function global:Set-OCTheme {
    [CmdletBinding()]
    param([ValidateSet('{{PROFILE_NAME}}')][string]$Character='{{PROFILE_NAME}}',[switch]$NoCompanion)
    $script:OCCharacter=$Character
    $env:OC_CLAUDE_CHARACTER=$Character.ToLowerInvariant()
    $script:OCScheme=$script:OCSchemes|Where-Object name -eq "OC $Character"
    $script:OCSymbol=[char]{{MOTIF_CODEPOINT}}
    $title=$script:OCWindowTitle
    if($Host.Name-eq'ConsoleHost'){$Host.UI.RawUI.WindowTitle=$title}
    $script:OCEscape=[string][char]27
    function local:Color($Hex){$r=[Convert]::ToInt32($Hex.Substring(1,2),16);$g=[Convert]::ToInt32($Hex.Substring(3,2),16);$b=[Convert]::ToInt32($Hex.Substring(5,2),16);return "$script:OCEscape[38;2;$r;$g;${b}m"}
    $script:OCAccent=Color $script:OCScheme.cursorColor
    $script:OCSecondary=Color $script:OCScheme.purple
    $script:OCSubtle=Color $script:OCScheme.brightBlack
    $script:OCForeground=Color $script:OCScheme.foreground
    $script:OCError=Color $script:OCScheme.red
    if($Host.Name-eq'ConsoleHost' -and -not[Console]::IsOutputRedirected){
        # OSC 10/11 updates the current pane, including after Set-OCTheme.
        [Console]::Write("$script:OCEscape]10;$($script:OCScheme.foreground)$([char]7)$script:OCEscape]11;$($script:OCScheme.background)$([char]7)")
    }
    if(Get-Module PSReadLine){
        Set-PSReadLineOption -Colors @{Command=$script:OCAccent;Parameter=$script:OCSecondary;String=(Color $script:OCScheme.green);Operator=(Color $script:OCScheme.cyan);Variable=(Color $script:OCScheme.yellow);Number=(Color $script:OCScheme.blue);Type=$script:OCSecondary;Comment=$script:OCSubtle;Error=$script:OCError;Default=$script:OCForeground;Selection="$script:OCEscape[7m"} -ErrorAction SilentlyContinue
    }
    Stop-OwnOCCompanion
    if($env:WT_SESSION -and -not$NoCompanion -and -not[Console]::IsOutputRedirected){
        $script:OCCompanionProcess=Start-Process -FilePath "$script:OCShellRoot/bin/OCCompanion.exe" -ArgumentList @('--character',$Character.ToLowerInvariant(),'--parent',"$PID",'--title',('"'+$title+'"')) -WindowStyle Hidden -PassThru
    }
}
function global:Hide-OCCompanion {
    Stop-OwnOCCompanion
}
function global:Show-OCCompanion { Set-OCTheme -Character $script:OCCharacter }
function global:Start-OCClaude {
    param([ValidateSet('{{PROFILE_NAME}}')][string]$Character=$script:OCCharacter,[Parameter(ValueFromRemainingArguments=$true)][string[]]$ClaudeArguments)
    Hide-OCCompanion
    try { & "$script:OCShellRoot/Start-OCClaude.ps1" -Character $Character -WindowTitle $script:OCWindowTitle -ClaudeArguments $ClaudeArguments }
    finally { Set-OCTheme -Character $script:OCCharacter }
}
function global:claude {
    $taskClaudeExecutable=Get-OCPortableClaudeExecutable
    if($args.Count-gt0 -and $args[0]-in@('--version','-v','--help','-h','auth','update','install','doctor','mcp','plugin','agents','logs','stop','rm')){
        & $taskClaudeExecutable @args
    }elseif($args-contains'-p' -or $args-contains'--print' -or $MyInvocation.ExpectingInput){
        $settings=Get-OCPortableClaudeSettings
        $taskPreviousCredentialEnvironment=Enter-OCClaudeKeyEnvironment
        try{
            if($MyInvocation.ExpectingInput){$input|& $taskClaudeExecutable --settings $settings @args}
            else{& $taskClaudeExecutable --settings $settings @args}
        }finally{Exit-OCClaudeKeyEnvironment $taskPreviousCredentialEnvironment}
    }else{
        Start-OCClaude -Character $script:OCCharacter -ClaudeArguments $args
    }
}
function global:prompt {
    $success=$?
    $path=$ExecutionContext.SessionState.Path.CurrentLocation.Path
    $display=$path
    if($path.StartsWith($env:USERPROFILE,[StringComparison]::OrdinalIgnoreCase)){$display='~'+$path.Substring($env:USERPROFILE.Length)}
    $branch=''
    if($script:OCGitCache.Path -eq $path -and ((Get-Date)-$script:OCGitCache.Time).TotalSeconds-lt2){$branch=$script:OCGitCache.Branch}
    else {
        try{
            $info=New-Object Diagnostics.ProcessStartInfo
            $info.FileName='git';$info.Arguments='symbolic-ref --quiet --short HEAD';$info.WorkingDirectory=$path;$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
            $git=[Diagnostics.Process]::Start($info)
            if($git.WaitForExit(180)){if($git.ExitCode-eq0){$branch=$git.StandardOutput.ReadToEnd().Trim()}}else{$git.Kill()}
            $git.Dispose()
        }catch{}
        $script:OCGitCache=@{Path=$path;Time=Get-Date;Branch=$branch}
    }
    $reset="$script:OCEscape[0m"
    $line="$script:OCAccent$($script:OCSymbol) $($script:OCCharacter.ToUpperInvariant()) $script:OCSubtle| $script:OCForeground$display"
    if($branch){$line+=" $script:OCSecondary[$branch]"}
    if(-not$success){$line+=" $script:OCError[failed]"}
    return "$line$reset`n$script:OCAccent$([char]0x276F)$reset "
}
if(-not(Get-Module PSReadLine)){Import-Module PSReadLine -ErrorAction SilentlyContinue}
$initial='{{PROFILE_NAME}}'
Set-OCTheme -Character $initial
Export-ModuleMember -Function Set-OCTheme,Hide-OCCompanion,Show-OCCompanion,Start-OCClaude,claude,prompt
