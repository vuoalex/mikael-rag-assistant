<#
.SYNOPSIS
    Ingest the test transcripts into the RAG backend.

.DESCRIPTION
    Reads testdata/transkript.md, splits it into individual transcripts (each
    delimited by a `---` front-matter block with `title:` and `source:`), and
    POSTs every transcript to the /ingest endpoint as JSON matching the
    IngestRequest contract: { "source": ..., "title": ..., "text": ... }.

    See PLAN.md section 5 for the endpoint contract.

.PARAMETER BaseUrl
    Base URL of the running API. Defaults to http://localhost:5218.

.PARAMETER InputFile
    Path to the transcript markdown file. Defaults to testdata/transkript.md
    resolved relative to the repository root.

.EXAMPLE
    ./scripts/ingest-testdata.ps1

.EXAMPLE
    ./scripts/ingest-testdata.ps1 -BaseUrl http://localhost:5218
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5218',
    [string]$InputFile
)

$ErrorActionPreference = 'Stop'

# Resolve the input file relative to the repo root (parent of this script's dir)
# so the script works regardless of the current working directory.
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $InputFile) {
    $InputFile = Join-Path $repoRoot 'testdata/transkript.md'
}

if (-not (Test-Path -LiteralPath $InputFile)) {
    throw "Input file not found: $InputFile"
}

$content = Get-Content -LiteralPath $InputFile -Raw -Encoding UTF8

# Each transcript is a YAML-style front-matter block followed by its body:
#
#   ---
#   title: "..."
#   source: transcript
#   ---
#
#   <body text...>
#
# The body runs until the next front-matter header or end of file. Leading
# comment lines (starting with #) before the first block are ignored
# automatically because they sit outside any match.
$pattern = '(?ms)^---\s*\r?\n' +
           'title:\s*(?<title>.*?)\s*\r?\n' +
           'source:\s*(?<source>\S+)\s*\r?\n' +
           '---\s*\r?\n' +
           '(?<body>.*?)' +
           '(?=^---\s*\r?\ntitle:|\z)'

$transcripts = [regex]::Matches($content, $pattern)

if ($transcripts.Count -eq 0) {
    throw "No transcripts found in $InputFile. Check the file format (--- / title: / source: ---)."
}

$ingestUrl = "$($BaseUrl.TrimEnd('/'))/ingest"
Write-Host "Found $($transcripts.Count) transcript(s). Posting to $ingestUrl" -ForegroundColor Cyan

$success = 0
$failed = 0

foreach ($transcript in $transcripts) {
    # Strip optional surrounding double quotes from the title.
    $title = $transcript.Groups['title'].Value.Trim().Trim('"')
    $source = $transcript.Groups['source'].Value.Trim()
    $text = $transcript.Groups['body'].Value.Trim()

    if ([string]::IsNullOrWhiteSpace($text)) {
        Write-Warning "Skipping '$title' - empty body."
        continue
    }

    $payload = @{
        source = $source
        title  = $title
        text   = $text
    } | ConvertTo-Json -Depth 3

    try {
        $response = Invoke-RestMethod -Uri $ingestUrl -Method Post `
            -ContentType 'application/json; charset=utf-8' `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($payload))

        Write-Host ("  OK  {0} -> documentId={1}, chunkCount={2}" -f `
            $title, $response.documentId, $response.chunkCount) -ForegroundColor Green
        $success++
    }
    catch {
        Write-Host ("  ERR {0} -> {1}" -f $title, $_.Exception.Message) -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
Write-Host "Done. $success ingested, $failed failed." -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
}
