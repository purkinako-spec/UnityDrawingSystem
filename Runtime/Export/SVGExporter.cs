using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DrawingSystem.Export
{
    /// <summary>
    /// Exports stroke data as Scalable Vector Graphics (SVG).
    /// Converts <see cref="StrokeData"/> points to smooth SVG paths using
    /// Catmull-Rom to cubic Bézier conversion, with pressure-varying stroke widths.
    /// </summary>
    public class SVGExporter
    {
        /// <summary>
        /// Exports a list of strokes as an SVG-formatted string.
        /// </summary>
        /// <param name="strokes">The stroke data to export. Must not be null.</param>
        /// <param name="width">The width of the SVG canvas in pixels.</param>
        /// <param name="height">The height of the SVG canvas in pixels.</param>
        /// <param name="backgroundColor">
        /// Optional background color. If provided, a filled rectangle is added behind all strokes.
        /// </param>
        /// <returns>A complete SVG document as a string.</returns>
        public string ExportToSVG(List<StrokeData> strokes, int width, int height, Color? backgroundColor = null)
        {
            if (strokes == null)
                throw new ArgumentNullException(nameof(strokes));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

            StringBuilder sb = new StringBuilder();

            // SVG header
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" width=\"{0}\" height=\"{1}\" viewBox=\"0 0 {0} {1}\">",
                width, height
            );
            sb.AppendLine();

            // Optional background rectangle
            if (backgroundColor.HasValue)
            {
                Color bg = backgroundColor.Value;
                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "  <rect width=\"{0}\" height=\"{1}\" fill=\"{2}\" fill-opacity=\"{3:F3}\" />",
                    width, height, ColorToHex(bg), bg.a
                );
                sb.AppendLine();
            }

            // Strokes
            foreach (StrokeData stroke in strokes)
            {
                if (stroke == null || stroke.Points == null || stroke.Points.Count == 0)
                    continue;

                AppendStroke(sb, stroke, width, height);
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Exports strokes as SVG and saves the result to a file.
        /// </summary>
        /// <param name="strokes">The stroke data to export. Must not be null.</param>
        /// <param name="width">The width of the SVG canvas in pixels.</param>
        /// <param name="height">The height of the SVG canvas in pixels.</param>
        /// <param name="filePath">The absolute file path to write to.</param>
        /// <param name="backgroundColor">
        /// Optional background color for the SVG.
        /// </param>
        public void ExportToFile(List<StrokeData> strokes, int width, int height, string filePath, Color? backgroundColor = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

            string svg = ExportToSVG(strokes, width, height, backgroundColor);

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, svg, Encoding.UTF8);
        }

        /// <summary>
        /// Appends SVG path elements for a single stroke. When the stroke contains pressure
        /// variation, the path is split into per-segment sub-paths with varying stroke-width.
        /// </summary>
        private void AppendStroke(StringBuilder sb, StrokeData stroke, int canvasWidth, int canvasHeight)
        {
            List<StrokePoint> points = stroke.Points;

            if (points.Count == 1)
            {
                // Single point: render as a small circle.
                Vector2 p = UVToPixel(points[0].Position, canvasWidth, canvasHeight);
                float r = stroke.BrushSize * points[0].Pressure * 0.5f;
                r = Mathf.Max(r, 0.5f);

                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "  <circle cx=\"{0:F2}\" cy=\"{1:F2}\" r=\"{2:F2}\" fill=\"{3}\" fill-opacity=\"{4:F3}\" />",
                    p.x, p.y, r, ColorToHex(stroke.Color), stroke.Color.a
                );
                sb.AppendLine();
                return;
            }

            // Convert UV points to pixel coordinates.
            Vector2[] pixelPoints = new Vector2[points.Count];
            float[] pressures = new float[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                pixelPoints[i] = UVToPixel(points[i].Position, canvasWidth, canvasHeight);
                pressures[i] = Mathf.Max(points[i].Pressure, 0.01f);
            }

            // Determine if pressure varies meaningfully along the stroke.
            bool hasPressureVariation = HasPressureVariation(pressures);

            string hexColor = ColorToHex(stroke.Color);
            float opacity = stroke.Color.a;

            if (!hasPressureVariation)
            {
                // Uniform stroke-width: emit a single path.
                float strokeWidth = stroke.BrushSize * pressures[0];
                string pathData = BuildCubicBezierPath(pixelPoints);

                sb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "  <path d=\"{0}\" stroke=\"{1}\" stroke-opacity=\"{2:F3}\" stroke-width=\"{3:F2}\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />",
                    pathData, hexColor, opacity, strokeWidth
                );
                sb.AppendLine();
            }
            else
            {
                // Pressure-varying stroke: emit one path segment per pair of consecutive points
                // using an averaged stroke-width for that segment.
                sb.AppendFormat(CultureInfo.InvariantCulture, "  <g stroke=\"{0}\" stroke-opacity=\"{1:F3}\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\">", hexColor, opacity);
                sb.AppendLine();

                for (int i = 0; i < pixelPoints.Length - 1; i++)
                {
                    float avgPressure = (pressures[i] + pressures[i + 1]) * 0.5f;
                    float strokeWidth = stroke.BrushSize * avgPressure;
                    strokeWidth = Mathf.Max(strokeWidth, 0.1f);

                    string segmentPath = BuildSegmentBezierPath(pixelPoints, i);

                    sb.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "    <path d=\"{0}\" stroke-width=\"{1:F2}\" />",
                        segmentPath, strokeWidth
                    );
                    sb.AppendLine();
                }

                sb.AppendLine("  </g>");
            }
        }

        /// <summary>
        /// Builds a full SVG cubic Bézier path from an array of pixel-space points
        /// using Catmull-Rom to cubic Bézier conversion.
        /// </summary>
        /// <param name="points">The array of points in pixel coordinates.</param>
        /// <returns>An SVG path data string (M and C commands).</returns>
        private string BuildCubicBezierPath(Vector2[] points)
        {
            StringBuilder pathSb = new StringBuilder();
            pathSb.AppendFormat(CultureInfo.InvariantCulture, "M {0:F2},{1:F2}", points[0].x, points[0].y);

            if (points.Length == 2)
            {
                // Only two points: draw a straight line.
                pathSb.AppendFormat(CultureInfo.InvariantCulture, " L {0:F2},{1:F2}", points[1].x, points[1].y);
                return pathSb.ToString();
            }

            for (int i = 0; i < points.Length - 1; i++)
            {
                CatmullRomToBezier(points, i, out Vector2 cp1, out Vector2 cp2, out Vector2 endPt);

                pathSb.AppendFormat(
                    CultureInfo.InvariantCulture,
                    " C {0:F2},{1:F2} {2:F2},{3:F2} {4:F2},{5:F2}",
                    cp1.x, cp1.y, cp2.x, cp2.y, endPt.x, endPt.y
                );
            }

            return pathSb.ToString();
        }

        /// <summary>
        /// Builds an SVG path for a single segment between points[index] and points[index+1],
        /// using Catmull-Rom interpolation for smooth curves.
        /// </summary>
        private string BuildSegmentBezierPath(Vector2[] points, int index)
        {
            StringBuilder pathSb = new StringBuilder();
            pathSb.AppendFormat(CultureInfo.InvariantCulture, "M {0:F2},{1:F2}", points[index].x, points[index].y);

            if (points.Length == 2)
            {
                pathSb.AppendFormat(CultureInfo.InvariantCulture, " L {0:F2},{1:F2}", points[index + 1].x, points[index + 1].y);
                return pathSb.ToString();
            }

            CatmullRomToBezier(points, index, out Vector2 cp1, out Vector2 cp2, out Vector2 endPt);

            pathSb.AppendFormat(
                CultureInfo.InvariantCulture,
                " C {0:F2},{1:F2} {2:F2},{3:F2} {4:F2},{5:F2}",
                cp1.x, cp1.y, cp2.x, cp2.y, endPt.x, endPt.y
            );

            return pathSb.ToString();
        }

        /// <summary>
        /// Converts a Catmull-Rom spline segment into cubic Bézier control points.
        /// Uses the four neighboring points (clamped at boundaries) to compute
        /// tangent-based control points.
        /// </summary>
        /// <param name="points">Full array of points.</param>
        /// <param name="index">Index of the segment start point.</param>
        /// <param name="cp1">First cubic Bézier control point (output).</param>
        /// <param name="cp2">Second cubic Bézier control point (output).</param>
        /// <param name="endPoint">End point of the segment (output).</param>
        private void CatmullRomToBezier(Vector2[] points, int index, out Vector2 cp1, out Vector2 cp2, out Vector2 endPoint)
        {
            // Catmull-Rom uses four points: P0, P1, P2, P3
            // The curve segment runs from P1 to P2.
            Vector2 p0 = points[Mathf.Max(index - 1, 0)];
            Vector2 p1 = points[index];
            Vector2 p2 = points[Mathf.Min(index + 1, points.Length - 1)];
            Vector2 p3 = points[Mathf.Min(index + 2, points.Length - 1)];

            // Catmull-Rom tangent vectors at P1 and P2.
            // T1 = (P2 - P0) / 2,  T2 = (P3 - P1) / 2
            Vector2 t1 = (p2 - p0) * 0.5f;
            Vector2 t2 = (p3 - p1) * 0.5f;

            // Convert to cubic Bézier control points:
            // CP1 = P1 + T1 / 3
            // CP2 = P2 - T2 / 3
            cp1 = p1 + t1 / 3f;
            cp2 = p2 - t2 / 3f;
            endPoint = p2;
        }

        /// <summary>
        /// Converts a UV coordinate (0–1 range) to pixel coordinates.
        /// </summary>
        private Vector2 UVToPixel(Vector2 uv, int canvasWidth, int canvasHeight)
        {
            return new Vector2(uv.x * canvasWidth, uv.y * canvasHeight);
        }

        /// <summary>
        /// Converts a <see cref="Color"/> to a hex string in the format #RRGGBB.
        /// </summary>
        private static string ColorToHex(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return string.Format("#{0:X2}{1:X2}{2:X2}", r, g, b);
        }

        /// <summary>
        /// Determines whether the pressure values vary meaningfully across a stroke.
        /// A threshold of 5% difference from the first pressure value is used.
        /// </summary>
        private bool HasPressureVariation(float[] pressures)
        {
            if (pressures.Length <= 1)
                return false;

            float baseline = pressures[0];
            const float threshold = 0.05f;

            for (int i = 1; i < pressures.Length; i++)
            {
                if (Mathf.Abs(pressures[i] - baseline) > threshold)
                    return true;
            }

            return false;
        }
    }
}
