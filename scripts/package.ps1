$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $package = 'artifacts/SequentialGallery'
    if (!(Test-Path "$package/SequentialGallery.gha")) { throw 'Build the plugin first.' }
    if (!(Test-Path 'samples/Facade.ghx')) { throw 'Prepare the sample in Rhino first.' }
    New-Item -ItemType Directory -Force "$package/samples" | Out-Null
    Copy-Item samples/Facade.ghx "$package/samples"
    Copy-Item LICENSE "$package/LICENSE.txt"
    @'
Sequential Gallery — Windows x64 / Rhino 8.36+ / .NET 10

1. Close Rhino. Copy SequentialGallery.gha and SequentialGallery.Native.dll
   to the same folder in Grasshopper's Libraries directory. Keep licenses/.
2. Restart Rhino and open Grasshopper.
3. Open samples/Facade.ghx, or connect Number Sliders directly to the
   Variables input of Params > Util > Sequential Gallery.
4. Double-click Sequential Gallery, choose Rows and Columns, frame the active
   Rhino viewport, then click Start exploration. The default grid is 5x5.
5. Select preferred images. Four selections advance to the next plane.
6. Finish with selected design restores the last selected shape.

For AI rendering, set OPENAI_API_KEY before starting Rhino. Add Params > Util >
AI Rendering, double-click it, select variables and grid size, then Start.
Select a cell and press Next to explore. Finish previews the selected crop;
Save PNG saves it without additional generation or upscaling. API usage applies.
Retry repeats failed conditions; Regenerate replaces the same matrix conditions.
Cancel stops waiting locally and does not guarantee remote cancellation.

Slider precision/type is preserved. Existing Fillet Edge errors in the
provided sample are intentionally left unchanged. The sample uses the
Fillet Edge component already installed on the development machine.
Cancel / Close keeps current slider values. Out-of-range cells cannot be selected.

Source, build instructions and Zed debug settings are in the repository README.
'@ | Set-Content "$package/INSTALL.txt" -Encoding utf8
    Compress-Archive -Path "$package/*" -DestinationPath artifacts/SequentialGallery-PoC.zip -Force
    Write-Host 'Created artifacts/SequentialGallery-PoC.zip'
} finally { Pop-Location }
