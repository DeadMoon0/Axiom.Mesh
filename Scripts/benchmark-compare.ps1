<#
.SYNOPSIS
    Compares BenchmarkDotNet results with the committed numbers and warns on a regression; never fails.

.DESCRIPTION
    benchmarks.yml runs a short list of the micro benchmarks and calls this script. Each benchmark whose mean is more
    than -Factor times its committed mean (Benchmarks/Axiom.Mesh.Benchmarks/baseline.json) is reported as a GitHub
    Actions warning; one without a committed number as a notice. The committed numbers were measured on the machine
    Documentation/BENCHMARKS.md names, so on another machine the comparison is coarse by design: it catches an order of
    magnitude, not a percentage. With -Write it replaces the committed numbers with the results instead.

.PARAMETER Results
    The folder BenchmarkDotNet wrote its JSON reports to (run with --exporters json).

.PARAMETER Baseline
    The committed numbers.

.PARAMETER Factor
    How many times slower than committed counts as a regression.

.PARAMETER Write
    Write the results as the new committed numbers.
#>
param(
    [string] $Results = "BenchmarkDotNet.Artifacts/results",
    [string] $Baseline = "Benchmarks/Axiom.Mesh.Benchmarks/baseline.json",
    [double] $Factor = 2.0,
    [switch] $Write
)

$ErrorActionPreference = "Stop"
$measured = [ordered]@{}
foreach ($report in Get-ChildItem -Path $Results -Filter "*.json" -ErrorAction SilentlyContinue) {
    $document = Get-Content $report.FullName -Raw | ConvertFrom-Json
    foreach ($benchmark in $document.Benchmarks) {
        if ($null -ne $benchmark.Statistics -and $null -ne $benchmark.Statistics.Mean) {
            $measured[$benchmark.FullName] = [math]::Round([double]$benchmark.Statistics.Mean, 1)
        }
    }
}

if ($measured.Count -eq 0) {
    Write-Output "::warning title=Benchmarks::No BenchmarkDotNet JSON results under $Results; nothing compared."
    exit 0
}

if ($Write) {
    $sorted = [ordered]@{}
    foreach ($name in ($measured.Keys | Sort-Object)) { $sorted[$name] = $measured[$name] }
    [ordered]@{ unit = "ns (mean)"; benchmarks = $sorted } | ConvertTo-Json -Depth 4 | Set-Content -Path $Baseline -Encoding utf8
    Write-Output "Wrote $($measured.Count) committed numbers to $Baseline."
    exit 0
}

$committed = @{}
if (Test-Path $Baseline) {
    foreach ($property in (Get-Content $Baseline -Raw | ConvertFrom-Json).benchmarks.PSObject.Properties) {
        $committed[$property.Name] = [double]$property.Value
    }
}

foreach ($name in $measured.Keys) {
    $now = $measured[$name]
    if (-not $committed.ContainsKey($name)) {
        Write-Output "::notice title=Benchmark without a committed number::$name took $now ns."
        continue
    }
    $ratio = $now / $committed[$name]
    $line = [string]::Format([cultureinfo]::InvariantCulture, "{0}: {1:N1} ns, {2:N2} x the committed {3:N1} ns", $name, $now, $ratio, $committed[$name])
    if ($ratio -gt $Factor) {
        Write-Output "::warning title=Benchmark regression above ${Factor}x::$line"
    } else {
        Write-Output $line
    }
}

exit 0
