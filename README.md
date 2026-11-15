# Sequential Gallery for Grasshopper

An interactive design exploration tool for Grasshopper that uses Sequential Plane Search (SPS), a Bayesian optimization method, to efficiently optimize geometry and AI renderings by exploring two-dimensional slices of a high-dimensional design space guided by user preferences.

Created for the **AEC Tech NYC 2026 hackathon** by **Hiroaki NATSUME**, **Hiroki YAGINUMA**, **Junichi NAGATA**, and **Yuki NAKANO**.

Requires Windows x64, Rhino 8.36+, and .NET 10.

## Install

1. Open the [GitHub Releases page](https://github.com/hrntsm/Guts-in-the-loop/releases).
2. Under the latest release's **Assets**, download the Windows `.yak` file.
3. Launch Rhino 8.36 or later on Windows.
4. Drag the downloaded `.yak` file from File Explorer onto a Rhino viewport and wait for installation to complete.
5. Restart Rhino and open Grasshopper, then follow the usage steps below.

## Optimization

Uses **Sequential Plane Search (SPS)**, a Bayesian optimization method that learns from user preferences through two-dimensional candidate grids. See the [SIGGRAPH 2020 paper](https://doi.org/10.1145/3386569.3392444) and the [authors' GitHub implementation](https://github.com/yuki-koyama/sequential-gallery). This project uses a [fork with isolated history snapshots](https://github.com/hrntsm/sequential-gallery).

## Use

1. Add **Params > Util > Sequential Gallery** in Grasshopper.
2. Connect at least two Number Sliders to **Variables** for geometry exploration, or leave it unconnected for AI rendering only.
3. Double-click the component to start. Use **Next** to explore, **History** to revisit choices, and **Finish** to save.

Set `OPENAI_API_KEY` before launching Rhino to use AI rendering. Results are saved under `Desktop/Guts-in-the-loop-results/`.

See [CONTRIBUTING.md](CONTRIBUTING.md) for building, development, validation, and packaging; [spec.md](docs/spec.md) for detailed behavior; and [CONTEXT.md](docs/CONTEXT.md) for terminology.

Licensed under the [MIT License](LICENSE).
