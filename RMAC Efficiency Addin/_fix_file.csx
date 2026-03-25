// C# Script to remove problematic lines
using System;
using System.IO;
using System.Linq;

var filePath = @"D:\Coding Projects\RMVS02 RMAC Efficiency\RMAC Efficiency Addin\RMAC Efficiency Addin\StandardAddInServer.cs";
var lines = File.ReadAllLines(filePath).ToList();

// Find and remove lines containing _MARKER_4_ and the blank lines before it
// Current state: line 1474 is blank, 1475 is blank, 1476 is blank, 1477 has _MARKER_4_ + unicode junk
// We want to keep line 1473 (closing brace) and remove 1474-1477, keeping just one blank line before EnsureILogicLogControl

int markerIdx = lines.FindIndex(l => l.Contains("_MARKER_4_"));
if (markerIdx >= 0)
{
    // Remove the marker line
    lines.RemoveAt(markerIdx);

    // Remove blank lines above until we hit a non-blank line
    while (markerIdx > 0 && string.IsNullOrWhiteSpace(lines[markerIdx - 1]))
    {
        lines.RemoveAt(markerIdx - 1);
        markerIdx--;
    }

    // Insert one blank line
    lines.Insert(markerIdx, "");
}

File.WriteAllLines(filePath, lines);
Console.WriteLine("Done. Marker line removed.");
