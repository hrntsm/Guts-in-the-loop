# Sequential Gallery for Grasshopper

A hackathon proof of concept for Rhino 8.36+, Windows x64, and .NET 10. Standard Number Sliders define the design variables, and a configurable gallery (5x5 by default) compares Rhino viewport captures. Optimization and zoom coordinates use the [authors' SPS implementation](https://github.com/yuki-koyama/sequential-gallery). The UI and repository documentation are in English.

## Build

Install the .NET 10 SDK, Rhino 8.36 or later, Git, and Visual Studio with **Desktop development with C++** and a Windows SDK. The project references RhinoCommon and the Grasshopper SDK from the installed Rhino directory; no NuGet SDK packages are required.

Run these commands from the repository root:

```powershell
./scripts/bootstrap.ps1
./scripts/build.ps1 -Smoke
```

The bootstrap script downloads pinned SPS dependencies and CMake 3.31.6 into the checkout. It does not build the upstream Qt application or upstream test suites. The plugin is produced in `artifacts/SequentialGallery/`. Keep `SequentialGallery.gha` and `SequentialGallery.Native.dll` in the same directory.

For a different Rhino installation, pass `-RhinoInstallDir 'D:/Rhino 8'` to the build script.

## Run

```powershell
./scripts/start-rhino.ps1
```

The launcher sets `RHINO_PACKAGE_DIRS` for the new process so Rhino can discover the build output. For a permanent installation, copy the plugin and native DLL into the same directory under Grasshopper's Libraries folder, retain the license notices, and restart Rhino.

1. Open a Grasshopper definition and identify at least two Number Sliders. Integer sliders can be used without changing their settings.
2. Add **Params > Util > Sequential Gallery**. Select the target sliders and use **Connect Selected Sliders** in the component's context menu, or wire them directly to **Variables**.
3. Hide unwanted previews and set the Rhino viewport's camera and display mode.
4. Double-click the component to open the setup UI. Choose **Rows** and **Columns** independently from 3, 5, 7, or 9 (default: 5x5). Frame the active Rhino viewport, then click **Start exploration**. Opening the UI does not change slider values or start SPS.
5. Click a preferred candidate. The first three selections zoom in; the fourth updates the preference model and starts a new search plane.
6. Use **Finish with selected design** to reapply the last selected candidate and close the gallery.

The viewport, camera, display mode, capture resolution, and slider settings are recorded when you press Start. Grid dimensions stay fixed for that session; close and reopen the gallery to change them. Odd dimensions retain a center candidate, and larger grids take longer to generate. The zoom factor stays at 2x, with four selections per plane.

Start switches the setup screen to the gallery screen. Grid controls, timing estimates, and setup instructions disappear; candidate images, generation progress, and finish controls take their place.

Setup reads the last Grasshopper document solution duration when the window opens and estimates computation time as that duration multiplied by the selected image count. Changing rows or columns updates the estimate without running a solution. The estimate excludes viewport redraw, image capture, and SPS; actual solve costs may change, and out-of-range candidates are skipped. If no positive completed timing is available, the UI shows that the estimate is unavailable. A partial or cached solution may underestimate a full candidate recomputation.

The first plane is centered at the midpoint of each slider's range. Candidates outside any slider's normalized range are not captured; their slots show **Out of range** and cannot be selected. Boundary regions may therefore contain fewer images than candidate slots.

**Cancel generation**, closing the window, or an error stop keeps the current slider values. Cancellation takes effect after the current Grasshopper computation or native SPS call returns. The finish button remains disabled until a candidate has been selected.

SPS uses continuous coordinates internally. Values are rounded only when applied to the sliders, respecting each slider's existing numeric type and precision. Rounding may cause multiple slots to show the same shape.

Keep the original Rhino and Grasshopper documents active during generation, and do not edit the slider ranges, connections, or definition. The camera, display mode, and capture resolution use the settings recorded at startup. Relay-based variable connections and session persistence are not supported. Existing component errors do not stop exploration when the document solution completes; the gallery captures the geometry visible in the viewport.

## AI rendering

Add **Params > Util > AI Rendering** as a separate component. It has no slider inputs and does not change geometry. Set `OPENAI_API_KEY` in the Windows user environment before launching Rhino. `scripts/start-rhino.ps1` refreshes the registered user key for its new Rhino process without printing it. If launching from Zed or another application that was already open, restart that application after changing the environment variable. Keys are never saved in Grasshopper definitions or repository files.

1. Double-click **AI Rendering** to open setup. No image request or capture runs yet.
2. Select at least two of the seven atmosphere variables, including Season and RenderingStyle with watercolor. Unchecked variables stay at 0.5.
3. Choose rows and columns from 3, 5, 7, or 9 (default: 5x5), frame the active Rhino viewport, and press **Start rendering exploration**.
4. The component captures that viewport once and sends one OpenAI image-edit request for the entire matrix, using `gpt-image-2.6-flare`, `1024x1024`, and `low` quality. Every subsequent request uses the original capture.
5. Click a valid cell to highlight it. **Next** commits that preference and generates another matrix. Four committed selections update the SPS plane. A new image clears the highlighted selection.
6. **Regenerate** requests another image of the same conditions without advancing SPS. Use it when the generated layout does not match the equal-cell overlay. Out-of-range cells remain covered and cannot be selected.
7. **Finish** crops the highlighted cell into a final preview. **Save PNG** saves only that original crop at a chosen path; it does not generate a new image or upscale it. A 5x5 matrix gives approximately 205x205 pixels per crop.

Failed requests keep the previous image and offer **Retry**, reusing the pending conditions without another SPS selection. **Cancel generation** stops local waiting and ignores late responses, preserving conditions for Retry. It does not guarantee remote cancellation or a refund of API usage. **Close** ends the session. Requests time out after five minutes; retries and regenerations are always manual. The window displays elapsed request time rather than estimating AI latency from Grasshopper computation.

## Supplied sample

```powershell
./scripts/start-rhino.ps1 -CreateSample
```

`samples/Facade.gh` is an unchanged copy of the supplied `test.gh`. The launcher runs `samples/open_sample.py`, which opens this copy, connects its Number Sliders to the gallery, and saves the prepared definition as `samples/Facade.ghx`. Slider types, precision, Fillet Edge wiring, and its existing computation error are preserved. The original `test.gh` is not modified. The same script can be run with Rhino's `_-RunPythonScript` command.

## Develop in Zed

The Rhino launch command and `RHINO_PACKAGE_DIRS` setup were adapted from Tunny's VS Code configuration into `.zed/debug.json` and `.zed/tasks.json`. Only the Windows / .NET 10 profile is included; Tunny-specific signing, licensing, and Revit tasks are not needed.

Install the C# extension and the [NetCoreDbg extension](https://github.com/qwadrox/zed-netcoredbg) in Zed. Run **Gallery: bootstrap dependencies** through `task: spawn` once. Then use `debugger: start` and select **Gallery: Rhino 8 / .NET 10**. This builds the managed plugin in Debug mode, launches Rhino, and opens Grasshopper. Suggested breakpoint locations are `GalleryComponent.OpenGallery`, `GrasshopperHost.ApplyAsync`, and `GalleryWindow.GenerateAsync`.

Zed uses `build` instead of `preLaunchTask`, and `$ZED_WORKTREE_ROOT` instead of `${workspaceFolder}`. The configuration follows the [Zed debugger documentation](https://zed.dev/docs/debugger), [task documentation](https://zed.dev/docs/tasks), and [NetCoreDbg configuration examples](https://github.com/qwadrox/zed-netcoredbg#configuration).

Build and launch tasks are also available independently. Close Rhino before replacing DLLs it has loaded. The native SPS library is built in Release mode for performance; the Zed debug profile targets the C# code.

## Component appearance

Variables wires use a maroon tint (`#9B1B30`), with stronger color when the gallery or a connected slider is selected. Faint and hidden wire settings remain supported. The component and plugin share a 24x24 transparent icon showing the 5x5 gallery and a selected center candidate.

Editable icon sources are `src/SequentialGallery/Resources/Gallery.svg` and `Rendering.svg`; their generated PNGs are embedded in the plugin. To regenerate both, install Node.js and run:

```powershell
./scripts/bootstrap.ps1 -IconTools
node scripts/render-icon.cjs
```

The renderer stays in the ignored `tools/` directory and is not a plugin runtime dependency. Both the SVG and generated PNG are kept in Git.

## Package

After building and preparing the sample in Rhino, run:

```powershell
./scripts/package.ps1
```

This creates `artifacts/SequentialGallery-PoC.zip` with the plugin, native DLL, prepared sample, installation instructions, and license notices. Downloaded dependencies and generated build artifacts are excluded from Git.

## Validation

`-Smoke` exercises the real C# to C ABI to author SPS path. It checks the default 5x5 grid, rectangular 3x7 and 9x3 grids, the initial center at 0.5, 2x zoom along both axes, plane updates after four selections, repeated preference-learning updates, and boundary extrapolation. It does not substitute a mock optimizer.

The supplied definition was also checked in Rhino 8.36.26296.17001 with .NET 10.0.15, using three integer sliders and one decimal slider. Verification covered plugin loading, double-click startup, progressive generation of 25 images, three zooms, advancing to search plane 2 after the fourth selection, and restoring the selected shape on finish. Generating 25 images took approximately 1.4-1.7 seconds for this definition. The restored values were `55, 55, 55, 0.7`, with slider settings and the existing Fillet Edge error preserved. SHA-256 hashes confirmed that `samples/Facade.gh` matches the supplied original.

The Zed configuration files have been checked as JSON and the Debug build passes. Breakpoint suspension inside Zed with NetCoreDbg has not been verified.

The configurable-grid setup was checked with the production WPF window and real SPS using a temporary fake viewport host: opening the window and changing grid dimensions did not start generation, Start processed 21 slots for a 3x7 grid, and settings were locked afterward. This check does not replace verifying the updated setup flow inside Rhino.

The rendering smoke check uses the production WPF window, SPS engine, and HTTP client against a repository-owned Postman stand-in. Start it in a separate terminal, then run the check:

```powershell
postman simulation run postman/simulations/rendering.sim.yaml --port 4900 --no-history --output ndjson
# In another terminal:
./scripts/test-rendering.ps1
```

Postman CLI 1.75.0 was used; local runs need no Postman account. The fixture follows the official image-edit JSON contract and validates request fields; its control routes inject 429, non-JSON 502, and delayed responses. The check verifies pending-condition reuse, cancellation, rejection of late responses, Regenerate, four-selection learning, fixed reference reuse, crop boundaries, and PNG round-trip. Its synthetic images do not establish AI layout quality. Runtime images and logs stay under ignored build output. Provenance and evidence are in `postman/simulations/rendering.context.yaml`.

For one real image-edit call using an existing reference PNG, run `./scripts/test-rendering.ps1 -LiveReferencePath 'path/to/reference.png'`. This uses the configured API account and consumes API usage. It validates the real response through the production gallery, crop, and PNG code; it does not replace testing viewport capture inside Rhino.

The AI Rendering component was also tested inside Rhino with the supplied sample. One live OpenAI request produced a 1024x1024 5x5 matrix in approximately 9.5 seconds, followed by center selection, Finish, and 205x205 PNG encoding. Slider values, bounds, types, and precision remained unchanged. This verifies the live capture-to-gallery-to-crop path; exact AI adherence to every atmosphere condition is not automatically assessed.

See `spec.md` for requirements and `CONTEXT.md` for domain terms, including AI rendering. SPS is pinned to `cfbf7955ad4b47bb3d3a6854d0b93f643bf20f61`. Dependency notices are listed in `THIRD_PARTY_NOTICES.md` and copied into the build output's `licenses/` directory.
