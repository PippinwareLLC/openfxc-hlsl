$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path | Split-Path -Parent
$proj = Join-Path $root 'src/openfxc-hlsl/openfxc-hlsl.csproj'
$exe = Join-Path $root 'src/openfxc-hlsl/bin/Debug/net8.0/openfxc-hlsl.exe'
$fixture = Join-Path $root 'tests/fixtures/basic-sm2.hlsl'

dotnet build $proj -nologo

function Assert($condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function Read-Json([string]$cmd, [string[]]$argList) {
    $result = & $cmd @argList
    $json = $result -join [Environment]::NewLine
    return $json | ConvertFrom-Json
}

Assert (Test-Path $exe) "CLI executable not found at $exe"
Assert (Test-Path $fixture) "Fixture missing at $fixture"
$sourceText = Get-Content -Raw $fixture

$lex = Read-Json $exe @('lex', '-i', $fixture)
Assert ($lex.formatVersion -eq 1) "Lex formatVersion mismatch"
Assert ($lex.source.fileName -eq 'basic-sm2.hlsl') "Lex source filename mismatch"
Assert ($lex.source.length -eq $sourceText.Length) "Lex source length mismatch"
Assert ($lex.tokens.Count -gt 0) "Lex tokens should not be empty"
Assert ($lex.diagnostics.Count -eq 0) "Lex diagnostics should be empty"
Assert ($lex.tokens[0].kind -eq 'KeywordFloat4') "Lex first token should be KeywordFloat4"

$lexFromStdinJson = $sourceText | & $exe 'lex'
$lexFromStdin = ($lexFromStdinJson -join [Environment]::NewLine) | ConvertFrom-Json
Assert ($lexFromStdin.source.fileName -eq 'stdin') "Lex stdin filename mismatch"
Assert ($lexFromStdin.source.length -ge $sourceText.Length) "Lex stdin source length mismatch"
Assert ($lexFromStdin.tokens.Count -gt 0) "Lex stdin tokens should not be empty"

$parse = Read-Json $exe @('parse', '-i', $fixture)
Assert ($parse.formatVersion -eq 1) "Parse formatVersion mismatch"
Assert ($parse.root.kind -eq 'CompilationUnit') "Parse root kind mismatch"
Assert ($parse.root.span.start -eq 0) "Parse root span start mismatch"
Assert ($parse.root.span.end -eq $sourceText.Length) "Parse root span end mismatch"
Assert ($parse.tokens.Count -gt 0) "Parse tokens should not be empty"
Assert ($parse.diagnostics.Count -eq 0) "Parse diagnostics should be empty"

Write-Output "cli smoke ok"
