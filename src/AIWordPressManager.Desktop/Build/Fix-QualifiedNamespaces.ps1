param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDirectory
)

$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)

function Update-FileText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][scriptblock]$Transform,
        [Parameter(Mandatory = $true)][string]$SuccessMessage
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Source file was not found: $Path"
    }

    $content = [System.IO.File]::ReadAllText($Path)
    $updated = & $Transform $content
    if ($updated -ne $content) {
        [System.IO.File]::WriteAllText($Path, $updated, $utf8)
        Write-Host $SuccessMessage
    }
}

$executionCenter = Join-Path $ProjectDirectory 'ViewModels\ExecutionCenterViewModel.cs'
Update-FileText -Path $executionCenter -SuccessMessage 'Normalized ExecutionCenter global namespace references.' -Transform {
    param($content)
    [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        '(?<!global::)AIWordPressManager\.Application\.Common\.Results\.Result',
        'global::AIWordPressManager.Application.Common.Results.Result')
}

$administrationFiles = @(
    (Join-Path $ProjectDirectory 'SystemAdministrationExperience.cs'),
    (Join-Path $ProjectDirectory 'UserAdministrationExperience.cs')
)

foreach ($file in $administrationFiles) {
    Update-FileText -Path $file -SuccessMessage "Normalized SQLite transaction type in $([System.IO.Path]::GetFileName($file))." -Transform {
        param($content)
        $content.Replace(
            'await using var transaction = await connection.BeginTransactionAsync();',
            'await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync();')
    }
}

Write-Host 'Desktop source normalization completed.'
