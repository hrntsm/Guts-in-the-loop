# Sequential Gallery for Grasshopper — PoC Implementation Specification

Sections 20 and 21 supersede earlier descriptions of crop-only Finish and unadjusted out-of-range candidates. Use those sections for current final rendering and boundary behavior; earlier validation records describe the revisions tested at that time.

**Version:** 2.1

**Updated:** 2026-11-14

**Scope:** Hackathon proof of concept

**Document organization:** Sections 1-16 specify the geometry exploration workflow. Section 17 contains the AI rendering requirements, consolidated from the supplied rendering specification and the design discussion. Both workflows are implemented; verification scope is documented in their acceptance sections. Geometry grid settings, timing estimates, completion behavior, and exclusions in sections 1-16 do not automatically define the AI rendering workflow.

**Grid configuration:** The gallery defaults to 5x5. Before starting, users choose rows and columns independently from 3, 5, 7, or 9. References below to 5x5 or 25 candidates describe the default; generation, progress, and selection use the configured candidate count. Rectangular grids use the authors' existing plane-coordinate API without changing the SPS dependency. Zoom remains 2x, with four selections per search plane.

**Time estimate:** When setup opens, read the latest completed document solution duration without triggering recomputation. Display that duration and its product with the selected image count; update the product when grid dimensions change. Mark this as a computation-only estimate excluding redraw, capture, and SPS. Actual costs vary, partial or cached solutions can underestimate recomputation, and out-of-range candidates are skipped. If timing is missing, non-positive, aborted, or still in progress, show that the estimate is unavailable.

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

Double-clicking the component opens the WPF setup UI without starting SPS, recomputing Grasshopper, capturing images, or changing slider values. Users choose the row and column counts, frame the active Rhino viewport, and press `Start exploration`. Only then validate the registered sliders and their ranges, record the viewport and camera, and initialize SPS. With no registered variables, use AI-only mode. Display an error for exactly one registered variable or unsupported connected sources, as defined in Section 19. Grid settings remain fixed until the window is closed.

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

Before generating images, fit the entire sampling window into the normalized 0-1 variable domain as defined in Section 21. Translate the complete window while retaining its width and selected point; shrink only when needed for a fresh plane or numerical fallback. Do not change slider bounds or clamp individual coordinates. Ordinary boundary exploration generates every slot. Keep `Out of range` masking only as a defensive response to unexpected invalid data, not as the normal boundary policy.

### 7.5 Preference learning

After four selections, submit the chosen candidate to SPS. SPS updates its preference model and generates the next search plane. Do not implement custom preference scores or candidate rankings.

---

## 8. WPF gallery

### 8.1 Main window

Use separate setup and gallery views in the same window. Show grid controls and timing estimates only in setup. After Start validates the host, replace setup with the gallery, progress, and finish controls; setup controls do not remain above the images.

Display 25 candidate slots in a separate WPF window, with:

- A 5x5 image gallery.
- The current search plane number.
- The current zoom stage.
- Candidate generation progress.
- A button to finish optimization.

### 8.2 User actions

**Image viewing:** Start each new candidate grid and final-render image fitted to its available viewport. Provide a fixed `Image zoom` toolbar with `Fit`, `100%`, `−`, and `+`; enlarged content scrolls horizontally and vertically. Mouse wheel input over the image zooms around the pointer, preserving its anchor where scroll bounds permit. Right-button dragging pans the image with movement-cursor feedback and mouse capture; releasing the button, losing capture, hiding/unloading the view, changing its image, or returning to Fit ends the drag. Right dragging never selects or learns a candidate. Toolbar, scrollbar, prompt-text, and History-graph gestures retain their own behavior. Fit follows window resizing and column splitters, while manual image scale remains fixed. Scale the AI image and its cell overlay together so selection indices remain correct after zooming and scrolling. Use original cached captures in geometry Evaluate and full PNG dimensions for AI/final images; History preview zoom uses its stored preview resolution. Image viewing must not generate images, change variables, advance SPS, alter exports, or change the independent History graph zoom.

**Candidate click:** Select and highlight the candidate without advancing the optimizer or recomputing the grid.

**Next:** Commit the selected candidate and zoom. On the fourth committed selection, submit it to SPS and generate the next search plane. `Use this shape` is an independent handoff, as defined in Section 19.

**Finish:** Apply the currently selected candidate to Grasshopper, automatically save its image and slider records as defined in Section 18, and close after successful saving.

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
5. Save the viewport image and slider JSON under the Desktop results directory, then close only after successful saving (Section 18).

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
- Select candidates with a click and commit zoom through Next.
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
- History features beyond the scope agreed in Section 18.
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
- [x] Candidate clicks select; Next commits and zooms the search region.
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
5. A minimal README and CONTRIBUTING.md with build, run, and validation instructions.

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

---

## 17. AI rendering exploration

Status: rendering behavior and its original standalone implementation are described here. Section 19 defines the current integrated entry point, handoffs, and shared history where they supersede earlier standalone requirements. Section 17.8 preserves earlier verification records and their limits.

### 17.1 Purpose and scope

Explore the visual atmosphere of a fixed Grasshopper-generated architectural facade using an image-input AI generation API. Users choose rendering variables, compare candidates in one AI-generated matrix image, and guide subsequent exploration through preferences. Prioritize comparing atmosphere over exact reproduction of architectural details.

Retain the existing Windows 11, Rhino 8.36+, .NET 10, and WPF environment. Reuse the existing SPS integration where applicable, with independent exploration state. Do not introduce an alternative optimization library.

Confirmed entry point: use the integrated Sequential Gallery component described in Section 19. With no design variables, Start captures the active viewport once for AI-only exploration. In combined mode, `Use this shape` supplies the selected geometry candidate's recorded image. The legacy AI Rendering component ID remains loadable for existing definitions but is hidden from the new-component palette.

Confirmed test scope: manual generation outside the component has already been tried. This PoC must exercise the complete flow from the component, including automatic image-generation requests on Start and Next and displaying the response in its gallery. Exporting a prompt for manual generation and importing the result does not meet this test objective.

Confirmed initial provider: use the OpenAI image-generation API for this PoC. Available API credits are the intended funding source for testing; the component connects to the API rather than controlling the ChatGPT website or borrowing a signed-in chat session. Do not implement provider switching in this scope. Section 17.11 defines the confirmed model, credential, and request-display defaults.

Confirmed initial output preset: request one 1024x1024 matrix image with low quality per generation, prioritizing end-to-end testing speed and consumption. These dimensions apply to the entire matrix, not each cell: a 5x5 grid gives roughly 200 pixels per cell, and larger grids give smaller cells. Keep this preset fixed for the initial PoC rather than adding output-size or quality settings to setup. Increasing output resolution or quality is a later adjustment, not a per-cell generation or final upscaling step.

### 17.2 Rendering variables

Define six continuous rendering variables, each normalized to 0-1: TimeOfDay, RenderingStyle, Weather, Activity, Vegetation, and Season. ColorTemperature is removed rather than retained as a hidden or fixed variable.

Keep the English prompt catalog in `src/SequentialGallery/Prompts/rendering.json`, independently reviewable from the C# implementation. It owns anchor labels, detailed descriptions, default Initial and Negative prompts, scene/layout instructions, and cross-variable rules. Embed that catalog in the plugin and smoke harness at build time; changes require rebuilding and restarting Rhino. Send shared anchor definitions once per request, with compact anchor references and deterministic blend weights in each cell, to support grids up to 9x9 within the existing 32000-character prompt budget. Inspector details expand the same definitions and include the season-dependent precipitation description. The full API prompt remains available unchanged in the inspector.

| Variable | 0.0 | 1.0 |
|---|---|---|
| TimeOfDay | Morning | Night |
| RenderingStyle | Hand-drawn sketch | Photorealistic |
| Weather | Good weather | Bad weather |
| Activity | Quiet | Lively |
| Vegetation | Sparse planting | Abundant planting |
| Season | Spring | Winter |

For intermediate values, construct deterministic descriptions between neighboring anchors without rounding the SPS coordinate to an anchor.

Weather is one continuous good-to-bad weather variable, rather than a sequence of precipitation categories or separate rain and snow variables. Weather determines whether and how strongly bad-weather conditions are requested; Season determines the precipitation type as well as the seasonal appearance of planting. Spring, summer, and autumn use rain. Between the autumn anchor (Season = 2/3) and winter (Season = 1), precipitation transitions continuously from rain through sleet to snow, using the relative position between these anchors without snapping to a categorical boundary. Winter alone does not request snow. This supersedes both the previous clear-to-rainy target and the earlier proposal to keep precipitation type independent of Season.

Confirmed Weather anchors:

| Value | Prompt description |
|---|---|
| 0.0 | Clear sky, direct light and well-defined shadows, with dry ground. |
| 0.25 | Thin cloud cover with slightly softened light and shadows. |
| 0.5 | Overcast sky with thick clouds, diffuse light and weak shadows, without precipitation. |
| 0.75 | Light precipitation: fine rain streaks and wet pavement, or sparse snowflakes and a thin covering of snow, according to Season. |
| 1.0 | Heavy precipitation: clearly visible rain streaks and reflective wet pavement, or dense snowflakes and snow accumulating on the ground, according to Season. |

Interpret lighting consistently with TimeOfDay: clear weather at night must not introduce sunlight. At every weather value, including the maximum, preserve sufficient visibility to compare the building's outline and openings. Show strong weather differences through clouds, precipitation, and ground surfaces without obscuring the architecture with a downpour or blizzard.

Confirmed Activity anchors:

| Value | Prompt description |
|---|---|
| 0.0 | An empty, quiet architectural setting with no people. |
| 0.25 | A few widely spaced people walking calmly; the setting remains quiet. |
| 0.5 | Moderate pedestrian activity, with people walking and a few small groups conversing. |
| 0.75 | Many people walking and conversing in small groups, creating a visibly active setting. |
| 1.0 | A lively, well-attended setting with frequent pedestrian movement and multiple small conversational groups. |

Activity changes both the number of people and their natural actions. Keep the building's outline and openings visible at every value; do not introduce an obscuring crowd, event equipment, or a change of building use to suggest greater activity.

Confirmed Vegetation anchors:

| Value | Prompt description |
|---|---|
| 0.0 | Almost no planting in the surrounding grounds; predominantly open, unplanted surfaces. |
| 0.25 | Sparse, isolated patches of groundcover and low planting, with occasional shrubs or trees. |
| 0.5 | A moderate amount of groundcover, low planting, shrubs, and trees in the surrounding grounds. |
| 0.75 | Richly planted grounds with extensive low planting, shrubs, and more trees. |
| 1.0 | Abundant layered planting and trees surrounding the building, creating a lushly landscaped setting. |

Vegetation may change the quantity of groundcover, flowers, shrubs, and trees. At every value, preserve visibility of the building's outline, windows, and entrances. Do not add green walls or green roofs. Season determines foliage color and leaf retention; high Vegetation in winter means more planting and trees, not automatically more green leaves. Fixed Initial and Negative prompt constraints still take priority.

Confirmed TimeOfDay anchors (expanded from the original three-anchor implementation):

| Value | Prompt description |
|---|---|
| 0.0 | Early morning immediately after dawn. Low morning sun, a pale blue-to-orange sky, and soft light. |
| 0.25 | Mid-morning. Bright, clear light with sunlight arriving at a moderately oblique angle. |
| 0.5 | Around noon. High sun with bright, even illumination. |
| 0.75 | Twilight immediately after sunset, during magic hour. A crimson-to-deep-blue sky; interior lights begin to glow. |
| 1.0 | Night. A dark sky, illuminated interiors and landscape lighting, with warm light spilling through the windows. |

Intermediate values retain weighted transitions between adjacent anchors. A fixed TimeOfDay uses the chosen anchor, defaulting to noon (0.5).

Confirmed RenderingStyle anchors:

| Value | Prompt style |
|---|---|
| 0.0 | Hand-drawn architectural pencil sketch. Monochrome linework only, with visible paper texture. |
| 0.25 | Light watercolor architectural sketch. Hand-drawn linework with pale, translucent watercolor washes. |
| 0.5 | Architectural illustration with shading, color, and a painterly touch. |
| 0.75 | Architectural CGI visualization. Orderly materials and lighting, with a visibly computer-generated finish. |
| 1.0 | Photorealistic architectural photograph, indistinguishable from a real photograph. |

Treat this as the PoC's defined style sequence rather than a universal ranking of artistic styles. For an intermediate value, construct a deterministic instruction blending its two neighboring anchors according to their relative weights. Preserve the original continuous SPS coordinate for search and preference learning; do not snap it to an anchor or introduce a separate watercolor variable. A fixed RenderingStyle uses the chosen anchor, defaulting to architectural illustration (0.5).

Confirmed Season anchors:

| Value | Season and prompt description |
|---|---|
| 0.0 | Spring: bright young leaves, budding vegetation, and restrained blossoms in some planted areas. |
| 1/3 | Summer: deep green mature foliage and full leafy tree canopies. |
| 2/3 | Autumn: yellow, orange, and red deciduous foliage, with fallen leaves scattered on the ground. |
| 1.0 | Winter: bare deciduous branches and dormant grass and shrubs; evergreen trees retain their foliage. |

Interpret intermediate values as transitions between neighboring seasons. Use a linear sequence without wrapping winter back to spring; SPS continues to use the existing bounded 0-1 coordinate. A fixed Season is chosen from Spring (0), Summer (1/3), Autumn (2/3), or Winter (1), defaulting to Autumn instead of the former 0.5 summer/autumn blend. Season describes seasonal appearance, including bare branches on deciduous trees in winter, and determines the precipitation type when Weather requests bad weather (summer rain, winter snow). Winter does not itself request snowfall or snow cover. Vegetation controls planting quantity, while Season controls the seasonal appearance of that planting.

For the same Vegetation value, Season must not change the number or placement of plants or trees. Use clearly legible seasonal cues, including restrained spring blossoms and autumn leaf litter, subject to the fixed Initial and Negative prompts. When Vegetation is near zero, accept weaker visual differences between seasons instead of adding planting solely to illustrate a season.

Before exploration, display a checkbox and name for each variable, with endpoint descriptions available in a tooltip. Checked variables are explored. Each row also has a fixed-value selector populated from the prompt catalog's anchor labels: five choices for TimeOfDay, RenderingStyle, Weather, Activity, and Vegetation, and four for Season. Enable the selector only when that variable is unchecked. The chosen anchor index maps to index / (anchor count - 1); do not force unselected variables to 0.5 or add them to the SPS dimensions. The initial fixed selection is the middle anchor (index floor(anchor count / 2)): noon, architectural illustration, overcast, moderate activity, moderate planting, and autumn. Remember the setup choice while toggling exploration on or off.

Require at least two explored variables. Support every combination of two through six predefined variables, with no additional selection limit. On Start, freeze the selected subset and fixed values for the session; changing either requires a new exploration session. Apply chosen fixed values to every stored candidate vector and the shared API prompt conditions, including season-dependent precipitation, throughout Next, Retry, Regenerate, zoom, and plane changes. Show these values with fixed-variable labels in inspector details. User-defined variables are out of scope.

Confirmed search method: use the existing SPS engine for every supported selection of two through six rendering variables. Do not implement a separate direct-grid search for two variables. Each cell corresponds to one complete parameter vector; a gallery axis can combine variables even when exactly two are selected. Use the same 2x zoom and preference update after four committed selections for every supported variable count.

Confirmed rendering domain: 0-1 is the complete supported semantic range for each rendering variable. The confirmed anchors above cover the intended atmosphere range within that interval. Do not extrapolate prompt meanings beyond those endpoints or clamp invalid coordinates to boundary values.

### 17.3 Reference image and geometry

The setup screen includes separate optional multiline `Initial prompt` and `Negative prompt` fields, each limited to 2000 characters. Initial prompt states desired conditions; its default preserves the same building, storey count, massing, roof, openings, camera, composition, and equal grid layout. Negative prompt lists unwanted results; its default avoids different buildings between cells, altered geometry, added or removed storeys, inconsistent cameras, uneven grids, unequal cells, merged or missing cells, watermarks, garbled or extra text, labels or numbers, and Rhino viewport grid lines, axes, selection highlighting, and CAD overlays. Important constraints may be reinforced positively and negatively. Users can add design-specific conditions such as keeping a building single-storey; the defaults do not assume every building has one storey.

Freeze both texts on Start and include them unchanged in separately labeled blocks of every matrix request's existing `prompt`, including Next, Retry, and Regenerate. Negative prompt is an avoidance instruction within that text, not a separate provider request field. Either field may be empty without restoring its default. Both apply to all valid cells and take priority over conflicting atmosphere conditions; the matrix layout and no-label format requirements remain in force. Neither is an SPS variable or a guarantee of AI compliance. Changing them requires a new session. Show both captured texts separately in the inspector, including while another request is pending or failed and after restoring a history node. Include both texts in the metadata exported on Finish.

In AI-only mode, Start captures the active Rhino viewport once. In combined mode, use the explicitly handed-off candidate's recorded image instead. Opening setup does not capture an image or start generation. Keep each rendering exploration's original reference for every matrix request, even if the live viewport or geometry exploration later changes. File-image input is outside the scope. Rendering preferences do not mutate geometry SPS state. Combined Finish applies the rendering source's recorded sliders as defined in Section 19.

`Use this shape` is the explicit integrated handoff; it does not finish the geometry side or advance its optimizer. Geometry-only Finish applies the currently selected cell, while combined rendering Finish applies that rendering exploration's source geometry. Section 19 defines their shared export and close behavior.

### 17.4 One matrix image per generation

Confirmed grid: default to 5x5 and let users choose rows and columns independently from 3, 5, 7, or 9, matching geometry exploration. Freeze the grid on Start. Replace the original proposal's fixed 3x3 grid everywhere with the configured row and column counts; rectangular grids are supported.

Generate one image containing exactly the configured rows and columns of rendering candidates in one image-generation request, regardless of candidate count. Do not generate cells individually or assemble separately generated images into a matrix. Each of the rows-times-columns cells corresponds to a candidate parameter vector from the current search region. Prompt cell assignments, selection overlays, hit testing, and final cropping must all use the same configured dimensions.

Supply the fixed facade reference image and one prompt containing conditions for every configured cell. Include values for every explored variable, with unselected variables fixed at their chosen anchors. For example, cell (0,0) could specify TimeOfDay 0.2, RenderingStyle 0.8, and Weather 0.4; cell (1,0) could specify 0.4, 0.7, and 0.5 respectively. These are examples only; actual explored values come from candidate generation.

For any cell with non-finite coordinates or a variable outside 0-1, retain its grid position and request a blank placeholder instead of assigning rendering conditions. WPF must cover that cell with an `Out of range` overlay and disable selection regardless of what the AI draws underneath. Only valid cells receive rendering conditions and can be selected or submitted to SPS. Do not compact the remaining cells or change their row/column-to-vector mapping.

Translate parameter values into natural-language conditions rather than relying on numeric values alone. Use the confirmed variable anchors above and weighted descriptions for intermediate values. Use deterministic prompt construction without an additional LLM call or a general-purpose prompt engine.

The prompt must request:

- The facade from the reference image in every cell.
- The same camera position and composition throughout the matrix.
- A matrix with exactly the configured row and column counts and clear cell boundaries.
- The conditions assigned to each cell.
- No text, numbers, or labels within the generated image.
- Comparable rendering atmosphere, accepting approximate architectural detail.

A single image request does not establish how reliably the model will honor cell boundaries, geometry, or conditions. OpenAI and the 1024x1024 low-quality test preset are confirmed. Divide the displayed image uniformly using the configured rows and columns; do not infer cell boundaries or conditions through image recognition. Users assess whether the generated layout matches the overlay. If it does not, they can press Regenerate to request another matrix with the same reference image, candidate vectors, prompt, grid, and output settings, without advancing SPS. Keep the previous successful image available until a replacement succeeds. Regeneration does not guarantee that a model will obey the layout, and it is not automatic.

### 17.5 Exploration and zoom

1. Let the user select rendering variables and grid dimensions and frame the active Rhino viewport in setup.
2. On Start, freeze the variable selection and grid dimensions and capture the active viewport once as the fixed reference image.
3. Initialize an independent rendering search session.
4. Generate rows-times-columns candidate vectors and translate them into one matrix prompt.
5. Submit one image-generation request and display the returned matrix.
6. Map the clicked row and column to the stored candidate vector without image recognition or feature extraction.
7. Highlight the selected cell without updating SPS or requesting an image. Allow the user to change the selection within the displayed matrix.
8. Zoom by 2x around the committed selection and request a new matrix.
9. After four committed selections, update SPS preferences and generate the next search plane.
10. Repeat until the user finishes.

The original proposal describes a plane conceptually as x(u,v) = c + u*a + v*b. This is not a replacement for the authors' existing piecewise plane implementation: evaluate the corrected sampling window through the author's `CalcParameters`. Preserve midpoint-centered initialization at 0.5. Section 21 supersedes the original unadjusted `CalcGridParameters` extrapolation and disabled-boundary-cell policy; preference learning remains the author's implementation.

The original proposal's two-variable direct-grid exception is superseded: two-variable sessions also use the authors' plane evaluation, zoom, preference learning, and plane updates. The rendering session remains independent of the geometry session.

### 17.6 Screens and final result

Use a variable- and grid-selection setup screen followed by a rendering gallery screen. Hide setup settings after Start, matching geometry exploration. The rendering gallery displays:

- The single AI matrix image with the configured row and column counts.
- Exploration iteration and zoom stage.
- The selected variable names.
- Image-generation progress or a busy indication.
- Next and Finish actions.
- A Regenerate action for requesting another image of the same candidate conditions.
- Cell boundaries and a selected-cell outline drawn by WPF.
- A Show Prompt / Hide Prompt toggle for a resizable two-column view: image or final crop on the left, read-only request details on the right.

The prompt inspector shows the session's captured Initial and Negative prompts in separate fields and a value grid with the same row and column counts as the image. Each value-grid box contains its row/column identifier, validity and all six normalized values together, rather than putting variables in separate table columns. Image and valid value-box selections stay synchronized. Use readable compact labels and numbers inside boxes, with exact round-trip numeric strings, full variable names, descriptions and fixed-variable labels in tooltips and expandable selected-cell details. Scroll larger value grids without reducing text to an unreadable size. Keep an expandable copy of the full API prompt. The inspector follows the batch belonging to the displayed image, including after restoring a history node and when another request is pending or failed. If no image has been received yet, show the pending batch with an explicit pending label. Showing, hiding, resizing, or reading the panel must not call the API or commit an SPS selection.

Confirmed selection behavior: clicking a valid cell only selects and highlights it. Users may revise the selection before pressing Next. Next commits the current selection to SPS and requests the next matrix; clicking alone never advances zoom, adds preference data, or calls the image-generation API. Next requires a valid selection and is unavailable during generation. The integrated geometry side uses the same select-then-Next behavior (Section 19).

Confirmed final result: Finish confirms the selected cell and crops it from the matrix for automatic result export. The crop is an application-side pixel-region extraction from the original AI response, not a separate generated image or a screenshot of the WPF overlay. Derive crop bounds from the selected row/column and configured grid using the same uniform partition used for hit testing, with integer pixel edges covering the original image. A 1024x1024 image divided into a 5x5 grid yields cells approximately 205x205 pixels. If the generated visual boundaries do not match that partition, the crop may include neighboring content or a drawn gutter; use the agreed manual Regenerate flow rather than claiming automatic visual boundary detection.

Save the crop at its existing resolution: the small cell image is sufficient for the end-to-end PoC, so Finish must not request a new standalone rendering or upscale the crop. Automatically save the crop with its input reference, exact matrix prompt, and conditions under the Desktop results directory as defined in Section 18, then close only after successful saving. Do not show a destination dialog or add a separate Save PNG step. Full-matrix/history export and session import/restoration remain outside the PoC scope.

Confirmed API failure behavior: preserve the last successfully displayed matrix and its selection when a generation request fails. Show the error and offer a manual Retry for the pending generation. Retain its reference image, candidate vectors, grid mapping, prompt, and output settings so Retry resends the same conditions without advancing zoom, resubmitting preference data, or incrementing the committed selection count. Disable further Next actions while that generation is pending. An initial generation failure has no previous matrix to display but can retry the same initial candidates. Do not retry automatically.

Distinguish Retry from Regenerate: Retry repeats a failed pending request; Regenerate replaces a successfully returned matrix while preserving its candidate conditions and search position. Neither action submits a new preference. While any request is running, prevent overlapping Next, Retry, or Regenerate requests. A regenerated response replaces the image only after success and clears the cell selection as defined in section 17.11.

Confirmed cancellation behavior: Cancel generation stops waiting for the current request, keeps the window and exploration state open, and retains the last successful matrix and selection. Preserve the pending candidate conditions for a manual Retry without another SPS selection. Ignore responses that arrive after cancellation, including when a later Retry has already started. During an initial request, cancellation leaves no prior matrix but still permits Retry. Close is a separate action that ends the session. This is local cancellation behavior and does not promise remote generation termination or a reversal of API usage.

### 17.7 Implementation sequence and deliverables

1. Define the six variables, including Season and watercolor-style coverage, and checkbox setup UI.
2. Generate rows-times-columns parameter vectors using the existing SPS engine for two through six selected variables.
3. Build a prompt and call the selected image-input API once per matrix.
4. Display the matrix, map cell selections, and implement zoom and preference updates.
5. Crop and display the final cell.

Deliver the integrated component, separate geometry and AI evaluation panes, explicit handoffs, per-reference SPS state, and shared connected History view as specified in Section 19.

Out of scope for rendering exploration: individual cell generation, user-defined rendering variables, changing variables mid-session, automatic AI evaluation, image recognition for condition estimation, high-resolution upscaling, full-matrix/history export, a custom renderer, databases, session persistence after closing, switching between multiple AI providers, and developing a new independent optimization algorithm.

### 17.8 Acceptance criteria

The implementation covers the following criteria. Automated WPF checks use the real SPS engine and HTTP client with a repository-owned Postman stand-in; the live Rhino check exercises one actual generation, selection, Finish, and PNG encoding. Visual adherence to every prompt condition remains a manual assessment.

- [x] Select any combination of two through six rendering variables through checkboxes, including Season.
- [x] Include watercolor-style conditions in the RenderingStyle mapping.
- [x] Choose unselected variables' fixed values from their catalog anchors (five choices, or four seasons) and retain them throughout the session.
- [x] Default to a 5x5 rendering grid and support independent row and column choices of 3, 5, 7, or 9, fixed for each session.
- [x] Generate rows-times-columns candidate vectors for the selected variables.
- [x] Use the existing SPS implementation for every selection of two through six variables, including 2x zoom and plane updates after four committed selections.
- [x] Obtain the configured candidate matrix in one image-generation response.
- [x] Use one 1024x1024 low-quality output image for the entire matrix in the initial end-to-end test preset.
- [x] Execute generation and display its response from the component without requiring a manual generation/export/import step in ChatGPT or Gemini.
- [x] Map a click to its row, column, and stored parameter vector.
- [x] Let clicks select and highlight without generating images or changing SPS; commit a valid selection and advance only on Next.
- [x] Keep out-of-range cells in their original slots, request blank placeholders, and mask them with non-selectable WPF overlays without clamping coordinates or inventing extrapolated prompt meanings.
- [x] Zoom around the selected candidate.
- [x] Update SPS planes through committed preferences.
- [x] Continue across repeated generations.
- [x] Automatically save the final selected crop, input reference, exact prompt, and conditions to the Desktop and close on success, without regenerating or upscaling it.
- [x] Preserve the last successful matrix on API failure and manually retry the same pending candidate conditions without committing a selection twice or automatically retrying.
- [x] Divide matrices uniformly by the configured grid and allow manual Regenerate with unchanged conditions when the visual layout is unsuitable, without image recognition or another SPS selection.
- [x] Cancel generation without closing the session, retain the last successful image and pending conditions for Retry, and ignore late responses from cancelled requests.
- [x] Keep the facade reference fixed and leave Number Sliders unchanged during rendering exploration.
- [x] Capture the active Rhino viewport once on Start and reuse that image for every matrix request in the session.
- [x] Configure fixed scene constraints through Initial and Negative prompts before Start and reuse both captured texts unchanged for all matrix requests, outside the SPS variables.
- [x] Inspect Initial and Negative prompts, every cell's normalized values, selected-cell details, and the exact API prompt in a two-column view without changing the search or generating another image.
- [x] Open rendering setup and exploration from a separate Grasshopper component, preserving the geometry component's existing finish behavior.

Success means users can choose rendering variables and interactively explore their visual atmosphere by selecting cells in AI-generated matrices.

Historical verification before branching history and automatic export: the production WPF smoke check exercised ten local HTTP calls, including 429, HTML 502, delayed responses, manual Retry, Regenerate, cancellation, four-selection plane learning, crop bounds, and PNG round-trip. Supported grid sizes were checked with seven variables, and a two-variable rectangular session confirmed unselected values stay at 0.5. A live component-driven Rhino run returned a 1024x1024 5x5 matrix in approximately 9.5 seconds, selected its center, finished to a 205x205 crop, and encoded that crop as PNG while preserving slider state. That earlier revision used a native WPF Save dialog; the current automatic export replaces it as specified in Section 18.

The six-variable prompt-catalog revision repeated the ten-call local WPF flow successfully. Checks covered the embedded detailed anchor definitions, removal of ColorTemperature from vectors and inspector values, a distinct twilight anchor, season-dependent rain/snow and their continuous transition, no requested precipitation for clear winter inspector details, and supported grids up to 9x9 with the maximum Initial prompt length. These checks establish caller request construction and display behavior; visual adherence to the expanded descriptions still needs real generated-image evaluation.

The separate-prompts revision repeated the ten-call flow with distinct Initial and Negative prompt text, verified both were frozen and displayed separately through Finish, and confirmed both persisted in actual HTTP prompts through retry, regeneration, and cancellation. Empty optional prompts, negative-only instructions, and grids up to 9x9 with both fields at their 2000-character limits were also checked. Effectiveness of the negative instructions on real generated imagery remains unverified.

The fixed-value-selector revision checked all 29 catalog anchor choices in candidate vectors and request conditions. The ten-call WPF flow explored TimeOfDay and RenderingStyle while fixing Weather to heavy precipitation, Activity to empty, Vegetation to rich planting, and Season to winter. Assertions verified selector enabling and locking, fixed-value snapshots, inspector correspondence, and persistence of fixed conditions across retries, regeneration, cancellation, zoom, and a learned plane update. No paid generation was made for this revision.

### 17.9 Comparison with the current implementation

| Area | Existing geometry implementation | Agreed rendering behavior |
|---|---|---|
| Entry and handoff | Geometry pane in the integrated component; Use this shape passes the selected candidate | AI pane in the same window; no-input AI-only mode captures the viewport on Start |
| Variables | Direct Number Slider references, at least two | Six predefined atmosphere variables including Season, with watercolor-style coverage; selected subset varies, others use chosen fixed anchors |
| Search engine | SPS for every dimension count of two or more | Confirmed: reuse existing SPS for all two-through-six-variable selections; no direct-grid exception |
| Search plane | Authors' plane evaluation through the C ABI | Reuse that evaluation; the proposal's affine equation is only illustrative |
| Grid | Configurable odd rows and columns, default 5x5 | Confirmed: same settings and default; one AI matrix image contains all configured cells |
| Images | Recompute and capture once per valid candidate | One fixed reference image and one AI-generated matrix per generation |
| Selection | Click selects; Next commits and advances | Click selects; Next commits and advances; shape handoff is explicit |
| Boundaries | Translate the complete sampling window to keep candidates inside 0-1 | Use the same bounded window for rendering; retain defensive invalid-cell masks, without per-variable clamping or semantic extrapolation |
| Finish | Restore actual applied slider values, export image and slider JSON, then close on success | Crop and export the selected image, input reference, exact prompt and metadata, then close on success |
| Timing | Last Grasshopper solution duration multiplied by candidate count | Show elapsed request time and an indeterminate busy indicator; do not reuse the geometry estimate |
| Integration | No AI client, provider configuration, or credential handling | OpenAI API with the confirmed defaults in section 17.11, one 1024x1024 low-quality output, manual Retry and Regenerate, and local cancellation with late-response rejection |

### 17.10 References

The paper and public SPS implementation are listed in section 16. OpenAI model and credential references are linked in section 17.11. Rendering requirements are maintained in this document rather than in a separate specification file.

### 17.11 Technical defaults

The following defaults are confirmed for the initial PoC:

- Use `gpt-image-2.6-flare` through the OpenAI Images API image-edit endpoint, with the fixed viewport capture as the image input and the complete matrix prompt. Request a single PNG with size `1024x1024` and quality `low`; do not add a separate conversational-model call. The model is selected for fast iteration as described in the [official image generation guide](https://developers.openai.com/api/docs/guides/image-generation). API model access must still be validated in the actual test environment.
- Read the API key from `OPENAI_API_KEY` in the Rhino process environment, following the [official quickstart](https://developers.openai.com/api/docs/quickstart). Keep it out of Grasshopper parameters, saved definitions, repository files, and logs. Missing credentials should prevent a request and produce an actionable UI message.
- Clear the cell selection whenever a newly generated matrix is successfully displayed, including after Regenerate or Retry. Require the user to inspect and select a cell in that new image before Next or Finish. Failed or cancelled requests preserve the previous matrix and its selection, while unresolved pending-next conditions continue to block another Next.
- Show elapsed request time and an indeterminate busy indicator for AI generation. Do not reuse the Grasshopper solve-time estimate or invent a completion percentage for a remote image request.

## 18. Branching exploration history and final result export

Confirmed request: provide two tabs, `Evaluate` and `History`. Evaluate retains the existing candidate evaluation workflow. History visualizes exploration steps as a tree and allows returning to an earlier step to revise a selection. This supersedes the earlier blanket exclusion of a detailed history UI.

History scope: geometry and AI retain independent exploration states and branches. Section 19 integrates their evaluation panes and connects their histories in one shared window and canvas, superseding the original separate-window layout.

Confirmed branching behavior: returning to an earlier candidate grid and choosing a different candidate creates a new branch. Preserve the original descendant path and its history nodes, allowing navigation back to either path. Do not delete or replace the previous branch when revising a choice.

Confirmed restoration semantics: restore the optimizer's preference-learning state, search plane, and zoom stage belonging to the chosen history point. Selections made later on other branches must not influence exploration resumed from that point. Restoring only a past image or parameter vector while retaining the latest optimizer model does not satisfy this requirement. Retain isolated in-memory optimizer copies and restore by cloning the chosen copy, including within-plane selections.

Confirmed history granularity: create one history node for each displayed candidate grid, including the initial grid and intermediate zoom stages. Do not collapse the four selection stages into a single search-plane node. Users can return to any recorded intermediate stage.

Confirmed history navigation: clicking a node previews its stored candidate grid and information without changing the active exploration state, changing Grasshopper sliders, or generating images. An explicit `Resume from here` action restores that node's exploration state and switches to Evaluate. Display the stored grid on resume without recapturing geometry candidates or requesting a new AI matrix. Distinguish the previewed node from the active exploration node.

Confirmed existing-path reuse: when resuming a history node and committing the same candidate cell that already led to a stored child node, navigate to that existing child and restore its state and images. Do not create a duplicate branch, resubmit the same preference to the restored optimizer, recompute Grasshopper images, or make another AI generation request merely to revisit that path. Explicit AI Regenerate remains a separate operation.

Confirmed regeneration history: preserve both the original AI matrix and each successful regenerated matrix. Add the regenerated image as a separate node connected to its source by an edge labeled `Regenerate`. Both nodes represent the same candidate conditions, preference-learning state, search plane, and zoom stage; regeneration must not count as a preference selection or zoom advance. Either variant can become the source of continued exploration. Existing-path reuse is scoped to the particular source node and its chosen cell, preserving which image variant informed the user's choice.

Confirmed failure history: create history nodes only for completed candidate grids. Failed or cancelled generation attempts remain status information on the current operation, not restorable nodes. A successful Retry adds the completed result once, using the pending conditions without duplicating a preference selection. Failed or cancelled Regenerate retains the original node and adds a variant only after successful completion. For geometry, a completed grid may contain disabled out-of-range slots, and captured Grasshopper solutions may still contain component errors as allowed by the existing capture policy; those conditions do not alone make the grid a failed generation.

Confirmed retention lifetime (revised for hackathon scope): settings, history, stored images, and optimizer restoration data exist only while the exploration window remains open. Close ends the session and discards its in-memory state; reopening the component starts a new setup. Do not add component-level retained sessions, history serialization in Grasshopper definitions, session files, or recovery after restarting Rhino. This supersedes the earlier choice to restore a session after reopening its component.

Confirmed Finish behavior: Finish saves the final result and its associated conditions, then closes the exploration window and ends the session. Do not retain history for post-Finish resumption. Geometry Finish applies the chosen candidate and records its Grasshopper variable values. AI rendering Finish saves the selected rendering together with the actual generation prompt and the input reference image. This supersedes the earlier post-Finish-resumption choice and extends the earlier crop-only PNG export. Export is a record for inspection and later use; implementing import or session restoration is outside the hackathon scope.

Confirmed geometry parameter record: save a JSON file identifying the chosen candidate's Number Sliders by name and instance ID, with the actual applied value, bounds, numeric type, and precision for each. Preserve the distinction between continuous SPS coordinates and rounded applied slider values. Do not create a copy of the Grasshopper definition as part of Finish. This export does not add automatic parameter import or reconstruction.

Confirmed save outcome handling: close the window through Finish only after the required result files have been saved successfully. A save error keeps the window, history, and final result available for another save attempt. If a save operation can be cancelled, cancellation likewise retains the result. Close remains available to end the session without saving. Do not discard in-memory results merely because an export was cancelled or failed. The final export destination is automatic; do not show a destination-selection dialog.

Confirmed export packaging: automatically save into `Guts-in-the-loop-results` under the current user's Desktop directory. Resolve the Desktop through the operating system rather than hardcoding a user path. Create a unique subfolder for each Finish result, using the mode and timestamp plus a collision-safe suffix when needed. Save ordinary files rather than ZIP archives. Geometry results contain `geometry.png` and `parameters.json`; AI results contain `rendering.png`, `reference.png`, `prompt.txt`, and `metadata.json`. AI metadata includes the captured Initial and Negative prompts, all chosen/explored values and fixed/explored flags, selected cell row and column, grid dimensions, and generation settings. The exact prompt belongs to the source matrix request; identify the saved rendering as a crop of its selected cell. Do not export the full history tree or copy the Grasshopper definition. Mark success and close only when all required files have been saved.

User-initiated closing through the title-bar X, Alt+F4, or a Close button asks for confirmation before discarding the session, including setup before a final image has been generated and an unsaved final preview. Use an owned English Yes/No dialog with No as the default. No leaves history, images, settings, and active generation unchanged. Yes approves closure once, then ends local work and waiting, releases the session after its in-flight work can safely stop, and ignores late AI responses; deferred closure must not prompt again. Successful geometry-only or final-render Finish closes after saving without a discard prompt. Component removal and document closure/unloading bypass the prompt so disposal cannot leave an invalid exploration window open. Do not continue a component-owned background generation or retain results for a later window. Local cancellation does not guarantee remote AI cancellation or a refund. This supersedes the earlier background-operation choice, which depended on retaining a closed session.

Confirmed History presentation: use an automatically arranged node-and-edge canvas. Each node shows a candidate-grid thumbnail plus its search plane and step, with parent-to-child flow from left to right and branches arranged vertically. Label selection edges with the chosen cell position and regeneration edges with `Regenerate`. Support panning and zooming; do not add manual node positioning or connection editing. Selecting a node shows a larger image, its conditions, and `Resume from here` on the right. Visually distinguish the active exploration node from the previewed node.

Confirmed busy navigation: allow browsing and previewing completed History nodes while generation is running, but disable `Resume from here` until generation completes or the user explicitly cancels it. Merely browsing history must not cancel, redirect, or duplicate the active generation. After cancellation, resuming a completed node restores that node and abandons the unresolved pending operation; late responses must not attach to the resumed branch.

Existing session constraints remain in force: do not change selected variables, fixed values, prompts, or grid dimensions within a session. Preserve the existing checks for original documents, viewport availability, and registered slider bounds, numeric type, and precision; history does not provide a snapshot of the entire editable Grasshopper definition. Definition editing during exploration and restoring an earlier definition are outside this scope. Component/document disposal must end owned work and release history resources even if the user did not explicitly press Close.

Implementation requirement: use the native state-cloning boundary to retain isolated optimizer state together with within-plane selections and managed plane/step metadata. The pinned SPS fork deep-copies mutable preference data, the learned model, and the search plane. Do not approximate restoration by replaying indices into a fresh stochastic optimizer, leave untracked dependency changes, or replace the author's preference-learning algorithm.

The owner confirmed this design before implementation. This section governs branching history and final export where it supersedes earlier crop-only export or one-way exploration descriptions. It does not introduce restoration after closing a window.

Validation: native smoke checks and independent review verified exact state copies and isolated preference observations after multiple learned planes. Geometry WPF smoke checks with real SPS and a fake host verified branching, reuse, cancellation recovery, exact recorded-value export, and save failure/retry. Rendering WPF checks and independent review passed twelve local HTTP calls covering history reuse without requests, alternate branches, Regenerate variants, learned-state restoration, late-response rejection, and complete result bundles. No paid AI request was made for this revision.

A real Rhino geometry run used the supplied definition with a 3x3 grid, completed 53 solutions in approximately 13.4 seconds, and confirmed preview/resume and existing-child reuse added no solves. It retained alternate branches and advanced to plane 2, then restored exact recorded slider values after deliberate live-value changes and saved matching JSON/PNG. Slider settings, original wiring, existing Fillet Edge errors, and both sample hashes were preserved. The temporary driver wrote only to ignored build output, including an export-root override; real Desktop redirection and physical mouse panning/zooming were not exercised by this run.

## 19. Integrated geometry and AI rendering

Confirmed request: combine geometry exploration and AI rendering into one Grasshopper component and one window. Display geometry optimization on the left and AI rendering optimization on the right, allowing both workflows to be used in the same window. With no design-variable inputs, provide AI rendering only. This replaces the earlier target of keeping the two features exclusively in separate components.

When both features are available, show their histories in two separate columns, geometry on the left and AI rendering on the right. AI rendering must use the chosen geometry as its reference, and the history must visibly identify the geometry-to-rendering handoff.

Confirmed handoff timing: an explicit `Use this shape` action passes a chosen geometry candidate to the AI side. Keep that rendering reference fixed until the user performs another handoff. Geometry exploration may continue on the left while rendering exploration uses the handed-off shape on the right. Do not update the rendering reference or trigger an AI generation merely because the user makes another geometry selection. Simultaneous exploration means both workflows remain available in the same window, with explicit reference changes rather than automatic coupling on every selection.

Confirmed reference history: handing off a different geometry candidate starts a new AI rendering exploration while retaining all previous rendering histories for the open integrated session. Connect each rendering exploration's starting point to the exact geometry history node and candidate cell used as its reference. History navigation must allow returning to either shape's rendering exploration with its original reference; do not replace older histories or reinterpret their generated images as results for the newly handed-off shape.

Confirmed integrated geometry selection: align the geometry side with rendering selection behavior. Clicking a valid geometry cell only selects and highlights it; `Next` commits that preference and advances geometry SPS. `Use this shape` passes the selected candidate to AI rendering without itself committing a geometry preference, zooming, or generating the next geometry grid. This supersedes immediate-click advancement for the integrated geometry UI. The handoff source is the selected cell in the displayed geometry history node, not an implicitly remembered candidate from the preceding grid.

Confirmed handoff reuse: performing `Use this shape` again for the same geometry history node and candidate cell returns to that candidate's existing AI rendering exploration. Do not create another rendering exploration, reset its learning, or make a new image request merely to revisit the same handoff. Explicit `Regenerate` remains available for another image with the same conditions. Handoff identity is the source history node and cell, rather than visual similarity or equality after slider rounding.

Confirmed handoff execution: for a new geometry source, `Use this shape` starts the first AI matrix generation using the prepared rendering variables, fixed values, prompts, and grid settings on the right. Do not require a second Start action after a valid new handoff. Returning through an existing handoff still restores its recorded rendering exploration without generation. Configuration prerequisites must be satisfied before starting a new rendering request.

Confirmed rendering configuration scope: freeze the explored rendering-variable subset, fixed rendering values, Initial prompt, Negative prompt, and rendering grid at the first AI exploration start. Use that same captured configuration for every handed-off geometry candidate during the open integrated session. Do not introduce per-shape configuration editing. This applies at the first handoff that starts AI generation in combined mode and at Start in AI-only mode; starting geometry exploration alone does not yet freeze the rendering setup.

Confirmed AI-history resumption: restoring an AI history node does not rewind or replace the geometry side's current exploration state or selection. Keep the two active exploration positions independent. Always display the current AI exploration's source geometry image on the rendering side, and emphasize its geometry-to-rendering connection in History so the source remains clear even when the left side has moved elsewhere. History preview alone still changes neither active exploration state.

Confirmed combined Finish: when an AI cell is selected for the final result, save it together with the exact geometry candidate that supplied that AI exploration's reference. Apply that source candidate's recorded slider values to Grasshopper, even if geometry exploration on the left has since reached or selected another candidate. Export the source geometry's variable record alongside the AI image, reference, exact prompt, and metadata, preserving the geometry-to-rendering provenance. Close the integrated window only after successful saving, retaining the existing retryable save-error behavior. Do not silently combine the current left-side candidate with an AI image generated from a different shape, or require a new AI generation merely to Finish the selected result.

Confirmed handoff while busy: keep geometry exploration usable while an AI image request is running, but disable `Use this shape` until the AI operation completes or the user explicitly cancels it. A handoff must not implicitly cancel or replace the ongoing generation. Continue allowing History browsing during generation, with resumption gated as previously agreed. Merely selecting geometry or browsing either history must not redirect the AI response to a different source.

Confirmed geometry-only completion: retain an explicit `Finish geometry only` action in combined mode. It applies the selected geometry candidate, exports its image and slider JSON, and closes only after successful saving, without making an AI generation request. Distinguish this action from finishing a selected rendering and its source geometry. The existence of an integrated rendering side must not require users to generate an AI image before they can finish geometry exploration.

Confirmed integrated History layout: use one shared automatically arranged canvas with two labeled columns, geometry on the left and AI rendering on the right. Within each column, exploration trees progress from top to bottom with branches arranged within that column. Draw cross-column handoff edges from each source geometry node and selected cell to the corresponding AI exploration's start. Use shared panning and zooming rather than separate graph viewports. This supersedes the single-workflow left-to-right orientation from Section 18 for the integrated History view. Keep preview selection distinct from each side's active node.

Confirmed input mode validation: no connected design variables means AI-only mode, using the existing active-viewport reference workflow. Exactly one connected design variable prevents starting and displays an actionable requirement for at least two Number Sliders. Two or more directly connected Number Sliders enable combined mode. Do not silently ignore a single connected variable or introduce a separate one-dimensional search algorithm.

Each new geometry source has a new rendering exploration and learning state; retaining older histories does not transfer their preference observations to another shape. Revisit an existing source with its retained rendering state. Geometry and rendering retain their separately configured grids and independent SPS state. In AI-only mode use the available window area for AI setup, evaluation, and history rather than reserving an empty geometry column.

Keep the existing window-lifetime and export rules: Close discards in-memory settings/history, successful Finish exports automatically beneath Desktop/Guts-in-the-loop-results and closes, and save errors preserve the session for retry. Combined AI results include the source geometry parameter JSON as well as the existing rendering crop, reference, exact matrix prompt, and AI metadata. Geometry-only completion exports the currently selected geometry cell, without committing another preference. Do not add session persistence, output import, or automatic history export.

Confirmed compatibility: promote the existing Sequential Gallery entry point to the integrated component and preserve its component ID and Variables input. Keep the old AI Rendering component ID loadable for existing definitions but hide it from the new-component palette and route its UI to AI-only behavior. Do not add a third public component or a definition migration framework.

The owner confirmed this design before implementation. This section supersedes earlier separate-component, immediate geometry-click advancement, and single-workflow history-layout requirements where they conflict. The integrated window owns independent geometry and rendering progress; changing the visible tab must not dispose either exploration. Closing the actual window cancels both and releases all retained runs and snapshots after in-flight work stops safely.

Validation: GeometrySmoke verifies selection without advancement, explicit Next, cached handoffs, independent peer-busy gating, branch restoration, and export failure/retry. RenderingSmoke passes twelve AI-only regression calls and seven multi-shape calls, including actual request-reference hashes, shared frozen configuration, retained per-source pending state, source restoration, and combined export. IntegratedSmoke passes four HTTP calls with the complete window, including two-column handoff links, geometry progress during AI waiting, AI-only layout, and source-A Finish while the left side has selected B. Independent review repeated all three suites with no mandatory findings. No paid requests were made.

A real Rhino integrated run completed 20 Grasshopper solutions and two local stand-in requests in approximately 22.8 seconds. It confirmed selection and handoff caused no extra solves, geometry advanced during AI generation, source B retained its own AI history, and restoring AI A preserved the left side's B position. Finish restored source A's exact slider values (55, 55, 55, 0.7) and saved a matching six-file combined bundle. Original wiring, Fillet Edge errors, slider bounds/types/precision, and both sample hashes were preserved. The driver used an export override beneath ignored build output; paid image quality and actual Desktop redirection were not exercised.

## 20. History-selected finishing render

Confirmed request: select a geometry choice and rendering conditions from History, then enable a finishing-render feature that generates one standalone image rather than another candidate matrix. Provide a way to configure that generation and render the selected combination. This is a new generation operation, distinct from the existing Finish action that exports a selected matrix cell without generation.

Confirmed combination scope: the finishing geometry and rendering conditions may be selected independently from different histories or source shapes. Use the selected geometry candidate's recorded image as the finishing input, and translate the selected AI candidate's complete rendering-variable vector into the finishing prompt. Do not require the selected rendering conditions to originate from that geometry, or silently substitute their original source shape. This combination does not alter the source histories or add preference observations.

Confirmed finishing selection constraint: choose geometry and rendering conditions from results whose exploration selection has already been completed. Do not add a finishing-only interaction for selecting arbitrary previously unchosen cells inside historical grids. The eligible history node is the source candidate grid in which the choice was made, not the next grid generated from that choice. For example, grid 1 followed by choosing A and generating grid 2 makes grid 1 the node associated with finishing candidate A. A latest grid with no completed selection is not eligible merely because it has an incoming transition.

Confirmed History context actions: right-click a source node for the two flat actions `Resume from here` and `Use this node for final`. Resume targets that node using the existing per-pane busy guards. Final selection uses the source node's last confirmed candidate, including a repeated confirmation of an earlier candidate, without a submenu or cell selector. Unconfirmed nodes cannot be used for final rendering. Selecting another node replaces only the geometry or rendering pin of the same type; it does not resume exploration, apply variables, learn preferences, or generate images. This supersedes the earlier multiple-choice selector requirement.

Display the selected geometry and rendering node/cell identifiers at the bottom of History and mark the selected nodes. Keep these pins through preview changes, history resumption, and graph redraws. If a pinned node receives another completed confirmation, update its pin to that latest candidate. Enable the `Final` button once both pins are present and no work blocks finishing; in AI-only mode only the rendering pin is required, with the original viewport reference shown for geometry. Final opens the existing settings/preview workflow.

Confirmed finishing settings: allow output image size and quality to be configured. Display the chosen geometry and the selected rendering candidate's complete six-variable vector as read-only provenance, without adding controls to change those values in the finishing screen.

Confirmed final prompt editing: show a prefilled standalone-image prompt derived from the selected conditions and allow direct editing or replacement of its complete text. This is a final prompt editor, not merely an additional-instructions field. Editing that text must not alter SPS history, the selected geometry record, or the stored rendering vector. Send the final edited text and retain it with the selected values in the result metadata so the record distinguishes the original conditions from the actual generation instructions.

Confirmed finishing preview: display the newly generated standalone image for inspection. Allow editing the final prompt and explicitly generating another image before accepting it. Finish saves the accepted displayed result and closes the session only after saving succeeds; do not automatically close as soon as generation completes. Keep each displayed image associated with the exact prompt, reference, selected conditions, and output settings used to generate it rather than substituting later draft edits into its saved record.

Confirmed AI-only availability: enable finishing in AI-only mode after selecting completed rendering conditions from History. Use that rendering exploration's original input image as the fixed geometry reference, without requiring a geometry history node or fabricating slider records. Combined mode retains independent geometry and rendering-condition choices.

Confirmed completion action: add `Confirm selection` to Evaluate so the selected valid candidate can be recorded as a completed finishing choice without generating the next candidate grid. Record the choice against its existing source node and cell; do not add a fabricated grid node or advance SPS learning, selection count, plane, or zoom. Choices committed through Next are also eligible. Reconfirming the same source node and cell refreshes its recency without duplicating the choice. A latest source node becomes eligible after explicit confirmation even if it has no generated child. The node's last confirmed choice is used for final rendering whether or not it has a successor grid.

Confirmed Finish replacement: replace the existing AI-side crop-export Finish action with `Confirm selection`. Complete AI results through the History-selected final-render workflow and its generated-image preview, then Finish to save. Do not retain a second AI Finish action that exports only a matrix crop. Keep `Finish geometry only` available with its existing geometry-image/parameter export behavior.

Final output records retain the selected geometry and rendering-condition source node/cell identifiers independently, the original six-variable vector, the exact edited prompt used by the accepted image, and requested output settings. In combined mode, Finish applies the explicitly chosen final geometry's recorded slider values and exports its parameter record. In AI-only mode it saves the fixed input reference without fabricating geometry variables. Reuse automatic unique Desktop/Guts-in-the-loop-results folders and retryable save errors; do not introduce a destination dialog or session restoration.

Confirmed output defaults: provide square 1024x1024, landscape 1536x1024, and portrait 1024x1536 output presets, with Low/Medium/High quality; initially choose 1024x1024 and High. Produce one PNG using the existing provider/model, with no candidate matrix or separate upscaling. Prefill a single-image prompt from the selected scene constraints and rendering values, excluding the exploration matrix layout, and allow full direct editing. Open this settings/preview workflow from History once the required confirmed choices are selected. These settings are implemented by the standalone final-render view.

## 21. Automatic sampling-window boundary adjustment

Confirmed request: when ordinary optimization approaches variable boundaries, move the entire candidate grid instead of leaving Out of range slots. Geometry and AI use the same native sampling-window implementation. This supersedes earlier instructions to preserve unadjusted boundary extrapolation and disable normal out-of-range candidates.

Keep an explicit continuous rectangle in the author's two-dimensional plane coordinates. Initial sampling remains the canonical square centered on normalized variable value 0.5 when that window is valid. On each of the first three selections, request a half-width window centered on the selected coordinate. Preserve that requested window unchanged when the complete rectangle maps into 0-1 for every variable, including valid extrapolation outside the original square.

If the requested rectangle is invalid, translate the complete child into its previous certified rectangle. Keep the requested width, row/column order, and selected point; the selected candidate may move away from the center. This parent-contained translation is deliberately conservative and does not promise the globally nearest feasible center. Under the current 2x zoom it always exists in exact arithmetic. Shrink only if translation cannot yield a numerically usable window or a newly constructed plane requires it. For a new plane, try translations retaining the plane origin before reducing width. Reject a truly unusable plane with an actionable error rather than silently collapsing all cells to one point.

Certify the whole rectangle using the author's piecewise-affine evaluation at its corners, its edge intersections with both diagonal seams, and the origin when included; also validate actual sampled vectors. Consequently a window can be adjusted even if its discrete samples happen to be valid but its unsampled interior is invalid. Keep finite-value and 0-1 checks as defensive guards. Never clamp individual variables, extend slider ranges, round continuous SPS coordinates, or invent prompt meanings beyond their semantic domain.

Use the corrected mapping for both generated candidates and the selected vector submitted to SPS. Retain the window and within-plane stage in native clones, so branch restoration and existing-path reuse recover exactly the same candidates without solves or API requests. Keep four committed selections per plane and the author's preference-learning comparisons. This implementation adapts repository-owned sampling code and does not modify the pinned SPS dependency.
