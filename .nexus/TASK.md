# Active Task
**Status:** PENDING

## Instructions from Nexus
Copilot, your first task is to scaffold the `CalibrationMatrixCalculator.cs` class. 

**Requirements:**
1. Method to calculate the vertical/horizontal angular delta between the Kinect's optical axis and the center of the display (or specific dot).
2. Apply this offset to the raw 3D coordinates tracked from the Kinect `Face Tracking Basic` eye socket bounds.
3. Return the corrected X/Y vector for the Avatar's eye IK controllers.
4. Keep allocations low (use `struct` or `ref` where applicable) since this runs per-frame at 30fps.

**Output:** Generate the initial skeleton of the class and update `STATUS.md` when completed.