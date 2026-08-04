param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDirectory
)

$ErrorActionPreference = 'Stop'
$file = Join-Path $ProjectDirectory 'ViewModels\ExecutionCenterViewModel.cs'

if (-not (Test-Path -LiteralPath $file)) {
    throw "ExecutionCenterViewModel.cs was not found: $file"
}

$content = [System.IO.File]::ReadAllText($file)
$pattern = '(?<!global::)AIWordPressManager\.Application\.Common\.Results\.Result'
$updated = [System.Text.RegularExpressions.Regex]::Replace(
    $content,
    $pattern,
    'global::AIWordPressManager.Application.Common.Results.Result')

if ($updated -ne $content) {
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($file, $updated, $utf8)
    Write-Host 'Normalized ExecutionCenter global namespace references.'
}
else {
    Write-Host 'ExecutionCenter namespace references are already normalized.'
}
