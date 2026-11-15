# Sequential Gallery for Grasshopper

Explore Grasshopper geometry and AI renderings in one component.

Created for the **AEC Tech NYC 2026 hackathon** by **Hiroaki NATSUME**, **Hiroki YAGINUMA**, **Junichi NAGATA**, and **Yuki NAKANO**.

Requires Windows x64, Rhino 8.36+, and .NET 10.

## Optimization

Uses **Sequential Plane Search (SPS)**, a Bayesian optimization method that learns from user preferences through two-dimensional candidate grids. See the [SIGGRAPH 2020 paper](https://doi.org/10.1145/3386569.3392444) and the [authors' GitHub implementation](https://github.com/yuki-koyama/sequential-gallery). This project uses a [fork with isolated history snapshots](https://github.com/hrntsm/sequential-gallery).

## Use

1. Add **Params > Util > Sequential Gallery** in Grasshopper.
2. Connect at least two Number Sliders to **Variables** for geometry exploration, or leave it unconnected for AI rendering only.
3. Double-click the component to start. Use **Next** to explore, **History** to revisit choices, and **Finish** to save.

Set `OPENAI_API_KEY` before launching Rhino to use AI rendering. Results are saved under `Desktop/Guts-in-the-loop-results/`.

See [CONTRIBUTING.md](CONTRIBUTING.md) for building, development, validation, and packaging; [spec.md](docs/spec.md) for detailed behavior; and [CONTEXT.md](docs/CONTEXT.md) for terminology.

Licensed under the [MIT License](LICENSE).
