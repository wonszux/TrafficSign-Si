param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\demo-data')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $destination) {
    throw "Output directory already exists: $destination. Choose a new path."
}

# Original synthetic patterns for a technical smoke test. These are not traffic signs.
$random = [System.Random]::new(42)
foreach ($label in @('horizontal', 'vertical')) {
    $classDirectory = Join-Path $destination $label
    [void][System.IO.Directory]::CreateDirectory($classDirectory)
    for ($index = 0; $index -lt 24; $index++) {
        $bitmap = [System.Drawing.Bitmap]::new(64, 64)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::Black, 5)
        try {
            $graphics.Clear([System.Drawing.Color]::White)
            $offset = $random.Next(-3, 4)
            foreach ($coordinate in @(16, 32, 48)) {
                $position = $coordinate + $offset
                if ($label -eq 'horizontal') {
                    $graphics.DrawLine($pen, 6, $position, 58, $position)
                }
                else {
                    $graphics.DrawLine($pen, $position, 6, $position, 58)
                }
            }
            # Unique deterministic noise prevents byte-identical samples.
            for ($pixel = 0; $pixel -lt 70; $pixel++) {
                $gray = $random.Next(80, 230)
                $bitmap.SetPixel($random.Next(64), $random.Next(64), [System.Drawing.Color]::FromArgb($gray, $gray, $gray))
            }
            $bitmap.Save((Join-Path $classDirectory ('{0:D3}.png' -f $index)), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $pen.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
Write-Output "Created 48 synthetic horizontal/vertical patterns in $destination"
Write-Output 'Use only to verify the pipeline, never as a traffic-sign accuracy benchmark.'
