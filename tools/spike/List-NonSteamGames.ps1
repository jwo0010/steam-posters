<#
.SYNOPSIS
    Step 0 spike (READ-ONLY): list non-Steam games for every Steam account on this PC.

.DESCRIPTION
    Reads the Steam install path, loginusers.vdf, each account's shortcuts.vdf (binary VDF)
    and grid folder. Prints each non-Steam game with its stored appid, the computed
    shortcut id and which artwork files already exist. Never writes to the Steam folder.
    Works on Windows PowerShell 5.1 and PowerShell 7.
#>
[CmdletBinding()]
param(
    [string]$SteamPath
)

$ErrorActionPreference = 'Stop'
$SteamId64Base = [UInt64]76561197960265728

# --- Steam path -------------------------------------------------------------
if (-not $SteamPath) {
    try {
        $SteamPath = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath
    } catch { }
    if (-not $SteamPath) { $SteamPath = 'C:\Program Files (x86)\Steam' }
}
$SteamPath = $SteamPath -replace '/', '\'
Write-Output "Steam path: $SteamPath"
if (-not (Test-Path -LiteralPath $SteamPath)) { throw "Steam folder not found: $SteamPath" }

# --- CRC32 (IEEE, same as zlib) --------------------------------------------
# Math is done in Int64 and masked, because Windows PowerShell 5.1 parses hex
# literals such as 0xFFFFFFFF as negative Int32 values.
$script:Mask32 = [Int64]4294967295
$script:CrcTable = New-Object 'Int64[]' 256
for ($n = 0; $n -lt 256; $n++) {
    [Int64]$c = $n
    for ($k = 0; $k -lt 8; $k++) {
        if ($c -band 1) { $c = [Int64]3988292384 -bxor ($c -shr 1) } else { $c = $c -shr 1 }
    }
    $script:CrcTable[$n] = $c
}
function Get-Crc32([byte[]]$Bytes) {
    [Int64]$crc = $script:Mask32
    foreach ($b in $Bytes) {
        $crc = $script:CrcTable[($crc -bxor $b) -band 255] -bxor ($crc -shr 8)
    }
    return [UInt32](($crc -bxor $script:Mask32) -band $script:Mask32)
}
function Get-ShortcutId([string]$Exe, [string]$AppName) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Exe + $AppName)
    return [UInt32](([Int64](Get-Crc32 $bytes)) -bor [Int64]2147483648)
}
# --- Text VDF (loginusers.vdf) ---------------------------------------------
function Read-LoginUsers([string]$Path) {
    $result = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $result }
    $current = $null
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $tokens = [regex]::Matches($line, '"((?:[^"\\]|\\.)*)"') | ForEach-Object { $_.Groups[1].Value }
        $tokens = @($tokens)
        if ($tokens.Count -eq 1 -and $tokens[0] -match '^\d{17}$') {
            $current = @{ SteamId64 = $tokens[0] }
            $result[$tokens[0]] = $current
        } elseif ($tokens.Count -eq 2 -and $current) {
            $current[$tokens[0]] = $tokens[1]
        }
    }
    return $result
}

# --- Binary VDF (shortcuts.vdf) ---------------------------------------------
function Read-CString([byte[]]$Data, [ref]$Pos) {
    $start = $Pos.Value
    while ($Pos.Value -lt $Data.Length -and $Data[$Pos.Value] -ne 0) { $Pos.Value++ }
    if ($Pos.Value -ge $Data.Length) { throw "Unterminated string at offset $start" }
    $s = [System.Text.Encoding]::UTF8.GetString($Data, $start, $Pos.Value - $start)
    $Pos.Value++  # skip null
    return $s
}
function Read-BinaryVdfMap([byte[]]$Data, [ref]$Pos) {
    # Returns an ordered dictionary; ends at 0x08 (or end of data).
    $map = [ordered]@{}
    while ($Pos.Value -lt $Data.Length) {
        $type = $Data[$Pos.Value]; $Pos.Value++
        if ($type -eq 0x08) { return $map }
        $key = Read-CString $Data $Pos
        switch ($type) {
            0x00 { $map[$key] = Read-BinaryVdfMap $Data $Pos }
            0x01 { $map[$key] = Read-CString $Data $Pos }
            0x02 {
                $map[$key] = [BitConverter]::ToUInt32($Data, $Pos.Value)
                $Pos.Value += 4
            }
            default { throw ("Unknown VDF type 0x{0:X2} for key '{1}' at offset {2}" -f $type, $key, ($Pos.Value)) }
        }
    }
    return $map
}
function Get-Field($Map, [string]$Name) {
    # Keys vary in case (appname vs AppName); ordered dictionary lookup is case-sensitive, so search.
    foreach ($k in $Map.Keys) { if ($k -ieq $Name) { return $Map[$k] } }
    return $null
}

# --- Accounts ---------------------------------------------------------------
$loginUsers = Read-LoginUsers (Join-Path $SteamPath 'config\loginusers.vdf')
$userdata = Join-Path $SteamPath 'userdata'
if (-not (Test-Path -LiteralPath $userdata)) { throw "No userdata folder: $userdata" }

$accounts = Get-ChildItem -LiteralPath $userdata -Directory | Where-Object { $_.Name -match '^\d+$' }
Write-Output ""
Write-Output "Accounts in userdata: $($accounts.Count)"
foreach ($acc in $accounts) {
    $id64 = ([UInt64]$acc.Name + $SteamId64Base).ToString()
    $lu = $loginUsers[$id64]
    $persona = if ($lu) { $lu['PersonaName'] } else { '(not in loginusers.vdf)' }
    $recent = if ($lu -and $lu['MostRecent'] -eq '1') { ' [MostRecent]' } else { '' }
    Write-Output ("  {0}  SteamID64={1}  PersonaName={2}{3}" -f $acc.Name, $id64, $persona, $recent)
}

foreach ($acc in $accounts) {
    $id64 = ([UInt64]$acc.Name + $SteamId64Base).ToString()
    $lu = $loginUsers[$id64]
    $persona = if ($lu) { $lu['PersonaName'] } else { '?' }
    Write-Output ""
    Write-Output ("=== Account {0} ({1}) ===" -f $acc.Name, $persona)

    $vdfPath = Join-Path $acc.FullName 'config\shortcuts.vdf'
    if (-not (Test-Path -LiteralPath $vdfPath)) { Write-Output "  No shortcuts.vdf"; continue }
    $gridDir = Join-Path $acc.FullName 'config\grid'
    $gridFiles = if (Test-Path -LiteralPath $gridDir) { Get-ChildItem -LiteralPath $gridDir -File } else { @() }

    $data = [System.IO.File]::ReadAllBytes($vdfPath)
    $pos = 0
    $root = Read-BinaryVdfMap $data ([ref]$pos)
    $shortcuts = Get-Field $root 'shortcuts'
    if (-not $shortcuts) { Write-Output "  shortcuts.vdf has no 'shortcuts' map"; continue }
    Write-Output ("  shortcuts.vdf: {0} bytes, {1} entries" -f $data.Length, $shortcuts.Count)

    foreach ($idx in $shortcuts.Keys) {
        $s = $shortcuts[$idx]
        $name = Get-Field $s 'AppName'
        $exe = Get-Field $s 'Exe'
        $stored = Get-Field $s 'appid'
        $computed = Get-ShortcutId $exe $name
        $storedText = if ($null -ne $stored) { [string][UInt32]$stored } else { 'missing' }
        $match = if ($null -eq $stored) { 'n/a' } elseif ([UInt32]$stored -eq $computed) { 'yes' } else { 'NO' }
        $artId = if ($null -ne $stored) { [UInt32]$stored } else { $computed }

        Write-Output ""
        Write-Output ("  [{0}] {1}" -f $idx, $name)
        Write-Output ("      Exe:      {0}" -f $exe)
        Write-Output ("      StartDir: {0}" -f (Get-Field $s 'StartDir'))
        Write-Output ("      icon:     {0}" -f (Get-Field $s 'icon'))
        Write-Output ("      appid stored={0}  computed={1}  match={2}" -f $storedText, $computed, $match)

        $kinds = [ordered]@{
            'poster (p)' = "^$artId" + 'p\.'
            'wide'       = "^$artId\."
            'hero'       = "^$artId" + '_hero\.'
            'logo'       = "^$artId" + '_logo\.'
            'icon'       = "^$artId" + '_icon\.'
        }
        $found = foreach ($kind in $kinds.Keys) {
            $hits = @($gridFiles | Where-Object { $_.Name -match $kinds[$kind] } | ForEach-Object { $_.Name })
            if ($hits.Count) { "{0}: {1}" -f $kind, ($hits -join ', ') }
        }
        $artText = if ($found) { ($found -join '; ') } else { 'none' }
        Write-Output ("      grid art (id {0}): {1}" -f $artId, $artText)
    }
}
