# Sequential Gallery for Grasshopper — PoC Implementation Specification

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

Double-clicking the component opens the WPF setup UI without starting SPS, recomputing Grasshopper, capturing images, or changing slider values. Users choose the row and column counts, frame the active Rhino viewport, and press `Start exploration`. Only then validate the registered sliders and their ranges, record the viewport and camera, and initialize SPS. Display an error and do not start if no variables are registered or unsupported sliders are included. Grid settings remain fixed until the window is closed.

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

Use separate setup and gallery views in the same window. Show grid controls and timing estimates only in setup. After Start validates the host, replace setup with the gallery, progress, and finish controls; setup controls do not remain above the images.

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

---

## 17. AI rendering exploration

Status: Implemented as a separate AI Rendering component. Section 17.8 records the focused verification and its limits. Geometry exploration behavior is preserved; section 17.9 compares the two workflows.

### 17.1 Purpose and scope

Explore the visual atmosphere of a fixed Grasshopper-generated architectural facade using an image-input AI generation API. Users choose rendering variables, compare candidates in one AI-generated matrix image, and guide subsequent exploration through preferences. Prioritize comparing atmosphere over exact reproduction of architectural details.

Retain the existing Windows 11, Rhino 8.36+, .NET 10, and WPF environment. Reuse the existing SPS integration where applicable, with independent exploration state. Do not introduce an alternative optimization library.

Confirmed entry point: implement AI rendering exploration as a separate Grasshopper component with its own setup and gallery UI. Keep the existing Sequential Gallery component's finish behavior. Integrating the two workflows is a future goal and is outside this implementation's scope. On Start, capture the active Rhino viewport once as the rendering reference image.

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

Intermediate values retain weighted transitions between adjacent anchors. An unselected TimeOfDay remains at 0.5 (noon).

Confirmed RenderingStyle anchors:

| Value | Prompt style |
|---|---|
| 0.0 | Hand-drawn architectural pencil sketch. Monochrome linework only, with visible paper texture. |
| 0.25 | Light watercolor architectural sketch. Hand-drawn linework with pale, translucent watercolor washes. |
| 0.5 | Architectural illustration with shading, color, and a painterly touch. |
| 0.75 | Architectural CGI visualization. Orderly materials and lighting, with a visibly computer-generated finish. |
| 1.0 | Photorealistic architectural photograph, indistinguishable from a real photograph. |

Treat this as the PoC's defined style sequence rather than a universal ranking of artistic styles. For an intermediate value, construct a deterministic instruction blending its two neighboring anchors according to their relative weights. Preserve the original continuous SPS coordinate for search and preference learning; do not snap it to an anchor or introduce a separate watercolor variable. An unselected RenderingStyle remains at 0.5, meaning architectural illustration.

Confirmed Season anchors:

| Value | Season and prompt description |
|---|---|
| 0.0 | Spring: bright young leaves, budding vegetation, and restrained blossoms in some planted areas. |
| 1/3 | Summer: deep green mature foliage and full leafy tree canopies. |
| 2/3 | Autumn: yellow, orange, and red deciduous foliage, with fallen leaves scattered on the ground. |
| 1.0 | Winter: bare deciduous branches and dormant grass and shrubs; evergreen trees retain their foliage. |

Interpret intermediate values as transitions between neighboring seasons. Use a linear sequence without wrapping winter back to spring; SPS continues to use the existing bounded 0-1 coordinate. An unselected Season stays at 0.5, meaning the transition from summer to autumn. Season describes seasonal appearance, including bare branches on deciduous trees in winter, and determines the precipitation type when Weather requests bad weather (summer rain, winter snow). Winter does not itself request snowfall or snow cover. Vegetation controls planting quantity, while Season controls the seasonal appearance of that planting.

For the same Vegetation value, Season must not change the number or placement of plants or trees. Use clearly legible seasonal cues, including restrained spring blossoms and autumn leaf litter, subject to the fixed Initial and Negative prompts. When Vegetation is near zero, accept weaker visual differences between seasons instead of adding planting solely to illustrate a season.

Before exploration, display a checkbox, name, and endpoint descriptions for each variable. Require at least two selected variables. Support every combination of two through six predefined variables, with no additional selection limit. Unselected variables remain at 0.5. Freeze the selection for the session; changing it requires a new exploration session. User-defined variables are out of scope.

Confirmed search method: use the existing SPS engine for every supported selection of two through six rendering variables. Do not implement a separate direct-grid search for two variables. Each cell corresponds to one complete parameter vector; a gallery axis can combine variables even when exactly two are selected. Use the same 2x zoom and preference update after four committed selections for every supported variable count.

Confirmed rendering domain: 0-1 is the complete supported semantic range for each rendering variable. The confirmed anchors above cover the intended atmosphere range within that interval. Do not extrapolate prompt meanings beyond those endpoints or clamp invalid coordinates to boundary values.

### 17.3 Reference image and geometry

The setup screen includes separate optional multiline `Initial prompt` and `Negative prompt` fields, each limited to 2000 characters. Initial prompt states desired conditions; its default preserves the same building, storey count, massing, roof, openings, camera, composition, and equal grid layout. Negative prompt lists unwanted results; its default avoids different buildings between cells, altered geometry, added or removed storeys, inconsistent cameras, uneven grids, unequal cells, merged or missing cells, watermarks, garbled or extra text, labels or numbers, and Rhino viewport grid lines, axes, selection highlighting, and CAD overlays. Important constraints may be reinforced positively and negatively. Users can add design-specific conditions such as keeping a building single-storey; the defaults do not assume every building has one storey.

Freeze both texts on Start and include them unchanged in separately labeled blocks of every matrix request's existing `prompt`, including Next, Retry, and Regenerate. Negative prompt is an avoidance instruction within that text, not a separate provider request field. Either field may be empty without restoring its default. Both apply to all valid cells and take priority over conflicting atmosphere conditions; the matrix layout and no-label format requirements remain in force. Neither is an SPS variable or a guarantee of AI compliance. Changing them requires a new session. Show both captured texts separately in the inspector, including while another request is pending or failed and after Finish.

On Start, capture the active Rhino viewport once and retain that image throughout rendering exploration. Opening setup does not capture an image or start generation; users can frame the model before Start. Use this same original capture as the reference for every matrix request, even if the live viewport later changes. File-image input is outside the current scope. Do not change Number Sliders during rendering exploration. Share the SPS implementation, not the geometry session's preference or zoom state.

The normal geometry finish continues to reapply the last selected slider values and close the window. Do not add an automatic transition or direct image handoff to the rendering component in this scope. A future integrated workflow may add that connection.

### 17.4 One matrix image per generation

Confirmed grid: default to 5x5 and let users choose rows and columns independently from 3, 5, 7, or 9, matching geometry exploration. Freeze the grid on Start. Replace the original proposal's fixed 3x3 grid everywhere with the configured row and column counts; rectangular grids are supported.

Generate one image containing exactly the configured rows and columns of rendering candidates in one image-generation request, regardless of candidate count. Do not generate cells individually or assemble separately generated images into a matrix. Each of the rows-times-columns cells corresponds to a candidate parameter vector from the current search region. Prompt cell assignments, selection overlays, hit testing, and final cropping must all use the same configured dimensions.

Supply the fixed facade reference image and one prompt containing conditions for every configured cell. Include values for every selected variable, with unselected variables fixed at their defaults. For example, cell (0,0) could specify TimeOfDay 0.2, RenderingStyle 0.8, and Weather 0.4; cell (1,0) could specify 0.4, 0.7, and 0.5 respectively. These are examples only; actual values come from candidate generation.

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

The original proposal describes a plane conceptually as x(u,v) = c + u*a + v*b. This is not a replacement for the authors' existing plane implementation: the current bridge uses `CalcGridParameters`, whose result must remain authoritative. Preserve the existing midpoint-centered initialization at 0.5 and boundary policy: out-of-range cells remain in place and cannot be selected.

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

The prompt inspector shows the session's captured Initial and Negative prompts in separate fields and a value grid with the same row and column counts as the image. Each value-grid box contains its row/column identifier, validity and all six normalized values together, rather than putting variables in separate table columns. Image and valid value-box selections stay synchronized. Use readable compact labels and numbers inside boxes, with exact round-trip numeric strings, full variable names, descriptions and fixed-variable labels in tooltips and expandable selected-cell details. Scroll larger value grids without reducing text to an unreadable size. Keep an expandable copy of the full API prompt. The inspector follows the batch belonging to the displayed image, including when another request is pending or failed. If no image has been received yet, show the pending batch with an explicit pending label. Preserve the displayed batch's details after Finish for inspecting the final crop. Showing, hiding, resizing, or reading the panel must not call the API or commit an SPS selection.

Confirmed selection behavior: clicking a valid cell only selects and highlights it. Users may revise the selection before pressing Next. Next commits the current selection to SPS and requests the next matrix; clicking alone never advances zoom, adds preference data, or calls the image-generation API. Next requires a valid selection and is unavailable during generation. Keep the existing geometry gallery's immediate-click behavior unchanged.

Confirmed final result: Finish confirms the selected cell, crops it from the matrix, and switches to a final preview in the same rendering window. The crop is an application-side pixel-region extraction from the original AI response, not a separate generated image or a screenshot of the WPF overlay. Derive crop bounds from the selected row/column and configured grid using the same uniform partition used for hit testing, with integer pixel edges covering the original image. A 1024x1024 image divided into a 5x5 grid yields cells approximately 205x205 pixels. If the generated visual boundaries do not match that partition, the crop may include neighboring content or a drawn gutter; use the agreed manual Regenerate flow rather than claiming automatic visual boundary detection.

Display and save the crop at its existing resolution: the small cell image is sufficient for the end-to-end PoC, so Finish must not request a new standalone rendering or upscale the crop. Provide a Save PNG action that lets the user choose the destination and save only the final selected crop. Cancelling the save dialog leaves the preview open and does not regenerate the image. Session save/resume and automatic export of the full matrix, reference image, or search history remain outside this PoC's scope.

Confirmed API failure behavior: preserve the last successfully displayed matrix and its selection when a generation request fails. Show the error and offer a manual Retry for the pending generation. Retain its reference image, candidate vectors, grid mapping, prompt, and output settings so Retry resends the same conditions without advancing zoom, resubmitting preference data, or incrementing the committed selection count. Disable further Next actions while that generation is pending. An initial generation failure has no previous matrix to display but can retry the same initial candidates. Do not retry automatically.

Distinguish Retry from Regenerate: Retry repeats a failed pending request; Regenerate replaces a successfully returned matrix while preserving its candidate conditions and search position. Neither action submits a new preference. While any request is running, prevent overlapping Next, Retry, or Regenerate requests. A regenerated response replaces the image only after success and clears the cell selection as defined in section 17.11.

Confirmed cancellation behavior: Cancel generation stops waiting for the current request, keeps the window and exploration state open, and retains the last successful matrix and selection. Preserve the pending candidate conditions for a manual Retry without another SPS selection. Ignore responses that arrive after cancellation, including when a later Retry has already started. During an initial request, cancellation leaves no prior matrix but still permits Retry. Close is a separate action that ends the session. This is local cancellation behavior and does not promise remote generation termination or a reversal of API usage.

### 17.7 Implementation sequence and deliverables

1. Define the six variables, including Season and watercolor-style coverage, and checkbox setup UI.
2. Generate rows-times-columns parameter vectors using the existing SPS engine for two through six selected variables.
3. Build a prompt and call the selected image-input API once per matrix.
4. Display the matrix, map cell selections, and implement zoom and preference updates.
5. Crop and display the final cell.

Deliver a separate AI rendering component, variable setup, candidate generation, SPS integration, AI matrix generation, and its WPF rendering gallery. Direct integration with geometry exploration is deferred.

Out of scope for rendering exploration: individual cell generation, user-defined rendering variables, changing variables mid-session, automatic AI evaluation, image recognition for condition estimation, high-resolution upscaling, automatic matrix/reference/history export, a custom renderer, databases, session persistence, switching between multiple AI providers, and developing a new independent optimization algorithm.

### 17.8 Acceptance criteria

The implementation covers the following criteria. Automated WPF checks use the real SPS engine and HTTP client with a repository-owned Postman stand-in; the live Rhino check exercises one actual generation, selection, Finish, and PNG encoding. Visual adherence to every prompt condition remains a manual assessment.

- [x] Select any combination of two through six rendering variables through checkboxes, including Season.
- [x] Include watercolor-style conditions in the RenderingStyle mapping.
- [x] Keep unselected variables fixed at 0.5.
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
- [x] Crop and preview the final selected rendering and optionally save that crop as a PNG at a user-selected path, without regenerating or upscaling it.
- [x] Preserve the last successful matrix on API failure and manually retry the same pending candidate conditions without committing a selection twice or automatically retrying.
- [x] Divide matrices uniformly by the configured grid and allow manual Regenerate with unchanged conditions when the visual layout is unsuitable, without image recognition or another SPS selection.
- [x] Cancel generation without closing the session, retain the last successful image and pending conditions for Retry, and ignore late responses from cancelled requests.
- [x] Keep the facade reference fixed and leave Number Sliders unchanged during rendering exploration.
- [x] Capture the active Rhino viewport once on Start and reuse that image for every matrix request in the session.
- [x] Configure fixed scene constraints through Initial and Negative prompts before Start and reuse both captured texts unchanged for all matrix requests, outside the SPS variables.
- [x] Inspect Initial and Negative prompts, every cell's normalized values, selected-cell details, and the exact API prompt in a two-column view without changing the search or generating another image.
- [x] Open rendering setup and exploration from a separate Grasshopper component, preserving the geometry component's existing finish behavior.

Success means users can choose rendering variables and interactively explore their visual atmosphere by selecting cells in AI-generated matrices.

Verification record: the production WPF smoke check exercised ten local HTTP calls, including 429, HTML 502, delayed responses, manual Retry, Regenerate, cancellation, four-selection plane learning, crop bounds, and PNG round-trip. Supported grid sizes were checked with seven variables, and a two-variable rectangular session confirmed unselected values stay at 0.5. A live component-driven Rhino run returned a 1024x1024 5x5 matrix in approximately 9.5 seconds, selected its center, finished to a 205x205 crop, and encoded that crop as PNG while preserving slider state. The native Save dialog is provided by WPF; automated verification exercises PNG writing without driving that dialog.

The six-variable prompt-catalog revision repeated the ten-call local WPF flow successfully. Checks covered the embedded detailed anchor definitions, removal of ColorTemperature from vectors and inspector values, a distinct twilight anchor, season-dependent rain/snow and their continuous transition, no requested precipitation for clear winter inspector details, and supported grids up to 9x9 with the maximum Initial prompt length. These checks establish caller request construction and display behavior; visual adherence to the expanded descriptions still needs real generated-image evaluation.

The separate-prompts revision repeated the ten-call flow with distinct Initial and Negative prompt text, verified both were frozen and displayed separately through Finish, and confirmed both persisted in actual HTTP prompts through retry, regeneration, and cancellation. Empty optional prompts, negative-only instructions, and grids up to 9x9 with both fields at their 2000-character limits were also checked. Effectiveness of the negative instructions on real generated imagery remains unverified.

### 17.9 Comparison with the current implementation

| Area | Existing geometry implementation | Agreed rendering behavior |
|---|---|---|
| Entry and handoff | Finish reapplies the selected sliders and closes the window | Confirmed: separate AI rendering component; capture the active viewport once on Start; future workflow integration deferred |
| Variables | Direct Number Slider references, at least two | Six predefined atmosphere variables including Season, with watercolor-style coverage; selected subset varies, others remain at 0.5 |
| Search engine | SPS for every dimension count of two or more | Confirmed: reuse existing SPS for all two-through-six-variable selections; no direct-grid exception |
| Search plane | Authors' plane evaluation through the C ABI | Reuse that evaluation; the proposal's affine equation is only illustrative |
| Grid | Configurable odd rows and columns, default 5x5 | Confirmed: same settings and default; one AI matrix image contains all configured cells |
| Images | Recompute and capture once per valid candidate | One fixed reference image and one AI-generated matrix per generation |
| Selection | A click immediately advances SPS and regenerates candidates | Confirmed: rendering clicks only select; Next commits and advances; geometry behavior stays unchanged |
| Boundaries | Extrapolated candidates outside 0-1 are disabled and not captured | Confirmed: keep the same domain, request blank invalid cells, and mask/disable them in WPF; no clamping or semantic extrapolation |
| Finish | Restore actual applied slider values and close | Confirmed: crop and show a final preview in the rendering window, with optional Save PNG for the selected crop |
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
