# Separate final rendering from exploration advancement

Final rendering uses confirmed candidates from their source history grids. `Confirm selection` records a valid geometry or rendering choice without generating another grid or advancing SPS; Next also records its committed choice. This permits accepting the last candidate without requesting an otherwise unused exploration matrix. Multiple recorded choices remain distinguishable by source node and cell.

A final request may combine geometry and rendering conditions from different explorations without rebinding either original history. Retain both sources, the selected parameter records, and the exact directly edited final prompt with the generated-image result. A later draft edit must not be recorded as the instructions for an earlier preview.

Replace AI crop-only Finish with the final-render preview and save workflow. Geometry-only completion remains available. Section 20 supersedes the earlier crop-export Finish requirement; it does not change the fixed-reference semantics of ordinary AI exploration.
