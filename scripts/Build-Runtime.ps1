[CmdletBinding()]
param([string]$ContractPath,[string]$AssetRoot,[string]$OutputDirectory,[switch]$CodeOnly)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $repoRoot 'runtime/WorkflowTools.psm1') -Force -DisableNameChecking
if(-not$ContractPath){$ContractPath=Join-Path $repoRoot 'templates/character-runtime-contract.json'}
$contract=Read-CharacterContract $ContractPath
if(-not$OutputDirectory){$OutputDirectory=Join-Path $repoRoot ('build/runtime/'+$contract.id)}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if($OutputDirectory-eq[IO.Path]::GetFullPath($repoRoot)){throw 'Build output must be separate from repository source'}
if(-not$AssetRoot){$CodeOnly=$true}
if(-not$CodeOnly -and $contract.reviewStatus-notin@('approved','reviewed-example')){throw 'Full asset build requires a reviewed contract. Drafts may compile with -CodeOnly.'}
$marker=Join-Path $OutputDirectory 'build-manifest.json'
if(Test-Path -LiteralPath $OutputDirectory){
    if(@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count){
        if(-not(Test-Path -LiteralPath $marker)){throw 'Output is not an existing workflow build; choose an empty directory.'}
        $previous=Get-Content -LiteralPath $marker -Raw|ConvertFrom-Json
        if($previous.kind-ne'oc-shell-workflow-generated-runtime' -or $previous.role-ne$contract.id){throw 'Output belongs to another build or role; preserved.'}
    }
}
$native=Join-Path $OutputDirectory 'native';$bin=Join-Path $OutputDirectory 'bin'
[IO.Directory]::CreateDirectory($native)|Out-Null;[IO.Directory]::CreateDirectory($bin)|Out-Null
Write-WorkflowJson $marker ([ordered]@{kind='oc-shell-workflow-generated-runtime';role=$contract.id;status='building';assetsInjected=$false})
$sourceRoot=Join-Path $repoRoot 'runtime/native'
$names=Get-Content -LiteralPath (Join-Path $sourceRoot 'sources.json') -Raw|ConvertFrom-Json
foreach($name in $names){if($name-ne'RoleCatalog.cs'){Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination (Join-Path $native $name) -Force}}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'OCCompanion.manifest') -Destination $native -Force
[IO.File]::WriteAllText((Join-Path $native 'RoleCatalog.cs'),(New-RoleCatalogSource $contract),(New-Object Text.UTF8Encoding($false)))
Write-WorkflowJson (Join-Path $OutputDirectory 'runtime-contract.json') $contract
foreach($file in (Get-ChildItem -LiteralPath (Join-Path $repoRoot 'runtime/launchers') -File)){
    $text=Render-RoleTemplate ([IO.File]::ReadAllText($file.FullName)) $contract
    $encoding=New-Object Text.UTF8Encoding($file.Extension-in@('.ps1','.psm1'))
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $file.Name),$text,$encoding)
}
if(-not$CodeOnly){
    $AssetRoot=[IO.Path]::GetFullPath($AssetRoot)
    if(-not(Test-Path -LiteralPath $AssetRoot -PathType Container)){throw 'AssetRoot does not exist'}
    foreach($relative in @($contract.requiredFiles)+@($contract.optionalFiles)){
        $source=Join-Path $AssetRoot $relative
        if(-not(Test-Path -LiteralPath $source)){if($relative-in$contract.requiredFiles){throw ('Reviewed resource missing: '+$relative)};continue}
        $target=Join-Path $OutputDirectory $relative;[IO.Directory]::CreateDirectory((Split-Path $target -Parent))|Out-Null;Copy-Item -LiteralPath $source -Destination $target -Force
    }
    $voiceSource=Join-Path $AssetRoot ('voice-library/interaction-ja/'+$contract.id)
    if(Test-Path -LiteralPath $voiceSource){$voiceTarget=Join-Path $OutputDirectory ('voice-library/interaction-ja/'+$contract.id);[IO.Directory]::CreateDirectory($voiceTarget)|Out-Null;foreach($file in (Get-ChildItem -LiteralPath $voiceSource -File)){if($file.Name-match'^[a-z0-9_]+\.wav(\.json)?$'){Copy-Item -LiteralPath $file.FullName -Destination $voiceTarget -Force}}}
    # Never copy source runtime/settings/logs, keys, memories, account voice bindings or bins.
    $claudeSkin=[ordered]@{theme=('custom:oc-'+$contract.id);statusLine=[ordered]@{type='command';command='"bin/OCStatusLine.exe"';padding=0}}
    Write-WorkflowJson (Join-Path $OutputDirectory ('claude/'+$contract.id+'.json')) $claudeSkin
}
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319';$compiler=Join-Path $framework 'csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){throw 'Windows x64 .NET Framework compiler is required'}
$refs=@('PresentationCore','PresentationFramework','WindowsBase')|ForEach-Object {'/reference:'+(Join-Path $framework ('WPF/'+$_+'.dll'))}
foreach($assembly in @('System.Web.Extensions','System.Xaml','System.Security')){$refs+='/reference:'+(Join-Path $framework ($assembly+'.dll'))}
$sources=@($names|ForEach-Object {Join-Path $native $_})
& $compiler /nologo /optimize+ /platform:x64 /target:winexe /main:OCShell.OverlayProgram ('/win32manifest:'+(Join-Path $native 'OCCompanion.manifest')) ('/out:'+(Join-Path $bin 'OCCompanion.exe')) @refs @sources
if($LASTEXITCODE-ne0){throw 'Runtime compilation failed'}
& $compiler /nologo /optimize+ /platform:x64 /target:exe /main:OCShell.StatusProgram ('/out:'+(Join-Path $bin 'OCStatusLine.exe')) @refs @sources
if($LASTEXITCODE-ne0){throw 'Status line compilation failed'}
$report=[ordered]@{kind='oc-shell-workflow-generated-runtime';status='compiled';role=$contract.id;profileName=$contract.profileName;configurationNamespace=$contract.configurationNamespace;assetsInjected=(-not$CodeOnly);guiReady=$false;validationState='not-run';offlineValidationRequired=$true;contractSha256=(Get-FileHash -LiteralPath $ContractPath).Hash;companionSha256=(Get-FileHash -LiteralPath (Join-Path $bin 'OCCompanion.exe')).Hash;statusLineSha256=(Get-FileHash -LiteralPath (Join-Path $bin 'OCStatusLine.exe')).Hash;apiRequests=0;userConfigurationChanged=$false;installed=$false}
Write-WorkflowJson $marker $report
Write-Output ('Compiled single-role runtime to '+$OutputDirectory+'. assetsInjected='+(-not$CodeOnly)+'. No installation or API request was performed.')
