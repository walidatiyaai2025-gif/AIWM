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

$systemLoginExperience = Join-Path $ProjectDirectory 'SystemLoginExperience.cs'
Update-FileText -Path $systemLoginExperience -SuccessMessage 'Normalized SQLite transaction type in SystemLoginExperience.cs.' -Transform {
    param($content)
    $content.Replace(
        'await using var transaction = await connection.BeginTransactionAsync(cancellationToken);',
        'await using var transaction = connection.BeginTransaction();')
}

$demoDataExperience = Join-Path $ProjectDirectory 'DemoDataInAppExperience.cs'
Update-FileText -Path $demoDataExperience -SuccessMessage 'Normalized in-app demo data launcher WPF ZIndex.' -Transform {
    param($content)
    $pattern = 'FontWeight = FontWeights\.SemiBold,\s*Panel = \{ ZIndex = 5000 \}\s*};\s*button\.Click'
    $replacement = "FontWeight = FontWeights.SemiBold`r`n        };`r`n        Panel.SetZIndex(button, 5000);`r`n`r`n        button.Click"
    [System.Text.RegularExpressions.Regex]::Replace($content, $pattern, $replacement)
}

Update-FileText -Path $demoDataExperience -SuccessMessage 'Normalized escaped TimeSpan formats in DemoDataInAppExperience.cs.' -Transform {
    param($content)
    $content.Replace('hh\:mm\:ss', 'hh\\:mm\\:ss')
}

$appCodeBehind = Join-Path $ProjectDirectory 'App.xaml.cs'
Update-FileText -Path $appCodeBehind -SuccessMessage 'Connected MainWindow to the active SQLite database path for demo data.' -Transform {
    param($content)
    $pattern = 'MainWindow = _host\.Services\.GetRequiredService<MainWindow>\(\);\s*MainWindow\.Show\(\);'
    $replacement = "MainWindow = _host.Services.GetRequiredService<MainWindow>();`r`n            MainWindow.Tag = databasePath;`r`n            MainWindow.Show();"
    [System.Text.RegularExpressions.Regex]::Replace($content, $pattern, $replacement)
}

Write-Host 'Desktop source normalization completed.'
