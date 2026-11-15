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
4. Double-click Sequential Gallery. With 2+ connected sliders, geometry is on
   the left and AI rendering on the right. With no sliders, AI uses the window.
   One connected slider is invalid. The default grid is 5x5 on each side.
5. Click a candidate to select it, then Next to commit and advance exploration.
   Four committed selections advance to the next plane. Use this shape sends
   the selected geometry image to AI and starts its first matrix.
6. Finish geometry only saves the selected shape. Rendering Finish saves its
   selected crop and its own source shape, applying that source's sliders.
   Results save to Desktop/Guts-in-the-loop-results before the window closes.

For AI rendering, set OPENAI_API_KEY before starting Rhino. Prepare variables,
fixed values, Initial/Negative prompts and rendering grid on the right. The
first AI start freezes this configuration for all shapes in the window.
AI-only mode captures the viewport on Start. Combined mode waits for an
explicit shape handoff. Each shape keeps its own AI exploration and reference;
returning to the same geometry node and cell reuses its existing AI history.
Finish saves the crop, source image, exact prompt and conditions; combined
results also include source geometry variables, even if the left side differs.
There is no additional generation or upscaling. API usage applies.
Retry repeats failed conditions; Regenerate adds a variant of the same conditions.
Cancel stops waiting locally and does not guarantee remote cancellation.

Evaluate and History tabs show current candidates and branching exploration.
History has geometry and AI columns with cross-links for shape handoffs.
Select a node to preview; Resume from here restores only that side's state
without new generation. Repeating an existing cell choice reuses its child.
History is discarded when the window closes; result files are not session saves.

Slider precision/type is preserved. Existing Fillet Edge errors in the
provided sample are intentionally left unchanged. The sample uses the
Fillet Edge component already installed on the development machine.
Cancel / Close keeps current slider values. Out-of-range cells cannot be selected.

Source, build instructions and Zed debug settings are in the repository README.
'@ | Set-Content "$package/INSTALL.txt" -Encoding utf8
    Compress-Archive -Path "$package/*" -DestinationPath artifacts/SequentialGallery-PoC.zip -Force
    Write-Host 'Created artifacts/SequentialGallery-PoC.zip'
} finally { Pop-Location }
