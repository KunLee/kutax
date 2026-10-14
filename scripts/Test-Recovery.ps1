# Requires PowerShell 7 and Podman. Uses only KuTax containers and a new isolated restore project.
[CmdletBinding()]
param([string]$EnvironmentFile=(Join-Path $PSScriptRoot '../infra/.env'),[string]$BackupDirectory=(Join-Path $PSScriptRoot ('../artifacts/recovery/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))))
$ErrorActionPreference='Stop'
$podman=(Get-Command podman -ErrorAction SilentlyContinue).Source
if(!$podman){$podman='C:\Program Files\RedHat\Podman\podman.exe'}
function Run([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments){$result=& $podman @Arguments;if($LASTEXITCODE -ne 0){throw "Podman operation failed: $($Arguments[0])"};return $result}
function Inspect([string]$Name){$item=(Run @('inspect',$Name)|ConvertFrom-Json)[0];if($item.Config.Labels.'com.docker.compose.project' -ne 'kutax'){throw 'Container is outside the KuTax project.'};return $item}
$app=Inspect 'kutax-app-1';$postgres=Inspect 'kutax-postgres-1';$rustfs=Inspect 'kutax-rustfs-1'
$rustVolume=($rustfs.Mounts|Where-Object Destination -eq '/data').Name;$keyVolume=($app.Mounts|Where-Object Destination -eq '/app/keys').Name
if(!$rustVolume -or !$keyVolume){throw 'Expected durable KuTax volumes are missing.'}
$settings=@{};Get-Content -LiteralPath $EnvironmentFile|ForEach-Object{if($_ -match '^([^#=]+)=(.*)$'){$settings[$Matches[1]]=$Matches[2]}}
foreach($name in @('POSTGRES_PASSWORD','RUSTFS_ACCESS_KEY','RUSTFS_SECRET_KEY')){if(!$settings[$name]){throw 'Required protected environment setting missing.'}}
$root=[IO.Path]::GetFullPath($BackupDirectory);New-Item -ItemType Directory -Path $root -Force|Out-Null
if($IsWindows){& icacls $root /inheritance:r /grant:r "$([Security.Principal.WindowsIdentity]::GetCurrent().Name):(OI)(CI)F" 'SYSTEM:(OI)(CI)F'|Out-Null;if($LASTEXITCODE -ne 0){throw 'Could not restrict backup permissions.'}}else{& chmod 700 $root}
$prefix='kutax-restore-'+[guid]::NewGuid().ToString('N').Substring(0,12)
$compose=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../infra/compose.yaml'))
$environment=[IO.Path]::GetFullPath($EnvironmentFile)
$sourceRunning=$app.State.Running;$rustRunning=$rustfs.State.Running
$sensitiveFiles=@()
$helpers=@()
Push-Location -LiteralPath $root
try{
    if($sourceRunning){Run @('stop','--time','30','kutax-app-1')|Out-Null}
    # No application writers remain. Verify originals, exports and frozen references before backup.
    $sourceCheck=Run @('compose','--env-file',$environment,'-f',$compose,'run','--rm','--no-deps','app','--verify-recovery')
    $sourceJson=($sourceCheck|Where-Object{$_ -match '^\{"verified":true,'}|Select-Object -Last 1)
    if(!$sourceJson){throw 'Source integrity verification did not complete.'}
    $dumpName='kutax-'+[guid]::NewGuid().ToString('N')+'.dump'
    Run @('exec','kutax-postgres-1','pg_dump','-U','kutax','-d','kutax','-Fc','--file',('/tmp/'+$dumpName))|Out-Null
    Run @('cp',('kutax-postgres-1:/tmp/'+$dumpName),'./database.dump')|Out-Null
    Run @('exec','kutax-postgres-1','rm',('/tmp/'+$dumpName))|Out-Null
    if($rustRunning){Run @('stop','--time','30','kutax-rustfs-1')|Out-Null}
    $backupHelper=$prefix+'-backup-helper';$helpers+=$backupHelper
    Run @('run','-d','--name',$backupHelper,'--network','none','--entrypoint','sleep','-v',($rustVolume+':/source:ro'),'-v',($keyVolume+':/keys:ro'),$postgres.Image,'infinity')|Out-Null
    Run @('exec',$backupHelper,'tar','-C','/source','-cf','/tmp/rustfs.tar','.')|Out-Null
    Run @('exec',$backupHelper,'tar','-C','/keys','-cf','/tmp/app-keys.tar','.')|Out-Null
    Run @('cp',($backupHelper+':/tmp/rustfs.tar'),'./rustfs.tar')|Out-Null
    Run @('cp',($backupHelper+':/tmp/app-keys.tar'),'./app-keys.tar')|Out-Null
    $manifest=[ordered]@{format='kutax-recovery-v1';createdAtUtc=[DateTimeOffset]::UtcNow.ToString('O');restoreProject=$prefix;sourceVerification=($sourceJson|ConvertFrom-Json);images=@{app=$app.Image;postgres=$postgres.Image;rustfs=$rustfs.Image};files=@(Get-ChildItem -LiteralPath $root -File|ForEach-Object{@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})}
    $manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $root 'manifest.json')
    if($rustRunning){Run @('start','kutax-rustfs-1')|Out-Null}
    Write-Host 'Backup complete. Restoring into an isolated network and fresh volumes.'
    Run @('network','create',$prefix)|Out-Null
    foreach($suffix in @('postgres','rustfs','keys')){Run @('volume','create',($prefix+'-'+$suffix))|Out-Null}
    foreach($file in $manifest.files){if((Get-FileHash -LiteralPath (Join-Path $root $file.name)).Hash -ne $file.sha256){throw 'Backup archive checksum changed before restore.'}}
    $restoreHelper=$prefix+'-restore-helper';$helpers+=$restoreHelper
    # Podman on Windows cannot copy host files into containers; read archives through a read-only mount instead.
    $inputMount=$root+':/recovery-input:ro'
    Run @('run','-d','--name',$restoreHelper,'--network','none','--entrypoint','sleep','-v',$inputMount,'-v',($prefix+'-rustfs:/target'),'-v',($prefix+'-keys:/keys'),$postgres.Image,'infinity')|Out-Null
    Run @('exec',$restoreHelper,'tar','-C','/target','-xf','/recovery-input/rustfs.tar')|Out-Null
    Run @('exec',$restoreHelper,'tar','-C','/keys','-xf','/recovery-input/app-keys.tar')|Out-Null
    $pgEnv=Join-Path $root 'postgres.env';$sensitiveFiles+=$pgEnv
    @('POSTGRES_DB=kutax','POSTGRES_USER=kutax',('POSTGRES_PASSWORD='+$settings['POSTGRES_PASSWORD']))|Set-Content -LiteralPath $pgEnv
    Run @('run','-d','--name',($prefix+'-postgres'),'--network',$prefix,'--network-alias','postgres','--env-file',$pgEnv,'-v',$inputMount,'-v',($prefix+'-postgres:/var/lib/postgresql'),$postgres.Image)|Out-Null
    $ready=$false;for($attempt=0;$attempt -lt 60;$attempt++){& $podman exec ($prefix+'-postgres') pg_isready -h 127.0.0.1 -U kutax -d kutax *> $null;if($LASTEXITCODE -eq 0){$ready=$true;break};Start-Sleep -Seconds 2};if(!$ready){throw 'Restored PostgreSQL did not become ready.'}
    Run @('exec',($prefix+'-postgres'),'pg_restore','--exit-on-error','--no-owner','--no-acl','-U','kutax','-d','kutax','/recovery-input/database.dump')|Out-Null
    $storageEnv=Join-Path $root 'storage.env';$sensitiveFiles+=$storageEnv
    @('RUSTFS_VOLUMES=/data','RUSTFS_ADDRESS=0.0.0.0:9000','RUSTFS_CONSOLE_ENABLE=false',('RUSTFS_ACCESS_KEY='+$settings['RUSTFS_ACCESS_KEY']),('RUSTFS_SECRET_KEY='+$settings['RUSTFS_SECRET_KEY']))|Set-Content -LiteralPath $storageEnv
    Run @('run','-d','--name',($prefix+'-rustfs'),'--network',$prefix,'--network-alias','rustfs','--env-file',$storageEnv,'-v',($prefix+'-rustfs:/data'),$rustfs.Image)|Out-Null
    $ready=$false;for($attempt=0;$attempt -lt 60;$attempt++){& $podman exec ($prefix+'-rustfs') curl -fsS http://localhost:9000/health *> $null;if($LASTEXITCODE -eq 0){$ready=$true;break};Start-Sleep -Seconds 2};if(!$ready){throw 'Restored RustFS did not become ready.'}
    $verifyEnv=Join-Path $root 'verify.env';$sensitiveFiles+=$verifyEnv
    @('ASPNETCORE_ENVIRONMENT=Development',('ConnectionStrings__Database=Host=postgres;Database=kutax;Username=kutax;Password='+$settings['POSTGRES_PASSWORD']+';GSS Encryption Mode=Disable'),'Storage__Endpoint=http://rustfs:9000',('Storage__AccessKey='+$settings['RUSTFS_ACCESS_KEY']),('Storage__SecretKey='+$settings['RUSTFS_SECRET_KEY']))|Set-Content -LiteralPath $verifyEnv
    $restoredCheck=Run @('run','--rm','--network',$prefix,'--env-file',$verifyEnv,'-v',($prefix+'-keys:/app/keys'),$app.Image,'--verify-recovery')
    $restoredJson=($restoredCheck|Where-Object{$_ -match '^\{"verified":true,'}|Select-Object -Last 1)
    if(!$restoredJson -or $restoredJson -ne $sourceJson){throw 'Restored object checksums, snapshot reconciliation or record counts differ from the source.'}
    $result=[ordered]@{passed=$true;completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');restoreProject=$prefix;verification=($restoredJson|ConvertFrom-Json);backupDirectory=$root}
    $result|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $root 'restore-result.json')
    Run @('stop',($prefix+'-postgres'),($prefix+'-rustfs'))|Out-Null
    Write-Host ('Restore passed. Isolated containers are stopped and retained for inspection: '+$prefix)
    Write-Host ('Protected backup and evidence: '+$root)
}finally{
    foreach($helper in $helpers){& $podman rm -f $helper *> $null}
    if($dumpName){& $podman exec kutax-postgres-1 rm -f ('/tmp/'+$dumpName) *> $null}
    foreach($file in $sensitiveFiles){if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file -Force}}
    if($rustRunning){& $podman start kutax-rustfs-1 *> $null}
    if($sourceRunning){& $podman start kutax-app-1 *> $null}
    Pop-Location
}

