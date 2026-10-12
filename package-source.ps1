param([string]$OutputPath)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if (!$OutputPath) { $OutputPath=Join-Path $root 'artifacts\codex-usage-panel-source.zip' }
$output=[IO.Path]::GetFullPath($OutputPath)
$files=@('.gitignore','.gitattributes','README.md','LICENSE','CHANGELOG.md','CONTRIBUTING.md','build.ps1','package-source.ps1')
foreach ($directory in @('src','tests')) {
    $base=Join-Path $root $directory
    $files+=Get-ChildItem -LiteralPath $base -Recurse -File | Where-Object {
        $_.Extension -in @('.cs','.csproj','.ico') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    } | ForEach-Object { [IO.Path]::GetRelativePath($root,$_.FullName) }
}
$files+=@('docs\CONFIGURATION.md','docs\PUBLISHING.md','docs\images\floating-ball.png','docs\images\floating-details.png','docs\images\floating-settings.png')
Add-Type -AssemblyName System.IO.Compression
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$stream=[IO.File]::Open($output,[IO.FileMode]::Create,[IO.FileAccess]::ReadWrite)
try {
    $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
        foreach ($file in $files | Sort-Object -Unique) {
            $entry=$zip.CreateEntry('codex-usage-panel/'+$file.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
            $source=[IO.File]::OpenRead((Join-Path $root $file))
            try { $destination=$entry.Open(); try { $source.CopyTo($destination) } finally { $destination.Dispose() } }
            finally { $source.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
Write-Output "Source archive: $output"
