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
Console.WriteLine("PASS: C# / C ABI / author SPS, 25 candidates, 2x zoom, two learned plane updates, boundary extrapolation.");
