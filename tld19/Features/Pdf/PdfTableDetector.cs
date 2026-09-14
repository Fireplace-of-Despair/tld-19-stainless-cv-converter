// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using System.Collections.Generic;
using System.Linq;
using tld19.Composition;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace tld19.Features.Pdf;

/// <summary>
/// Finds tables with visible borders. The detector collects horizontal and vertical ruling lines, joins the
/// lines that cross, and turns each joined set into a grid. A table without borders stays text.
/// </summary>
internal static class PdfTableDetector
{
    private const double Tolerance = Globals.Pdf.Tolerance;

    public static IReadOnlyList<TableGrid> Detect(Page page)
    {
        var horizontals = new List<Segment>();
        var verticals = new List<Segment>();

        foreach (var path in page.Paths)
        {
            if (!path.IsStroked && !path.IsFilled)
            {
                continue;
            }

            foreach (var subpath in path)
            {
                if (subpath.GetBoundingRectangle() is not { } box)
                {
                    continue;
                }

                if (subpath.IsDrawnAsRectangle || !path.IsStroked)
                {
                    AddRectangle(box, path.IsStroked, horizontals, verticals);
                    continue;
                }

                foreach (var line in subpath.Commands.OfType<PdfSubpath.Line>())
                {
                    AddLine(line.From, line.To, horizontals, verticals);
                }
            }
        }

        horizontals = Merge(horizontals);
        verticals = Merge(verticals);

        return Group(horizontals, verticals);
    }

    private static void AddRectangle(PdfRectangle box, bool stroked, List<Segment> horizontals, List<Segment> verticals)
    {
        var left = Math.Min(box.Left, box.Right);
        var right = Math.Max(box.Left, box.Right);
        var bottom = Math.Min(box.Bottom, box.Top);
        var top = Math.Max(box.Bottom, box.Top);
        var width = right - left;
        var height = top - bottom;

        if (height <= Globals.Pdf.RuleThickness && width >= Globals.Pdf.MinRuleLength)
        {
            horizontals.Add(new Segment((top + bottom) / 2, left, right));
        }
        else if (width <= Globals.Pdf.RuleThickness && height >= Globals.Pdf.MinRuleLength)
        {
            verticals.Add(new Segment((left + right) / 2, bottom, top));
        }
        else if (stroked && width >= Globals.Pdf.MinRuleLength && height >= Globals.Pdf.MinRuleLength)
        {
            horizontals.Add(new Segment(top, left, right));
            horizontals.Add(new Segment(bottom, left, right));
            verticals.Add(new Segment(left, bottom, top));
            verticals.Add(new Segment(right, bottom, top));
        }
    }

    private static void AddLine(PdfPoint from, PdfPoint to, List<Segment> horizontals, List<Segment> verticals)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);

        if (dy <= Tolerance && dx >= Globals.Pdf.MinRuleLength)
        {
            horizontals.Add(new Segment((from.Y + to.Y) / 2, Math.Min(from.X, to.X), Math.Max(from.X, to.X)));
        }
        else if (dx <= Tolerance && dy >= Globals.Pdf.MinRuleLength)
        {
            verticals.Add(new Segment((from.X + to.X) / 2, Math.Min(from.Y, to.Y), Math.Max(from.Y, to.Y)));
        }
    }

    /// <summary> Joins the segments that lie on one line and touch or overlap. </summary>
    private static List<Segment> Merge(List<Segment> segments)
    {
        var merged = new List<Segment>();

        foreach (var cluster in ClusterBy(segments, segment => segment.Position))
        {
            var position = cluster.Average(segment => segment.Position);
            Segment? current = null;

            foreach (var segment in cluster.OrderBy(segment => segment.Start))
            {
                if (current is { } open && segment.Start <= open.End + Tolerance)
                {
                    current = open with { End = Math.Max(open.End, segment.End) };
                    continue;
                }

                if (current is { } done)
                {
                    merged.Add(done);
                }

                current = new Segment(position, segment.Start, segment.End);
            }

            if (current is { } last)
            {
                merged.Add(last);
            }
        }

        return merged;
    }

    private static List<TableGrid> Group(List<Segment> horizontals, List<Segment> verticals)
    {
        // Union-find over both lists. A vertical segment takes the index horizontals.Count + its own index.
        var parents = Enumerable.Range(0, horizontals.Count + verticals.Count).ToArray();

        for (var h = 0; h < horizontals.Count; h++)
        {
            for (var v = 0; v < verticals.Count; v++)
            {
                if (Crosses(horizontals[h], verticals[v]))
                {
                    Union(parents, h, horizontals.Count + v);
                }
            }
        }

        var grids = new List<TableGrid>();
        var components = Enumerable.Range(0, parents.Length).GroupBy(index => Find(parents, index));

        foreach (var component in components)
        {
            var rows = component.Where(index => index < horizontals.Count).Select(index => horizontals[index]).ToList();
            var columns = component.Where(index => index >= horizontals.Count).Select(index => verticals[index - horizontals.Count]).ToList();

            var ys = ClusterBy(rows, segment => segment.Position)
                .Select(cluster => cluster.Average(segment => segment.Position))
                .OrderByDescending(y => y)
                .ToList();

            var xs = ClusterBy(columns, segment => segment.Position)
                .Select(cluster => cluster.Average(segment => segment.Position))
                .OrderBy(x => x)
                .ToList();

            if (ys.Count >= 2 && xs.Count >= 2)
            {
                grids.Add(new TableGrid(xs, ys, columns));
            }
        }

        return grids;
    }

    private static bool Crosses(Segment horizontal, Segment vertical) =>
        vertical.Position >= horizontal.Start - Tolerance && vertical.Position <= horizontal.End + Tolerance
        && horizontal.Position >= vertical.Start - Tolerance && horizontal.Position <= vertical.End + Tolerance;

    private static int Find(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }

        return index;
    }

    private static void Union(int[] parents, int a, int b) => parents[Find(parents, a)] = Find(parents, b);

    private static IEnumerable<List<T>> ClusterBy<T>(IEnumerable<T> items, Func<T, double> key)
    {
        List<T>? cluster = null;
        var last = double.NaN;

        foreach (var item in items.OrderBy(key))
        {
            var value = key(item);
            if (cluster is not null && value - last <= Tolerance)
            {
                cluster.Add(item);
            }
            else
            {
                if (cluster is not null)
                {
                    yield return cluster;
                }

                cluster = [item];
            }

            last = value;
        }

        if (cluster is not null)
        {
            yield return cluster;
        }
    }

    /// <summary> A ruling line. <c>Position</c> is Y for a horizontal line and X for a vertical line. </summary>
    internal readonly record struct Segment(double Position, double Start, double End);

    /// <summary> The cell boundaries of one table. <c>Xs</c> run left to right, <c>Ys</c> run top to bottom. </summary>
    internal sealed class TableGrid(IReadOnlyList<double> xs, IReadOnlyList<double> ys, IReadOnlyList<Segment> verticals)
    {
        public int Rows => ys.Count - 1;
        public int Columns => xs.Count - 1;
        public double Top => ys[0];

        public bool Contains(PdfPoint point) =>
            point.X > xs[0] && point.X < xs[^1] && point.Y < ys[0] && point.Y > ys[^1];

        /// <summary> Finds the cell that holds <paramref name="point"/>. A merged cell answers with its first column. </summary>
        public (int Row, int Column) Locate(PdfPoint point)
        {
            var row = 0;
            while (row < Rows - 1 && point.Y < ys[row + 1])
            {
                row++;
            }

            var column = 0;
            while (column < Columns - 1 && point.X > xs[column + 1])
            {
                column++;
            }

            // A missing border on the left of a cell means that the cell continues the cell on its left.
            var middle = (ys[row] + ys[row + 1]) / 2;
            while (column > 0 && !verticals.Any(line =>
                Math.Abs(line.Position - xs[column]) <= Tolerance && line.Start <= middle && line.End >= middle))
            {
                column--;
            }

            return (row, column);
        }
    }
}
