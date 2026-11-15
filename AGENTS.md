# Repository conventions

- Use English for repository-owned documentation, comments, configuration labels, UI text, and commit messages.
- This is a hackathon PoC. Prefer small, direct implementations and focused validation over extensive frameworks or test suites.
- Preserve each Number Slider's numeric type and precision. SPS coordinates remain continuous; round values only when applying them to sliders.
- Preserve the supplied sample's Fillet Edge wiring and existing errors. A completed Grasshopper solution may still be captured when individual components report errors.
- Keep downloaded dependencies, build output, and distributable packages out of Git. Fetch pinned dependencies with `scripts/bootstrap.ps1`.
- See `CONTRIBUTING.md` for build and validation commands, `docs/spec.md` for requirements, and `docs/CONTEXT.md` for domain terminology.
