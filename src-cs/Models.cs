using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FH6SkillPointOcr
{
    internal sealed class Screenshot
    {
        public Bitmap Image;
        public int Left;
        public int Top;
        public Screenshot(Bitmap image, int left, int top)
        {
            Image = image;
            Left = left;
            Top = top;
        }
    }

    internal sealed class OcrSnapshot
    {
        public Screenshot Screenshot;
        public List<OcrMatch> Words;
        public List<List<OcrMatch>> WordLines;
        public List<OcrMatch> Lines;
        public string EngineName = "";
        public string RawResponse = "";
        public string ErrorOutput = "";
        public string EngineDiagnostics = "";
        public OcrSnapshot(Screenshot screenshot, List<OcrMatch> words, List<List<OcrMatch>> wordLines, List<OcrMatch> lines)
        {
            Screenshot = screenshot;
            Words = words;
            WordLines = wordLines;
            Lines = lines;
        }
    }

    internal sealed class OcrMatch
    {
        public string Text;
        public RectangleF Rect;
        public double Confidence;
        public OcrMatch(string text, RectangleF rect, double confidence)
        {
            Text = text;
            Rect = rect;
            Confidence = confidence;
        }
        public Point RectCenter()
        {
            return new Point((int)Math.Round(Rect.Left + Rect.Width / 2), (int)Math.Round(Rect.Top + Rect.Height / 2));
        }
    }

    internal struct CellKey : IEquatable<CellKey>
    {
        public int Row;
        public int Col;
        public CellKey(int row, int col)
        {
            Row = row;
            Col = col;
        }
        public bool Equals(CellKey other)
        {
            return Row == other.Row && Col == other.Col;
        }
        public override bool Equals(object obj)
        {
            return obj is CellKey && Equals((CellKey)obj);
        }
        public override int GetHashCode()
        {
            return Row * 397 ^ Col;
        }
    }

    internal sealed class CellView
    {
        public int Row;
        public int Col;
        public int GlobalCol;
        public int StateCode;
        public bool Known;
        public RectangleF Rect;
        public string State;
        public bool Chosen;
        public CellView(int row, int col, RectangleF rect, string state, bool chosen)
        {
            Row = row;
            Col = col;
            GlobalCol = col;
            StateCode = -1;
            Known = false;
            Rect = rect;
            State = state;
            Chosen = chosen;
        }
    }

    internal sealed class OcrFieldView
    {
        public RectangleF Rect;
        public string Label;
        public double Confidence;
        public OcrFieldView(RectangleF rect, string label, double confidence)
        {
            Rect = rect;
            Label = label;
            Confidence = confidence;
        }
    }

    internal sealed class OverlayPointView
    {
        public Point Point;
        public string Label;
        public Color Color;
        public float Radius;
        public OverlayPointView(Point point, string label, Color color, float radius)
        {
            Point = point;
            Label = label;
            Color = color;
            Radius = radius;
        }
    }

    internal sealed class OverlayDetails
    {
        public string Mode = "-";
        public string Stage = "-";
        public string Status = "-";
        public string NextAction = "-";
        public int LoopCount;
        public int DebugSteps;
        public string Calibration = "-";
        public string Grid = "-";
        public string VirtualList = "-";
        public string Ocr = "-";
        public string Target = "-";
        public string SkillPoints = "-";
        public double ElapsedSeconds;
        public int Failures;
    }

    internal sealed class StopRequestedException : Exception { }

    internal sealed class CompletedException : Exception
    {
        public CompletedException(string message) : base(message) { }
    }

    internal enum AutomationTask
    {
        SkillPoints,
        DeleteVehicles,
        FullAuto,
        BlueprintCycleTest
    }

    internal enum VirtualListLoadMode
    {
        None,
        FullState
    }
}
