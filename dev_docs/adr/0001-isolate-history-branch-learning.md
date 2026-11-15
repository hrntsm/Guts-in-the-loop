# Isolate learning state when resuming history branches

History must restore the preference-learning state, search plane, and zoom stage of the chosen node. Later selections on another branch must not influence exploration resumed from that point, even though the other branch and its images remain available.

Keeping one latest optimizer model shared across all branches would be simpler, but would make a return to an earlier step depend on choices made afterward. Branch-local restoration preserves the meaning of trying an alternative from the same point. Retain isolated in-memory optimizer copies and clone the chosen copy when resuming it, including the native bridge's within-plane selections.

Source inspection shows mutable preference data is held through a shared pointer, so a default shallow optimizer copy is insufficient. Require isolated in-memory state capture through the native boundary, including within-plane selections, rather than replaying indices into a fresh stochastic optimizer. Any dependency API extension must be maintained in a pinned fork. Disk serialization and restoring a session after its window closes are excluded from the hackathon scope.
