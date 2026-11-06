<#
  Builds the three Word documents from the HTML sources in this folder.
    Practicum_Step_By_Step_Guide.docx
    <StudentNumber>_CLDV7112_Practicum.docx
    Theory_Answers_Harvard.docx
  Usage:  powershell -ExecutionPolicy Bypass -File build-docs.ps1 [-StudentNumber 12345678]
  Needs Microsoft Word installed (uses COM).
#>
param(
    [string]$StudentNumber = '',
    [switch]$HtmlOnly   # write browser-ready .html files into docs\ and skip Word
)

$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$out = Split-Path $src -Parent
$tmp = Join-Path $env:TEMP 'Agristream_docs_build'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

function Read-Utf8([string]$p) { [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }

# ---------- figure numbering ----------
$figs = Read-Utf8 (Join-Path $src 'figures.json') | ConvertFrom-Json
$map = @{}
$n = 0
foreach ($f in $figs) {
    if ($f.pair -ne 'b') { $n++ }
    $label = "$n"
    if ($f.pair) { $label = "$n$($f.pair)" }
    $map[$f.id] = [pscustomobject]@{ Label = $label; Capture = $f.capture; Caption = $f.caption; Optional = [bool]$f.optional; Id = $f.id }
}

function Esc([string]$s) { [System.Net.WebUtility]::HtmlEncode($s) }

function Expand-Figures([string]$html, [string]$mode) {
    $rx = [regex]'\{\{FIG:([a-z0-9\-]+)\}\}'
    return $rx.Replace($html, {
        param($m)
        $id = $m.Groups[1].Value
        if (-not $map.ContainsKey($id)) { throw "Unknown figure id: $id" }
        $f = $map[$id]
        $opt = ''
        if ($f.Optional) { $opt = ' (optional)' }
        if ($mode -eq 'guide') {
            return '<p class="shotg"><b>SCREENSHOT - Figure ' + $f.Label + $opt + '</b> <span style="color:#555">[' + $f.Id + ']</span><br/>Capture: ' + (Esc $f.Capture) + '</p>'
        }
        return '<p class="shoty"><span style="background:yellow">[Insert screenshot' + $opt + ': ' + (Esc $f.Capture) + ']</span></p>' +
               '<p class="MsoCaption">Figure ' + $f.Label + ': ' + (Esc $f.Caption) + '</p>'
    })
}

function Build-Checklist {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('<table class="grid"><tr><th>Figure</th><th>Id</th><th>What to capture</th><th>Needed?</th><th>Done</th></tr>')
    foreach ($f in $figs) {
        $k = $map[$f.id]
        $need = 'Yes'
        if ($k.Optional) { $need = 'Optional' }
        [void]$sb.Append('<tr><td>' + $k.Label + '</td><td>' + $k.Id + '</td><td>' + (Esc $k.Capture) + '</td><td>' + $need + '</td><td>&#9744;</td></tr>')
    }
    [void]$sb.Append('</table>')
    return $sb.ToString()
}

$css = @'
<style>
body { font-family: Calibri, Arial, sans-serif; font-size: 11pt; line-height: 1.25; }
h1 { font-size: 18pt; color: #1F3864; margin-top: 20pt; }
h2 { font-size: 14pt; color: #2F5496; margin-top: 14pt; }
h3 { font-size: 12pt; color: #2F5496; margin-top: 10pt; }
p.MsoCaption { font-size: 9pt; font-style: italic; color: #44546A; margin-top: 2pt; margin-bottom: 10pt; }
p.shoty { margin-top: 8pt; margin-bottom: 0pt; }
p.shotg { background: #E2EFDA; border: 1px solid #70AD47; padding: 6pt; margin-top: 8pt; margin-bottom: 8pt; }
p.warn  { background: #FDE9E7; border: 1px solid #C00000; padding: 6pt; }
p.tip   { background: #DEEAF6; border: 1px solid #2E75B6; padding: 6pt; }
p.fix   { background: #FFF2CC; border: 1px solid #BF9000; padding: 6pt; }
table.grid { border-collapse: collapse; width: 100%; }
table.grid td, table.grid th { border: 1px solid #999; padding: 3pt; font-size: 9.5pt; vertical-align: top; }
table.grid th { background: #D9E2F3; text-align: left; }
span.fill { background: yellow; }
pre, code { font-family: Consolas, 'Courier New', monospace; font-size: 9pt; }
pre { background: #F2F2F2; border: 1px solid #BBB; padding: 4pt; }
</style>
'@

function Wrap-Html([string]$body, [string]$title) {
    return '<html><head><meta charset="utf-8"/><title>' + (Esc $title) + '</title>' + $css + '</head><body>' + $body + '</body></html>'
}

function Apply-Student([string]$html) {
    if ($StudentNumber) {
        $html = $html -replace '<span class="fill">\[STUDENT NUMBER\]</span>', $StudentNumber
        $html = $html -replace '\[http://STUDENT_NUMBER\.azurewebsites\.net\]', "http://$StudentNumber.azurewebsites.net"
        $html = $html -replace 'STUDENT_NUMBER', $StudentNumber
    }
    return $html
}

# ---------- assemble HTML ----------
$theory = Read-Utf8 (Join-Path $src 'theory.html')
$refs = Read-Utf8 (Join-Path $src 'references.html')

$sub = Read-Utf8 (Join-Path $src 'submission.html')
$sub = $sub.Replace('<!--THEORY-->', $theory).Replace('<!--REFERENCES-->', $refs)
$sub = Apply-Student (Expand-Figures $sub 'submission')

$guide = Read-Utf8 (Join-Path $src 'guide.html')
$guide = $guide.Replace('{{CHECKLIST}}', (Build-Checklist))
$guide = Apply-Student (Expand-Figures $guide 'guide')

$subName = 'STUDENT_NUMBER'
if ($StudentNumber) { $subName = $StudentNumber }

$jobs = @(
    @{ Html = (Wrap-Html $sub 'CLDV7112 Practicum'); Docx = "${subName}_CLDV7112_Practicum.docx" },
    @{ Html = (Wrap-Html $guide 'Practicum step-by-step guide'); Docx = 'Practicum_Step_By_Step_Guide.docx' },
    @{ Html = (Wrap-Html ($theory + $refs) 'Theory answers'); Docx = 'Theory_Answers_Harvard.docx' }
)

if ($HtmlOnly) {
    foreach ($j in $jobs) {
        $target = Join-Path $out ([System.IO.Path]::GetFileNameWithoutExtension($j.Docx) + '.html')
        [System.IO.File]::WriteAllText($target, $j.Html, (New-Object System.Text.UTF8Encoding($true)))
        Write-Host "Wrote $target"
    }
    return
}

# ---------- Word COM ----------
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    foreach ($j in $jobs) {
        $htmlPath = Join-Path $tmp ([System.IO.Path]::GetFileNameWithoutExtension($j.Docx) + '.html')
        [System.IO.File]::WriteAllText($htmlPath, $j.Html, (New-Object System.Text.UTF8Encoding($true)))
        $target = Join-Path $out $j.Docx
        $doc = $word.Documents.Open($htmlPath, $false, $true)
        try {
            try {
                $doc.SaveAs2($target, 16)
            } catch {
                $alt = [System.IO.Path]::ChangeExtension($target, $null).TrimEnd('.') + '_v2.docx'
                Write-Warning "Could not overwrite $($j.Docx) (open or locked). Saving as $(Split-Path $alt -Leaf)"
                $doc.SaveAs2($alt, 16)
                $target = $alt
            }
        } finally {
            $doc.Close(0)
        }
        Write-Host "Wrote $target"
    }
} finally {
    $word.Quit()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)
}

# word counts for the theory answers
$t = (Read-Utf8 (Join-Path $src 'theory.html')) -split '<h1>' | Where-Object { $_ -match '\S' }
foreach ($blk in $t) {
    $title = ([regex]::Match($blk, '^(.*?)</h1>')).Groups[1].Value
    $plain = [regex]::Replace($blk, '<[^>]+>', ' ')
    $plain = [System.Net.WebUtility]::HtmlDecode($plain)
    $words = ($plain -split '\s+' | Where-Object { $_ }).Count
    Write-Host ("{0}: about {1} words" -f $title, $words)
}
