# Contributing

Run commands from the repository root. See [AGENTS.md](AGENTS.md) for repository conventions, [spec.md](docs/spec.md) for behavior, and [CONTEXT.md](docs/CONTEXT.md) for terminology.

Source is under `src/`, checks and HTTP fixtures under `tests/`, design documents and decisions under `docs/`, and sample definitions under `samples/`. Downloaded tools and dependencies live in ignored `.cache/`; rebuildable output goes to `build/` and packages to `artifacts/`.

## Requirements

Use Windows x64, Rhino 8.36 or later, the .NET 10 SDK, Git, and Visual Studio with **Desktop development with C++** and a Windows SDK. Rhino must run on .NET 10. RhinoCommon and Grasshopper are referenced from the Rhino installation; no NuGet SDK packages are required.

## Build

```powershell
./scripts/bootstrap.ps1
./scripts/build.ps1 -Smoke
```

Bootstrap fetches pinned dependencies and CMake into ignored directories. The build produces `artifacts/SequentialGallery/`; keep `SequentialGallery.gha`, `SequentialGallery.Native.dll`, and `licenses/` together. Use `-Configuration Debug` for a Debug build or `-RhinoInstallDir 'D:/Rhino 8'` for another Rhino installation. Close Rhino before replacing loaded binaries.

Downloaded dependencies, build output, and distribution packages stay out of Git. SPS uses the [state-copy fork](https://github.com/hrntsm/sequential-gallery); its pinned revision is maintained in [scripts/bootstrap.ps1](scripts/bootstrap.ps1). Dependency notices are in [THIRD_PARTY_NOTICES.md](docs/THIRD_PARTY_NOTICES.md).

## Run and configure

```powershell
./scripts/start-rhino.ps1
```

The launcher sets `RHINO_PACKAGE_DIRS` for its new process. For a permanent manual installation, copy the plugin, native DLL, and license files into one Grasshopper Libraries folder, then restart Rhino.

For AI rendering, set `OPENAI_API_KEY` in the Windows user environment before launching Rhino. The launcher refreshes that registered key without printing it. Restart an already-running editor before launching Rhino from it after changing environment variables. Never put keys in source files or Grasshopper definitions. AI requests consume API usage.

To prepare the supplied sample:

```powershell
./scripts/start-rhino.ps1 -CreateSample
```

This opens `samples/Facade.gh` through `samples/open_sample.py`, connects its sliders, and saves `samples/Facade.ghx`. Preserve the original sample, slider types and precision, Fillet Edge wiring, and existing component errors. Keep the source documents and slider settings unchanged during exploration.

## Zed

Use `task: spawn` for the **Gallery:** tasks in [.zed/tasks.json](.zed/tasks.json). They include dependency bootstrap, Debug/Release builds, Rhino launch, sample preparation, and Yak packaging.

For debugging, install Zed's C# and [NetCoreDbg](https://github.com/qwadrox/zed-netcoredbg) extensions, then choose **Gallery: Rhino 8 / .NET 10** with `debugger: start`. The profile builds Debug and starts Rhino with Grasshopper. The native library remains a Release build. Breakpoint suspension in Zed has not been verified.

## Validation

`./scripts/build.ps1 -Smoke` runs deterministic boundary-window checks and the real C# / C ABI / SPS checks, including zoom, learned planes, rectangular grids, and isolated history snapshots.

Run the geometry pane checks with a fake viewport host:

```powershell
dotnet build tests/GeometrySmoke/GeometrySmoke.csproj -c Release
dotnet tests/GeometrySmoke/bin/Release/net10.0-windows/GeometrySmoke.dll
```

For image viewing, close-confirmation, and History context-menu checks without HTTP:

```powershell
dotnet build tests/IntegratedSmoke/IntegratedSmoke.csproj -c Release
dotnet tests/IntegratedSmoke/bin/Release/net10.0-windows/IntegratedSmoke.dll --view-only
dotnet tests/IntegratedSmoke/bin/Release/net10.0-windows/IntegratedSmoke.dll --close-only
dotnet tests/IntegratedSmoke/bin/Release/net10.0-windows/IntegratedSmoke.dll --history-only
```

For rendering and full integration checks, start the local Postman stand-in in one terminal:

```powershell
postman simulation run tests/postman/simulations/rendering.sim.yaml --port 4900 --no-history --output ndjson
```

In another terminal, run these sequentially because they share the fixture's failure controls:

```powershell
./scripts/test-rendering.ps1
./scripts/test-integrated.ps1
```

The local stand-in requires no API key or Postman login; it was validated with Postman CLI 1.75.0. Tests write evidence and result bundles under ignored `build/evidence/`; earlier local evidence is retained in `.cache/validation-archive/`. They cover retries, cancellation, history, source provenance, standalone rendering, and export failures. Synthetic outputs do not establish AI fidelity. Offscreen WPF tests do not establish physical mouse capture inside Rhino. See [simulation evidence](tests/postman/simulations/rendering.context.yaml) and [spec.md](docs/spec.md) for validation records.

To make a paid live request with an existing reference PNG, use `./scripts/test-rendering.ps1 -LiveReferencePath 'path/to/reference.png'` with the registered API key.

## Packages

Run **Gallery: build Yak package** in Zed or:

```powershell
./scripts/build-yak.ps1
```

This performs a fresh Release build and uses Rhino's `Yak.exe` to create a Windows package under `artifacts/yak/`. Pass `-RhinoInstallDir` for another installation. Update the version in [packaging/manifest.yml](packaging/manifest.yml) for a release. The package includes the plugin, native DLL, icon, and licenses; no prepared sample is required. This command does not publish or install the package. See the [official Grasshopper package guide](https://developer.rhino3d.com/guides/yak/creating-a-grasshopper-plugin-package/).

## Editable assets

The English prompt catalog is [src/SequentialGallery/Prompts/rendering.json](src/SequentialGallery/Prompts/rendering.json). It is embedded at build time; rebuild and restart Rhino after editing it. Preserve variable order, keys, and the correspondence between anchor labels and descriptions.

Component icons use SVG sources and generated PNGs under `src/SequentialGallery/Resources/`. Regenerate them with Node.js:

```powershell
./scripts/bootstrap.ps1 -IconTools
node scripts/render-icon.cjs
```

Keep both SVG and PNG assets in Git. The renderer is downloaded into ignored `.cache/tools/` and is not a runtime dependency.
