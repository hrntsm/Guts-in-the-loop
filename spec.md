# Sequential Gallery for Grasshopper — PoC Implementation Specification

**Version:** 2.0

**Updated:** 2026-11-14

**Scope:** Hackathon proof of concept

---

## 1. Purpose

Develop an application for interactively optimizing architectural facades generated in Grasshopper. Users select preferred shapes from multiple candidates displayed in a gallery.

Use Sequential Plane Search (SPS) from *Sequential Gallery for Interactive Visual Design Optimization*.

### Principles

- Reuse the paper's optimization algorithm through the authors' public implementation.
- Use standard Grasshopper Number Sliders as design variables.
- Capture the Rhino viewport to generate candidate images.
- Display a 5x5 candidate gallery in WPF.
- Follow the paper's search procedure using user selections.
- Implement only the features needed to demonstrate the hackathon PoC.

### Completion condition

**Display Grasshopper-generated candidates in a 25-slot gallery, advance the SPS search through user selections, and reproduce the final selected shape in Rhino.** Duplicate shapes caused by slider rounding and out-of-range slots are allowed.

---

## 2. Development environment

| Item | Technology |
|---|---|
| OS | Windows 11 |
| CAD | Rhino 8.36 or later |
| .NET | .NET 10 |
| Grasshopper plugin | C# / Grasshopper SDK |
| UI | WPF / XAML |
| Optimization | Authors' SPS / C++ |
| Native integration | C ABI / P/Invoke |
| Image generation | RhinoCommon / Viewport Capture |

This is a Windows-only PoC; cross-platform support is out of scope. Verify Rhino 8's .NET 10 runtime, WPF integration, and the Windows SPS build early in implementation.

---

## 3. Architecture

The application consists of four parts.

### 3.1 Sequential Gallery component

The only custom component placed in Grasshopper. It registers Number Sliders, starts optimization, updates slider values, recomputes Grasshopper, and captures the Rhino viewport.

### 3.2 SPS engine

Use the authors' C++ implementation to generate search planes, manage user preferences, and calculate the next plane. As a rule, leave the internal SPS algorithm unchanged.

### 3.3 WPF gallery

Display a separate window for the 5x5 candidate images, candidate selection, four selection stages, advancing to the next search plane, and finishing optimization.

### 3.4 Rhino viewport

Display the Grasshopper-generated facade and capture each candidate using the same viewport. Do not develop a custom rendering engine.

---

## 4. Grasshopper component

### 4.1 Component name

`Sequential Gallery`

Add only one custom Grasshopper component.

### 4.2 Input

| Name | Description |
|---|---|
| Variables | References to the Number Sliders used as optimization variables |

Obtain the source `GH_NumberSlider` objects rather than their numeric outputs. Refer to the variable connection approach used by Tunny and Galapagos.

### 4.3 Registering variables

1. Create Number Sliders in Grasshopper.
2. Select the sliders to optimize.
3. Choose `Connect Selected Sliders` from the `Sequential Gallery` context menu.
4. Register the selected sliders at the Variables input.

Also support ordinary Grasshopper wiring.

### 4.4 Slider information

| Information | Source |
|---|---|
| Identifier | `InstanceGuid` |
| Name | `NickName` |
| Minimum | `Slider.Minimum` |
| Maximum | `Slider.Maximum` |
| Current value | `Slider.Value` |
| Numeric type | `Slider.Type` |

Preserve each standard Number Slider's numeric type and precision. SPS treats all variables, including integers, as continuous values normalized to 0-1. Round only when applying values to sliders, according to their existing type and precision. Rounding can produce duplicate candidate shapes.

### 4.5 Updating sliders

Use the Grasshopper SDK's `SetSliderValue`, referring to Tunny's `VariableManager.cs`.

Conceptually:

```csharp
slider.SetSliderValue(value);
slider.ExpireSolution(false);
```

Pass values compatible with the slider's precision settings. Following Tunny's approach, temporarily disable `Slider.RaiseEvents` while updating values and restore its previous setting afterward.

Perform preference learning and zooming with the original, unrounded SPS coordinates. Use the values actually applied to sliders to reproduce the selected shape on finish. Convert normalized SPS coordinates to actual values using each slider's bounds.

### 4.6 Opening the gallery

Double-clicking the component opens the WPF gallery. Validate the registered sliders and their ranges during initialization. Display an error and do not start if no variables are registered or unsupported sliders are included.

---

## 5. Grasshopper recomputation

### 5.1 Basic sequence

Process each candidate in order:

1. Obtain candidate parameters from SPS.
2. Set Number Slider values.
3. Expire the changed sliders.
4. Recompute Grasshopper.
5. Confirm completion through `SolutionEnd`.
6. Update the Rhino viewport.
7. Capture the viewport image.
8. Pass the image to the WPF gallery.

Process the 25 candidates sequentially. Access Rhino and Grasshopper objects on the required UI thread.

### 5.2 Detecting completion

Use `GH_Document.SolutionEnd`, referring to Tunny's `SolutionManager.cs`. Register the handler before calling `NewSolution` and remove it afterward. Avoid duplicate or recursive recomputation.

### 5.3 Precautions

- Confirm that slider updates change the geometry correctly.
- Redraw the viewport after Grasshopper finishes recomputing.
- Capture images after the display update completes.
- Do not start another candidate while one is being generated.
- Keep the WPF window responsive during computation.

---

## 6. Candidate images

### 6.1 Capture method

Capture images directly from the Rhino viewport, referring to Tunny's `Fish Print by Capture`.

Conceptually:

```csharp
RhinoView view = RhinoDoc.ActiveDoc.Views.ActiveView;
Bitmap bitmap = view.CaptureToBitmap();
```

Use the actual APIs and image types available in Rhino 8 / .NET 10.

### 6.2 Capture target

Choose the target viewport when optimization starts. Keep using that viewport so switching active windows cannot change the capture target.

### 6.3 Display conditions

Keep the camera position, camera direction, projection, display mode, viewport size, and background identical across candidates. Do not automatically adjust the camera during generation.

The display uses Grasshopper previews, so users must hide unwanted objects beforehand.

### 6.4 Image transfer

Convert captured images to data WPF can display. Keep images in memory for the PoC; saving files is optional. Display each candidate as soon as its image is ready.

---

## 7. Optimization algorithm

### 7.1 Algorithm

Use Sequential Plane Search (SPS) from the authors' public implementation:

https://github.com/yuki-koyama/sequential-gallery

### 7.2 Design variables

Normalize each Number Slider variable to 0-1:

\[
z_i=\frac{x_i-x_i^{\min}}{x_i^{\max}-x_i^{\min}}
\]

Convert SPS coordinates back to actual values before applying them to sliders:

\[
x_i=x_i^{\min}+z_i(x_i^{\max}-x_i^{\min})
\]

Use the authors' default initialization: the initial search plane is centered at normalized value 0.5 for every variable, corresponding to each slider's midpoint. Do not center the initial search on the starting slider values.

### 7.3 Search plane

Place 25 candidates on the two-dimensional search plane generated by SPS. Each candidate has design variables corresponding to its plane coordinate. Match the gallery ordering to the search plane coordinates.

### 7.4 Zooming

Follow the original paper's settings:

| Item | Setting |
|---|---|
| Gallery | 5x5 |
| Candidates per screen | 25 |
| Selections | 4 |
| Zoom factor | 2x |
| Plane update | After the fourth selection |

When the user selects a candidate, shrink the search region around that position and display new candidates. Follow the paper's procedure for moving the region, extrapolating after edge selections, and handling variable boundaries.

For Grasshopper, if extrapolation puts any variable outside the normalized range 0-1, skip image generation for that candidate, label its slot `Out of range`, and disable selection. Do not change slider bounds or clamp coordinates to a boundary. Near boundaries, some of the 25 slots therefore show `Out of range` instead of an image. This is an explicit exception to the completion and acceptance criteria requiring 25 distinct images.

### 7.5 Preference learning

After four selections, submit the chosen candidate to SPS. SPS updates its preference model and generates the next search plane. Do not implement custom preference scores or candidate rankings.

---

## 8. WPF gallery

### 8.1 Main window

Display 25 candidate slots in a separate WPF window, with:

- A 5x5 image gallery.
- The current search plane number.
- The current zoom stage.
- Candidate generation progress.
- A button to finish optimization.

### 8.2 User actions

**Candidate click:** Center the next search region on the selected image and zoom in.

**Fourth click:** Submit the selection to SPS and generate the next search plane.

**Finish:** Apply the currently selected candidate to Grasshopper and end optimization.

### 8.3 UI implementation

Use English throughout the user-facing UI, including window titles, component names and descriptions, context menus, buttons, progress, and errors. Label out-of-range candidates `Out of range`. Specifications and development documentation must also be in English.

Use standard WPF controls:

- `ItemsControl` or `ListBox`
- `UniformGrid`
- `Image`
- `ProgressBar`
- `Button`

Do not use advanced animations or external UI libraries. Avoid an unnecessarily complex MVVM structure for this PoC.

---

## 9. C++ integration

### 9.1 Approach

Build the authors' SPS implementation on Windows and call it from C# through P/Invoke. Add a thin C ABI wrapper as needed.

### 9.2 Required operations

- Initialize SPS.
- Set the variable count.
- Retrieve the search plane.
- Register a selection.
- Retrieve the next search plane.
- Dispose SPS.

Adapt API names and arguments to the public implementation. Do not reimplement the C++ algorithm in C#.

### 9.3 Initial verification

1. Build the public SPS implementation on Windows.
2. Call SPS from C#.
3. Retrieve a two-dimensional search plane.
4. Generate the next plane from a selection.

---

## 10. Optimization flow

### Initialization

1. Open Sequential Gallery.
2. Obtain the connected Number Sliders.
3. Read each variable's bounds.
4. Initialize SPS.
5. Generate the initial search plane.

### Candidate generation

1. Generate 25 candidates from the SPS search plane.
2. Apply candidate parameters to sliders.
3. Recompute Grasshopper.
4. Capture the Rhino viewport.
5. Display the image in the WPF gallery.
6. Finish processing all 25 candidates.

### Interactive exploration

1. Select a candidate.
2. Zoom around the selected position.
3. Generate a new set of 25 candidates.
4. Repeat until four selections have been made.
5. Submit preference information to SPS.
6. Generate the next SPS search plane.

### Finish

1. Finish optimization.
2. Apply the final candidate's parameters to sliders.
3. Recompute Grasshopper.
4. Display the final shape in the Rhino viewport.

---

## 11. Tunny implementation references

Use the following existing code as implementation references.

### 11.1 Number Slider management

`Tunny/GrasshopperInOut/VariableManager.cs`

https://github.com/Tunny-gh/development/blob/main/Tunny/GrasshopperInOut/VariableManager.cs

Refer to source-slider retrieval, identification through `InstanceGuid`, reading bounds and current values, and updating values with `SetSliderValue`.

### 11.2 Grasshopper recomputation

`Tunny/GrasshopperInOut/SolutionManager.cs`

https://github.com/Tunny-gh/development/blob/main/Tunny/GrasshopperInOut/SolutionManager.cs

Refer to `ExpireSolution`, `GH_Document.NewSolution`, completion detection through `SolutionEnd`, and recomputation synchronization.

### 11.3 Viewport capture

`Tunny/Component/Print/FishPrintByCapture.cs`

https://github.com/Tunny-gh/development/blob/main/Tunny/Component/Print/FishPrintByCapture.cs

Refer to `RhinoView`, `CaptureToBitmap`, and retrieving viewport images.

Do not add Tunny as a whole dependency. Use its implementation approaches to write the minimum code needed for this PoC.

---

## 12. Implementation order

### Phase 1: Generate the gallery from Grasshopper

**Goal: Display 25 facade candidates without SPS.**

- Create the Sequential Gallery component.
- Register Number Sliders.
- Update slider values.
- Recompute Grasshopper.
- Capture the viewport.
- Display 25 images in WPF.

Temporary candidate parameter values are acceptable at this stage.

**Completion: Inspect 25 Grasshopper model variations in the gallery.**

### Phase 2: Connect SPS

**Goal: Run the paper's optimization method.**

- Build the authors' SPS implementation on Windows.
- Call it from C#.
- Generate 25 candidates from the search plane.
- Zoom through candidate clicks.
- Learn preferences after four selections.
- Generate the next search plane.

**Completion: Continue exploration through user selections.**

### Phase 3: Final verification

**Goal: Prepare a working hackathon demonstration.**

- Prepare a sample facade.
- Run continuous exploration.
- Restore the final shape.
- Measure generation time for 25 candidates.
- Confirm that the demo runs without stopping unexpectedly.

**Completion: Demonstrate the full interaction from start to finish.**

---

## 13. Out of scope

- Dedicated geometry input.
- Dedicated capture component.
- New parameter definition components.
- Custom rendering engine.
- Multiple optimization methods.
- Specialized integer or discrete optimization algorithms (rounding when applying values to standard sliders remains supported).
- Solar or structural performance evaluation.
- Database.
- Session save and resume.
- Image caching system.
- Parallel computation across multiple processes.
- Detailed history UI.
- Web application.
- Cloud integration.
- General plugin extension system.
- Advanced exception recovery.

Implement the minimum error reporting and computation cancellation needed for the demo.

Existing component errors in Grasshopper do not prevent exploration when document recomputation completes. Capture the geometry visible in the viewport. Preserve the supplied sample's Fillet Edge wiring and existing errors.

Cancellation or errors during generation, or exiting without selecting any candidate, leave the current slider values unchanged. Do not automatically restore the initial values or the last selected candidate. Normal finish applies the last selected candidate as described in section 8.2.

---

## 14. Acceptance criteria

- [x] Runs on Rhino 8.36 or later / .NET 10 / Windows.
- [x] Sequential Gallery can be placed in Grasshopper.
- [x] Number Sliders can be registered as optimization variables.
- [x] Slider bounds can be retrieved.
- [x] Slider values can be changed from C#.
- [x] Grasshopper recomputation completion can be detected.
- [x] The Rhino viewport can be captured.
- [x] WPF displays 25 candidate slots (duplicates from rounding and out-of-range slots are allowed).
- [x] Search planes are generated using the public SPS implementation.
- [x] Candidate clicks zoom the search region.
- [x] The next search plane is generated after four selections.
- [x] Optimization continues across multiple search planes.
- [x] The final candidate can be reproduced in Grasshopper.

Verification record: Rhino 8.36.26296.17001 / .NET 10.0.15, using a sample copied from the user-supplied `test.gh`. Confirmed generation of 25 images (approximately 1.4-1.7 seconds), transition to search plane 2 after four selections, and slider values `55, 55, 55, 0.7` after finishing. Integer slider settings and the existing Fillet Edge error were preserved. C ABI smoke checks also confirmed two preference-learning updates and edge extrapolation.

---

## 15. Deliverables

1. Rhino 8 / Grasshopper plugin.
2. C++ integration for SPS.
3. WPF gallery UI.
4. Sample facade Grasshopper definition.
5. README with build and run instructions.

### Definition of success

**Users can compare and select Grasshopper-generated facades in a gallery and explore successive candidates through Sequential Plane Search.**

---

## 16. References

**Sequential Gallery**

Koyama, Y., Sato, I., and Goto, M. (2020). *Sequential Gallery for Interactive Visual Design Optimization*.

https://arxiv.org/html/2005.04107v1

**Sequential Gallery — Public implementation**

https://github.com/yuki-koyama/sequential-gallery

**Tunny — Grasshopper optimization plugin**

https://github.com/Tunny-gh/development

**Rhino Developer Documentation**

https://developer.rhino3d.com/
