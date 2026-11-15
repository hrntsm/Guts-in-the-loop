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
6. Use **Finish with selected design** to reapply the last selected candidate, save its viewport image and slider records to the Desktop, and close the gallery after saving succeeds.

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
2. Check at least two of the six atmosphere variables to explore. Uncheck any others and choose their fixed conditions from the dropdowns: five choices per variable, or Spring/Summer/Autumn/Winter for Season. The initial fixed choices are noon, architectural illustration, overcast, moderate activity, moderate planting, and autumn. These choices are frozen on Start and retained through all iterations; Show Prompt identifies them as fixed.
3. Choose rows and columns from 3, 5, 7, or 9 (default: 5x5). Edit **Initial prompt** for desired fixed conditions and **Negative prompt** for results to avoid, frame the active Rhino viewport, and press **Start rendering exploration**.
4. The component captures that viewport once and sends one OpenAI image-edit request for the entire matrix, using `gpt-image-2.6-flare`, `1024x1024`, and `low` quality. Every subsequent request uses the original capture.
5. Click a valid cell to highlight it. **Next** commits that preference and generates another matrix. Four committed selections update the SPS plane. A new image clears the highlighted selection.
6. **Regenerate** requests another image of the same conditions without advancing SPS. Use it when the generated layout does not match the equal-cell overlay. Out-of-range cells remain covered and cannot be selected.
7. **Finish** crops the highlighted cell and automatically saves the result with its input image, exact matrix prompt, and conditions to the Desktop, then closes the window after saving succeeds. It does not generate a new image or upscale the crop. A 5x5 matrix gives approximately 205x205 pixels per crop.

Failed requests keep the previous image and offer **Retry**, reusing the pending conditions without another SPS selection. **Cancel generation** stops local waiting and ignores late responses, preserving conditions for Retry. It does not guarantee remote cancellation or a refund of API usage. **Close** ends the session. Requests time out after five minutes; retries and regenerations are always manual. The window displays elapsed request time rather than estimating AI latency from Grasshopper computation.

**Initial prompt** and **Negative prompt** are separate optional multiline setup fields (up to 2000 characters each). Both are frozen on Start and included unchanged in every Next, Retry, and Regenerate request. Initial prompt describes desired conditions: its default preserves the same building, storey count, massing, roof, openings, camera, and composition. Negative prompt lists unwanted results: its default avoids changed geometry or storeys, inconsistent cameras, unequal or merged/missing cells, watermarks, garbled or extra text, and Rhino viewport grid lines, axes, selection highlighting, and CAD overlays. For a particular design, use an Initial prompt such as `Keep the building single-storey` and a Negative prompt such as `added storeys`. Both are shared scene constraints that take priority over conflicting atmosphere conditions while preserving the matrix format. They are sent as separately labeled sections of the existing API `prompt`, not as a separate API field, and do not guarantee exact image compliance. Either field can be cleared. To change them during exploration, close and start a new rendering session.

Review or edit the English prompt catalog in [`src/SequentialGallery/Prompts/rendering.json`](src/SequentialGallery/Prompts/rendering.json). It contains each variable's short labels and detailed anchor descriptions, the default Initial and Negative prompts, and shared scene and interaction rules. The catalog is embedded at build time: after editing it, rebuild the plugin and restart Rhino. Requests define each anchor once and use codes such as `D1` and weighted blends inside cells; **Selected cell details** expands these into readable descriptions, including season-dependent rain or snow. The six variables are TimeOfDay, RenderingStyle, Weather, Activity, Vegetation, and Season. Weather runs from clear to heavy precipitation; precipitation is rain through autumn and blends through sleet to snow as Season approaches winter. Winter alone does not request snow. Color temperature is not a search variable.

Within each variable, `anchors` and `descriptions` correspond by index and are evenly spaced over 0-1: five entries mean 0, 0.25, 0.5, 0.75, 1; Season's four entries mean 0, 1/3, 2/3, 1. `label` is the compact inspector label; `key` identifies that variable in API anchor references. Preserve variable order and keys when editing prompt wording.

**Show Prompt** opens a resizable second column: the image stays on the left, with separately labeled captured Initial and Negative prompts and a matching value grid on the right. Each box occupies the same row and column as its image cell and lists all six normalized variable values together. Click a valid value box or image cell to synchronize the selection. Hover over a box for exact values and descriptions, or expand **Selected cell details**; the full API prompt is also expandable. Larger grids can be scrolled rather than shrinking the text. **Hide Prompt** restores the full image area. Details always follow the displayed image, including after History restores it; if a different request is pending or failed, its values do not replace the old image's details. Before the first image arrives, the panel identifies its data as a pending request. Toggling the panel does not generate an image or advance SPS.

## Exploration history and result files

Both components provide **Evaluate** and **History** tabs during exploration. Evaluate keeps the existing selection behavior. History shows a left-to-right tree of completed candidate grids, including intermediate zoom stages. Pan or zoom the canvas, select a thumbnail to preview its grid and conditions, then choose **Resume from here** to restore that node's learning state and return to Evaluate. Previewing or resuming does not recompute candidates, change sliders, or make an image request. The active exploration node and preview selection have distinct highlights. Resume is unavailable during generation; cancel first or wait for completion.

Choosing another cell from an earlier grid creates a branch without removing the old path. Choosing the same cell again reuses its stored child and images. AI **Regenerate** creates a separate image variant at the same search plane and step; either variant can lead to further exploration. Failed and cancelled generations do not create history nodes. A successful Retry adds the completed result once. All history and optimizer snapshots are held only in the open window; Close discards them, and reopening starts a new setup. Keep the source Grasshopper definition unchanged during exploration.

Finish saves automatically under `Desktop/Guts-in-the-loop-results/` in a unique result folder, without a destination dialog. Geometry results contain `geometry.png` and `parameters.json`, recording slider names, IDs, actual values, bounds, types, precision, and corresponding SPS coordinates. AI results contain `rendering.png`, the original `reference.png`, the exact source-matrix request in `prompt.txt`, and `metadata.json` with Initial/Negative prompts, fixed/explored variable values, grid/cell coordinates, and generation settings. The saved AI result is a crop, not a separately generated image. A save error leaves the window open for another attempt; only a successful Finish closes it. These files record the final result; they do not include the full history or provide session import/restoration.

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

`-Smoke` exercises the real C# to C ABI to author SPS path. It checks the default 5x5 grid, rectangular 3x7 and 9x3 grids, the initial center at 0.5, 2x zoom along both axes, plane updates after four selections, repeated preference-learning updates, boundary extrapolation, exact state cloning, and isolation of learned observations across branches. It does not substitute a mock optimizer.

The supplied definition was also checked in Rhino 8.36.26296.17001 with .NET 10.0.15, using three integer sliders and one decimal slider. Verification covered plugin loading, double-click startup, progressive generation of 25 images, three zooms, advancing to search plane 2 after the fourth selection, and restoring the selected shape on finish. Generating 25 images took approximately 1.4-1.7 seconds for this definition. The restored values were `55, 55, 55, 0.7`, with slider settings and the existing Fillet Edge error preserved. SHA-256 hashes confirmed that `samples/Facade.gh` matches the supplied original.

The Zed configuration files have been checked as JSON and the Debug build passes. Breakpoint suspension inside Zed with NetCoreDbg has not been verified.

The configurable-grid setup was checked with the production WPF window and real SPS using a temporary fake viewport host: opening the window and changing grid dimensions did not start generation, Start processed 21 slots for a 3x7 grid, and settings were locked afterward. This check does not replace verifying the updated setup flow inside Rhino.

The geometry history smoke check uses the production WPF window and real SPS with a fake geometry host. It checks history preview isolation, existing-child reuse without host calls, alternative branches, learned-plane restoration, cancellation/recovery, save failure/retry, and JSON export of recorded slider values. It writes only under ignored build output:

```powershell
dotnet build smoke/GeometrySmoke/GeometrySmoke.csproj -c Release
dotnet smoke/GeometrySmoke/bin/Release/net10.0-windows/GeometrySmoke.dll
```

The updated geometry component was also checked in real Rhino with the supplied definition and a 3x3 grid. The run completed 53 Grasshopper solutions in approximately 13.4 seconds, confirmed no solves for History preview/resume or existing-child reuse, retained alternate branches, and reached search plane 2. After deliberately changing live slider values, Finish restored the recorded decimal values (`10, 100, 55, 0.7`) and saved matching JSON and PNG. Slider settings, original wiring, existing Fillet Edge errors, and both sample file hashes were preserved. The temporary validation driver redirected exports to ignored build output.

The rendering smoke check uses the production WPF window, SPS engine, and HTTP client against a repository-owned Postman stand-in. Start it in a separate terminal, then run the check:

```powershell
postman simulation run postman/simulations/rendering.sim.yaml --port 4900 --no-history --output ndjson
# In another terminal:
./scripts/test-rendering.ps1
```

Postman CLI 1.75.0 was used; local runs need no Postman account. The fixture follows the official image-edit JSON contract and validates request fields; its control routes inject 429, non-JSON 502, and delayed responses. The check verifies pending-condition reuse, cancellation, rejection of late responses, Regenerate variants, four-selection learning, fixed reference reuse, history branching/restoration without duplicate requests, crop boundaries, and automatic result bundles. Export destinations are overridden to ignored build output for tests. Synthetic images do not establish AI layout quality. Provenance and evidence are in `postman/simulations/rendering.context.yaml`.

For one real image-edit call using an existing reference PNG, run `./scripts/test-rendering.ps1 -LiveReferencePath 'path/to/reference.png'`. This uses the configured API account and consumes API usage. It validates the real response through the production gallery, crop, and PNG code; it does not replace testing viewport capture inside Rhino.

Before the history/export revision, the AI Rendering component was tested inside Rhino with the supplied sample. One live OpenAI request produced a 1024x1024 5x5 matrix in approximately 9.5 seconds, followed by center selection, Finish, and 205x205 PNG encoding. Slider values, bounds, types, and precision remained unchanged. This verifies that earlier live capture-to-gallery-to-crop path; exact AI adherence to every atmosphere condition is not automatically assessed.

See `spec.md` for requirements and `CONTEXT.md` for domain terms, including AI rendering. SPS uses the [state-copy fork](https://github.com/hrntsm/sequential-gallery) pinned to `c1adc392e8a455d9fc3994fe19852a080f79401a`, based on the original upstream `cfbf7955ad4b47bb3d3a6854d0b93f643bf20f61`. The fork adds isolated state copies for history; preference learning remains the author's implementation. Dependency notices are listed in `THIRD_PARTY_NOTICES.md` and copied into the build output's `licenses/` directory.
