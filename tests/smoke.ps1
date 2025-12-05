$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path | Split-Path -Parent

function Assert-Path([string]$path) {
    if (-not (Test-Path $path)) {
        throw "Missing expected path: $path"
    }
}

Assert-Path (Join-Path $root 'docs/TODO.md')
Assert-Path (Join-Path $root 'docs/TDD.md')
Assert-Path (Join-Path $root 'README.md')

Write-Output 'smoke ok'
