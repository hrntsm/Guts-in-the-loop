# Bind rendering explorations to explicit geometry handoffs

In the integrated window, `Use this shape` supplies a selected geometry history node and candidate cell as a fixed AI reference. Geometry exploration may continue independently, but later geometry selections do not replace that reference. A different handoff starts another rendering exploration while preserving the earlier histories; repeating the same source node and cell revisits its existing exploration.

Automatically updating AI on every geometry selection would tightly couple progress and generation cost. Explicit handoffs allow both sides to remain usable while keeping each rendering image attributable to the exact geometry that informed it. Restoring AI history therefore does not rewind the geometry side.

Finish uses the selected AI image's source geometry, not the geometry side's latest selection. Apply and export that source's recorded variables with the AI result so the final geometry and rendering remain a consistent pair.
