using System;

namespace clicky_windows.Models
{
    public class AnnotationInstruction
    {
        public string Shape { get; set; } = ""; // "ARROW", "CIRCLE", "RECTANGLE", "TEXT", "LINE", "HIGHLIGHT", "BADGE", "SVG", "CLEAR"
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Radius { get; set; }
        public string Text { get; set; } = "";
        public string PathData { get; set; } = ""; // For SVG paths/drawings
        public string Label { get; set; } = "";
        public int DurationMs { get; set; } = 6000; // Default lifetime of drawing (6 seconds)
        public int ScreenIndex { get; set; } = 0; // Target screen index
    }
}
