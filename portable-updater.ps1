param([string]$Source, [string]$Destination, [switch]$Manual)

# Called from the verified service. Also dot-sourced by file/lock regression tests.
function Invoke-LertaroUpdateCopy([string]$Source, [string]$Destination, [string]$Backup) {
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\') + '\'
    $targetRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    $backupRoot = [IO.Path]::GetFullPath($Backup).TrimEnd('\') + '\'
    if ($sourceRoot -eq $targetRoot -or $backupRoot -eq $targetRoot -or
        $targetRoot.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $backupRoot.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $sourceRoot.StartsWith($backupRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update directories overlap.' }
    $changed = [Collections.Generic.List[object]]::new()
    try {
        $entries = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -Force -ErrorAction Stop)
        if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Update payload contains a link.' }
        foreach ($file in $entries | Where-Object { !$_.PSIsContainer } | Sort-Object FullName) {
            $relative = $file.FullName.Substring($sourceRoot.Length)
            if ($relative -match '^(Data|update-payload)(\\|$)' -or $relative -in @('update.lock', 'update-result.json', '.permissions-version')) { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
            if (!$target.StartsWith($targetRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path escaped the installation.' }
            $saved = Join-Path $backupRoot $relative
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
            $temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
            $existed = [IO.File]::Exists($target)
            $recorded = $false
            try {
                [IO.File]::Copy($file.FullName, $temporary, $false)
                if ($existed) {
                    try { [IO.File]::Replace($temporary, $target, $saved) }
                    catch {
                        # ponytail: ReplaceFile also needs DELETE on the target, which endpoint protection,
                        # controlled folder access and some sandboxed hosts deny while a plain rename works.
                        # Moving the old file aside first keeps the rollback contract; a genuinely locked
                        # file still fails here because the rename needs the same access.
                        [IO.File]::Move($target, $saved)
                        $changed.Add(@{ Target = $target; Backup = $saved; Existed = $true })
                        $recorded = $true
                        [IO.File]::Move($temporary, $target)
                    }
                }
                else { [IO.File]::Move($temporary, $target) }
                if (!$recorded) { $changed.Add(@{ Target = $target; Backup = $saved; Existed = $existed }) }
            } finally {
                if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
            }
        }
    } catch {
        $copyError = $_
        $rollbackErrors = [Collections.Generic.List[string]]::new()
        for ($i = $changed.Count - 1; $i -ge 0; $i--) {
            $item = $changed[$i]
            try {
                if ($item.Existed) {
                    try { [IO.File]::Replace($item.Backup, $item.Target, [NullString]::Value) }
                    catch {
                        # ponytail: same ReplaceFile restriction as the forward copy; the backup survives
                        # at $item.Backup if the move fails, which is what the message points at.
                        if ([IO.File]::Exists($item.Target)) { [IO.File]::Delete($item.Target) }
                        [IO.File]::Move($item.Backup, $item.Target)
                    }
                }
                else { [IO.File]::Delete($item.Target) }
            } catch { $rollbackErrors.Add($_.Exception.Message) }
        }
        if ($rollbackErrors.Count) { throw "ROLLBACK FAILED: $($rollbackErrors -join '; '). Recovery files: $backupRoot" }
        throw "Update failed at '$target': $($copyError.Exception.Message)"
    }
}

# A name is a pattern, so one call lists several: Get-Process -Name accepts an array, but a parameter
# typed [string] would join the array into "Lertaro.App lff" and match nothing at all.
function Get-LertaroProcessHere([string[]]$Name, [string]$Root) {
    @(foreach ($processName in $Name) {
        Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object {
            # A process of another account (the LocalSystem service) can refuse to reveal its path.
            # Reading it anyway would abort the whole update under $ErrorActionPreference = 'Stop'.
            $here = $false
            try { $here = $_.Path -and [IO.Path]::GetDirectoryName($_.Path).TrimEnd('\') -eq $Root } catch { }
            $here
        }
    })
}

# A process the SCM owns is waited for, never terminated: the copier runs as an elevated user, and an
# elevated user cannot open a LocalSystem process for termination, so Kill on it only ever reports
# "Access is denied" and hides which process is standing in the way.
#
# A minute, not fifteen seconds: the service writes its index out on the way down, and on a machine in the
# middle of a full rebuild that is the difference between an update and a report that it could not stop it.
function Wait-LertaroProcessExit([string]$Name, [string]$Root, [int]$TimeoutMs = 60000, [int]$PollMs = 100) {
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $alive = Get-LertaroProcessHere $Name $Root
        if (!$alive) { return }
        if ($deadline.Elapsed.TotalMilliseconds -ge $TimeoutMs) {
            throw ("{0} (PID {1}) did not exit." -f ($Name -join ','), (($alive.Id | Sort-Object) -join ','))
        }
        Start-Sleep -Milliseconds $PollMs
    }
}

function Stop-LertaroService {
    param([string]$Sc, [int]$TimeoutMs = 60000, [int]$PollMs = 100)
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        & $Sc stop LertaroService | Out-Null
        $exit = $LASTEXITCODE
        # 1061 means the SCM is already processing a stop; retry until it accepts a stop or reports stopped.
        if ($exit -in 0, 1060, 1062) { return }
        if ($exit -ne 1061 -or $deadline.Elapsed.TotalMilliseconds -ge $TimeoutMs) { throw "Could not stop service: $exit" }
        Start-Sleep -Milliseconds $PollMs
    }
}

function Start-LertaroService {
    param([string]$Sc, [int]$TimeoutMs = 15000, [int]$PollMs = 100)
    $deadline = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        & $Sc start LertaroService | Out-Null
        $exit = $LASTEXITCODE
        if ($exit -in 0, 1056) { return }
        # A stop from another controller can still make start report 1061 briefly.
        if ($exit -ne 1061 -or $deadline.Elapsed.TotalMilliseconds -ge $TimeoutMs) { throw "Could not restart service: $exit" }
        Start-Sleep -Milliseconds $PollMs
    }
}

function Complete-LertaroUpdate {
    param([hashtable]$Result, [bool]$RestartSafe, [string]$Sc, [string]$TargetRoot, [IDisposable]$Lease)
    $write = {
        $Result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $TargetRoot 'update-result.json') -Encoding UTF8
    }
    try {
        # Written before the service starts, not after: its OnStart relaunches the App, and the App reports
        # this file on the way up, which a write behind the start can lose.
        & $write
        if ($RestartSafe) {
            try { Start-LertaroService $Sc }
            catch {
                $message = $_.Exception.Message
                $Result.Status = 'failed'
                $Result.Message = if ([string]::IsNullOrWhiteSpace($Result.Message)) { $message } else { "$($Result.Message); $message" }
                & $write
            }
        }
    } finally { if ($Lease) { $Lease.Dispose() } }
}

# A folder the user downloaded (portable-updater.bat) is not under the installation's staging area, and
# the copier refuses to read a user-writable source with its own rights. Copying it into the installation
# first keeps one code path for the copy itself, and that path's refusal of links covers this one too.
function Copy-LertaroManualPayload([string]$Source, [string]$TargetRoot) {
    if (!(Test-Path -LiteralPath (Join-Path $TargetRoot 'portable-updater.ps1') -PathType Leaf)) {
        throw 'The destination is not a Lertaro installation.'
    }
    if (!(Test-Path -LiteralPath $Source -PathType Container)) { throw "Update source not found: $Source" }
    $links = @(Get-ChildItem -LiteralPath $Source -Recurse -Force -ErrorAction Stop |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
    if ($links.Count) { throw 'Update payload contains a link.' }
    $staging = Join-Path (Join-Path $TargetRoot 'update-payload') ('manual-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $staging -Recurse -Force -ErrorAction Stop
    }
    return $staging
}

# Payload copies and rollback snapshots left by runs that died before cleaning up after themselves: each
# can be the size of the whole application. Only ones older than a week, because anything newer can belong
# to a copier that is still working -- the copier running this one holds the update lock regardless.
function Clear-LertaroStalePayload([string]$PayloadRoot, [string]$Keep, [int]$KeepDays = 7) {
    if (!(Test-Path -LiteralPath $PayloadRoot)) { return }
    $deadline = [DateTime]::UtcNow.AddDays(-$KeepDays)
    foreach ($item in Get-ChildItem -LiteralPath $PayloadRoot -Directory -Force -ErrorAction SilentlyContinue) {
        if ($item.FullName -eq $Keep -or $item.LastWriteTimeUtc -ge $deadline) { continue }
        Remove-Item -LiteralPath $item.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($MyInvocation.InvocationName -eq '.') { return }
$ErrorActionPreference = 'Stop'
# An elevated PowerShell must not discover modules from a user's custom module search path.
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
$targetRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
$payloadRoot = Join-Path $targetRoot 'update-payload'
if ($sourceRoot -eq [IO.Path]::GetPathRoot($sourceRoot) -or $targetRoot -eq [IO.Path]::GetPathRoot($targetRoot)) { throw 'Invalid update root.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'The updater requires elevation.' }
$sc = Join-Path ([Environment]::GetFolderPath('System')) 'sc.exe'
$backup = Join-Path $payloadRoot ('rollback-' + [Guid]::NewGuid().ToString('N'))
$lease = $null
$restartSafe = $true
# Every failure below has to land here: the App has already been told to exit, so this file is the only
# place the reason survives. The two checks above stay outside because a caller that is not an
# administrator cannot write to the installation folder in the first place.
$result = @{ Status = 'failed'; Time = [DateTime]::UtcNow.ToString('O'); Message = ''; RecoveryDirectory = $backup }
try {
    # Before the trust check below, and after the elevation check above: staging is itself a privileged write.
    if ($Manual -and !$sourceRoot.StartsWith($payloadRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        $sourceRoot = Copy-LertaroManualPayload $sourceRoot $targetRoot
    }
    if (!$sourceRoot.StartsWith($payloadRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Untrusted update source.' }
    $lockFile = Join-Path $targetRoot 'update.lock'
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (!$lease) {
        try { $lease = [IO.File]::Open($lockFile, 'OpenOrCreate', 'ReadWrite', 'None') }
        catch [IO.IOException] {
            if ([DateTime]::UtcNow -ge $deadline) { throw 'Another update is running.' }
            Start-Sleep -Milliseconds 100
        }
    }
    Clear-LertaroStalePayload $payloadRoot $sourceRoot
    Stop-LertaroService $sc
    Wait-LertaroProcessExit 'Lertaro.Service' $targetRoot
    foreach ($process in Get-LertaroProcessHere @('Lertaro.App', 'lff') $targetRoot) {
        try {
            try { $process.Kill() }
            catch { throw "Could not stop $($process.ProcessName) (PID $($process.Id)): $($_.Exception.Message)" }
            if (!$process.WaitForExit(30000)) { throw "$($process.ProcessName) (PID $($process.Id)) did not stop." }
        } finally { $process.Dispose() }
    }
    Invoke-LertaroUpdateCopy $sourceRoot $targetRoot $backup
    $result.Status = 'success'
    # Both targets are checked against this installation's staging root before recursive removal.
    foreach ($completed in @($sourceRoot, $backup)) {
        $resolved = [IO.Path]::GetFullPath($completed)
        if ($resolved.StartsWith($payloadRoot + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
} catch {
    $result.Message = $_.Exception.Message
    $restartSafe = !$result.Message.StartsWith('ROLLBACK FAILED:')
} finally {
    Complete-LertaroUpdate -Result $result -RestartSafe $restartSafe -Sc $sc -TargetRoot $targetRoot -Lease $lease
}
# To the raw error stream rather than Write-Error: under $ErrorActionPreference = 'Stop' an error record is
# itself a terminating error, and the exit code is what the caller (and portable-updater.bat) reads.
if ($result.Status -ne 'success') { [Console]::Error.WriteLine($result.Message); exit 1 }
