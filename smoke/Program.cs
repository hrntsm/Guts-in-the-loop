using System;
using System.Linq;
using SequentialGallery;

static void Require(bool condition, string message) {
    if (!condition) throw new Exception(message);
}
using var engine = new SpsEngine(3);
var first = engine.Grid();
Require(first.Length == 25 && first.All(x => x.Length == 3), "Expected a 5x5 grid with three variables.");
Require(first[12].All(x => Math.Abs(x - .5) < 1e-10), "Expected initial center at 0.5.");
Require(first.All(x => x.All(v => double.IsFinite(v) && v >= 0 && v <= 1)), "Initial grid must fit variable bounds.");
var neighbor = first[13];
engine.Select(12);
var zoomed = engine.Grid();
for (int j = 0; j < 3; j++)
    Require(Math.Abs((zoomed[13][j] - .5) * 2 - (neighbor[j] - .5)) < 1e-9, "Zoom must halve the spacing.");
for (int click = 1; click < 4; click++) engine.Select(12);
var second = engine.Grid();
Require(second.All(x => x.All(double.IsFinite)), "Next plane must be finite.");
Require(second.Where((x, i) => !x.SequenceEqual(first[i])).Any(), "Fourth selection must construct a new plane.");
// Continue through a second learned plane, checking repeated use of the C ABI.
for (int click = 0; click < 4; click++) engine.Select(12);
Require(engine.Grid().Length == 25, "Repeated plane updates must work.");
using var boundary = new SpsEngine(3);
boundary.Select(0);
Require(boundary.Grid().Any(x => x.Any(v => v < 0 || v > 1)), "Edge zoom must preserve upstream extrapolation.");
using var rectangular = new SpsEngine(3, 3, 7);
var rectangle = rectangular.Grid();
Require(rectangle.Length == 21, "Expected 21 candidates for a 3x7 grid.");
Require(rectangle[10].All(x => Math.Abs(x - .5) < 1e-10), "Rectangular grid center must remain at 0.5.");
rectangular.Select(10);
var rectangleZoomed = rectangular.Grid();
foreach (int index in new[] { 11, 17 })
    for (int j = 0; j < 3; j++)
        Require(Math.Abs((rectangleZoomed[index][j] - .5) * 2 - (rectangle[index][j] - .5)) < 1e-9,
            "Rectangular zoom must halve spacing along both axes.");
for (int click = 1; click < 4; click++) rectangular.Select(10);
var rectangleNext = rectangular.Grid();
Require(rectangleNext.Length == 21 && rectangleNext.All(x => x.All(double.IsFinite)), "Rectangular plane update must work.");
Require(rectangleNext.Where((x, i) => !x.SequenceEqual(rectangle[i])).Any(), "Rectangular fourth selection must update the plane.");
using var tall = new SpsEngine(3, 9, 3);
Require(tall.Grid().Length == 27 && tall.Grid()[13].All(x => Math.Abs(x - .5) < 1e-10), "Tall grid must retain its center.");
tall.Select(0);
Require(tall.Grid().Any(x => x.Any(v => v < 0 || v > 1)), "Rectangular edge zoom must preserve extrapolation.");
using var branchSource = new SpsEngine(3, 3, 7);
for (int click = 0; click < 8; click++) branchSource.Select(10);
Require(branchSource.LearnedPreferenceCount == 2, "Two learned planes must contain two observations.");
branchSource.Select(10);
var savedGrid = branchSource.Grid();
using var savedState = branchSource.Clone();
Require(savedState.Grid().SelectMany(x => x).SequenceEqual(savedGrid.SelectMany(x => x)), "Snapshot must preserve the exact plane and within-plane zoom.");
for (int click = 0; click < 3; click++) branchSource.Select(10);
Require(branchSource.LearnedPreferenceCount == 3 && savedState.LearnedPreferenceCount == 2, "Learning on the live branch must not mutate the saved preference data.");
using var alternate = savedState.Clone();
Require(alternate.Grid().SelectMany(x => x).SequenceEqual(savedGrid.SelectMany(x => x)), "Restoring must not reconstruct a new random plane.");
for (int click = 0; click < 3; click++) alternate.Select(10);
Require(alternate.LearnedPreferenceCount == 3 && branchSource.LearnedPreferenceCount == 3 && savedState.LearnedPreferenceCount == 2,
    "Each branch must learn from its own historical observations.");
Require(savedState.Grid().SelectMany(x => x).SequenceEqual(savedGrid.SelectMany(x => x)), "Advancing both branches must leave the snapshot unchanged.");
for (int click = 0; click < 4; click++) alternate.Select(10);
Require(alternate.LearnedPreferenceCount == 4 && branchSource.LearnedPreferenceCount == 3 && savedState.LearnedPreferenceCount == 2,
    "Repeated learned-plane updates must stay isolated.");
using var detached = alternate.Clone();
alternate.Dispose();
Require(detached.Grid().Length == 21 && detached.LearnedPreferenceCount == 4, "A cloned handle must survive disposal of its source.");
Console.WriteLine("PASS: C# / C ABI / author SPS, rectangular grids, zoom, learned planes, boundary extrapolation, exact state clones and branch learning isolation.");
