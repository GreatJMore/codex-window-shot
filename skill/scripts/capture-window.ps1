[CmdletBinding(DefaultParameterSetName = 'Match')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Match')]
    [string]$Match,

    [Parameter(Mandatory = $true, ParameterSetName = 'Handle')]
    [string]$Handle,

    [Parameter(Mandatory = $true)]
    [string]$Out,

    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path (Join-Path $PSScriptRoot '..\bin\WindowShot.exe')).Path
$outPath = [System.IO.Path]::GetFullPath($Out)
$outDir = [System.IO.Path]::GetDirectoryName($outPath)
if (-not [string]::IsNullOrWhiteSpace($outDir)) {
    [System.IO.Directory]::CreateDirectory($outDir) | Out-Null
}

function Invoke-WindowShot {
    param([string[]]$Arguments)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $exe
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Arguments = ($Arguments | ForEach-Object {
        '"' + ($_ -replace '"', '\"') + '"'
    }) -join ' '

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        throw 'WindowShot timed out.'
    }

    return $process.ExitCode
}

function Convert-Handle {
    param([string]$Value)
    return ($Value -replace '^0x', '').ToUpperInvariant()
}

try {
    $tempList = Join-Path ([System.IO.Path]::GetTempPath()) (
        'window-shot-' + [guid]::NewGuid().ToString('N') + '.json'
    )

    try {
        if ((Invoke-WindowShot -Arguments @('--list', $tempList)) -ne 0) {
            throw 'Failed to list windows.'
        }

        if (-not (Test-Path -LiteralPath $tempList)) {
            throw 'WindowShot did not create the window list.'
        }

        $data = Get-Content -LiteralPath $tempList -Raw -Encoding UTF8 | ConvertFrom-Json
        $windows = @($data.windows)
    }
    finally {
        Remove-Item -LiteralPath $tempList -Force -ErrorAction SilentlyContinue
    }

    if ($PSCmdlet.ParameterSetName -eq 'Handle') {
        $wanted = Convert-Handle $Handle
        $target = $windows |
            Where-Object { (Convert-Handle $_.handle) -eq $wanted } |
            Select-Object -First 1

        if (-not $target) {
            $target = [pscustomobject]@{
                handle = "0x$wanted"
                title = ''
                process = ''
                width = 0
                height = 0
                minimized = $false
            }
        }
    }
    else {
        $needle = $Match.Trim()
        $candidates = @($windows | Where-Object {
            $_.title -like "*$needle*" -or $_.process -like "*$needle*"
        })

        if ($candidates.Count -eq 0) {
            throw "No window matched '$Match'."
        }

        $target = $candidates |
            Sort-Object -Property @(
                @{ Expression = {
                    if ($_.title -eq $needle) { 0 }
                    elseif ($_.process -eq $needle) { 1 }
                    else { 2 }
                } }
                @{ Expression = { if ($_.minimized) { 1 } else { 0 } } }
                @{ Expression = { $_.title.Length } }
            ) |
            Select-Object -First 1
    }

    if (-not $target) {
        throw 'No matching window was found.'
    }

    if ($target.minimized) {
        throw "Window '$($target.title)' is minimized. Restore it first."
    }

    if ((Invoke-WindowShot -Arguments @('--capture', $target.handle, $outPath)) -ne 0) {
        throw 'WindowShot could not capture the selected window.'
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path -LiteralPath $outPath) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }

    if (-not (Test-Path -LiteralPath $outPath)) {
        throw 'The capture file was not created.'
    }

    $file = Get-Item -LiteralPath $outPath
    if ($file.Length -le 0) {
        throw 'The capture file is empty.'
    }

    [pscustomobject]@{
        ok = $true
        handle = $target.handle
        title = $target.title
        process = $target.process
        width = $target.width
        height = $target.height
        minimized = [bool]$target.minimized
        output = $outPath
    } | ConvertTo-Json -Depth 4
}
catch {
    [pscustomobject]@{
        ok = $false
        error = $_.Exception.Message
        output = $outPath
    } | ConvertTo-Json -Depth 4
    exit 1
}
