// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using tld19.Composition;

namespace tld19.Features.Shared.Markdown;

/// <summary> Writes a <see cref="MarkdownDocument"/> as CommonMark text with GitHub tables. </summary>
public static partial class MarkdownWriter
{
    private static readonly string s_blockSeparator = new(Globals.Markdown.NewLine, 2);

    /// <summary> Writes <paramref name="document"/> as Markdown text. </summary>
    /// <param name="document"> The blocks to write. </param>
    /// <returns> The Markdown text. The text ends with one line feed, or is empty. </returns>
    public static string Write(MarkdownDocument document)
    {
        var output = new StringBuilder();
        var list = new ListState();
        Block? previous = null;

        foreach (var block in document.Blocks)
        {
            var text = block switch
            {
                HeadingBlock heading => WriteHeading(heading),
                ParagraphBlock paragraph => WriteParagraph(paragraph),
                ListItemBlock item => WriteListItem(item, list),
                TableBlock table => WriteTable(table),
                ImageBlock => Globals.Markdown.ImagePlaceholder,
                _ => string.Empty,
            };

            if (text.Length == 0)
            {
                continue;
            }

            if (block is not ListItemBlock)
            {
                list.Reset();
            }

            if (previous is not null)
            {
                output.Append(previous is ListItemBlock && block is ListItemBlock
                    ? Globals.Markdown.NewLine.ToString()
                    : s_blockSeparator);
            }

            output.Append(text);
            previous = block;
        }

        if (output.Length > 0)
        {
            output.Append(Globals.Markdown.NewLine);
        }

        return output.ToString();
    }

    private static string WriteHeading(HeadingBlock heading)
    {
        var text = string.Join(' ', RenderLines(heading.Inlines, tableCell: false));
        if (text.Length == 0)
        {
            return string.Empty;
        }

        // A trailing '#' closes an ATX heading, and CommonMark drops it from the text.
        if (text.EndsWith('#'))
        {
            text = string.Concat(text.AsSpan(0, text.Length - 1), "\\#");
        }

        var level = Math.Clamp(heading.Level, 1, Globals.Markdown.MaxHeadingLevel);
        return $"{new string('#', level)} {text}";
    }

    private static string WriteParagraph(ParagraphBlock paragraph)
    {
        var lines = RenderLines(paragraph.Inlines, tableCell: false).Select(EscapeLineStart);
        return string.Join(Globals.Markdown.NewLine, lines);
    }

    private static string WriteListItem(ListItemBlock item, ListState list)
    {
        var lines = RenderLines(item.Inlines, tableCell: false);
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var level = Math.Clamp(item.Level, 0, list.LastLevel + 1);
        var marker = list.NextMarker(level, item.Ordered);
        var indent = new string(' ', level * Globals.Markdown.ListIndent);
        var continuation = new string(' ', indent.Length + marker.Length + 1);

        var output = new StringBuilder();
        output.Append(indent).Append(marker).Append(' ').Append(EscapeLineStart(lines[0]));
        foreach (var line in lines.Skip(1))
        {
            output.Append(Globals.Markdown.NewLine).Append(continuation).Append(EscapeLineStart(line));
        }

        return output.ToString();
    }

    private static string WriteTable(TableBlock table)
    {
        var rows = table.Rows
            .Select(row => row.Select(cell => string.Join(Globals.Markdown.TableLineBreak, RenderLines(cell, tableCell: true))).ToList())
            .ToList();

        var columns = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
        if (columns == 0 || rows.All(row => row.All(cell => cell.Length == 0)))
        {
            return string.Empty;
        }

        foreach (var row in rows)
        {
            while (row.Count < columns)
            {
                row.Add(string.Empty);
            }
        }

        var output = new StringBuilder();
        AppendRow(output, rows[0]);
        output.Append(Globals.Markdown.NewLine);
        AppendRow(output, Enumerable.Repeat("---", columns));

        foreach (var row in rows.Skip(1))
        {
            output.Append(Globals.Markdown.NewLine);
            AppendRow(output, row);
        }

        return output.ToString();
    }

    private static void AppendRow(StringBuilder output, IEnumerable<string> cells)
    {
        output.Append('|');
        foreach (var cell in cells)
        {
            output.Append(' ').Append(cell).Append(" |");
        }
    }

    /// <summary> Renders inlines into trimmed, non-empty lines. A line break inline starts a new line. </summary>
    private static List<string> RenderLines(IReadOnlyList<Inline> inlines, bool tableCell)
    {
        var text = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextInline plain:
                    text.Append(Escape(NormalizeBreaks(plain.Text), tableCell));
                    break;
                case LinkInline link:
                    var label = WhitespaceRegex().Replace(NormalizeBreaks(link.Text), " ").Trim();
                    var url = link.Url.Trim();
                    text.Append('[').Append(Escape(label.Length == 0 ? url : label, tableCell)).Append("](")
                        .Append(EscapeUrl(url)).Append(')');
                    break;
                case ImageInline:
                    text.Append(' ').Append(Globals.Markdown.ImagePlaceholder).Append(' ');
                    break;
                case LineBreakInline:
                    text.Append('\n');
                    break;
            }
        }

        return text.ToString()
            .Split('\n')
            .Select(line => WhitespaceRegex().Replace(line, " ").Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static string NormalizeBreaks(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\v', '\n').Replace('\f', '\n');

    private static string Escape(string text, bool tableCell)
    {
        var output = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\\' or '`' or '*' or '[' or ']' or '<' or '~':
                    output.Append('\\').Append(c);
                    break;
                case '|' when tableCell:
                    output.Append("\\|");
                    break;
                // An underscore inside a word never opens emphasis. Leave it bare, so that an address
                // such as first_last@example.com stays readable.
                case '_' when !IsWordChar(text, i - 1) || !IsWordChar(text, i + 1):
                    output.Append("\\_");
                    break;
                default:
                    output.Append(c);
                    break;
            }
        }

        return output.ToString();
    }

    private static bool IsWordChar(string text, int index) =>
        index >= 0 && index < text.Length && char.IsLetterOrDigit(text[index]);

    /// <summary> Escapes a marker at the start of a line that would open a block of its own. </summary>
    private static string EscapeLineStart(string line)
    {
        if (line.Length == 0)
        {
            return line;
        }

        if (line[0] is '#' or '>' or '=' or '+' or '-')
        {
            return "\\" + line;
        }

        var match = OrderedMarkerRegex().Match(line);
        return match.Success
            ? string.Concat(match.Groups[1].Value, "\\", line.AsSpan(match.Groups[1].Length))
            : line;
    }

    private static string EscapeUrl(string url) =>
        url.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29").Replace("<", "%3C").Replace(">", "%3E");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(\d{1,9})[.)](\s|$)")]
    private static partial Regex OrderedMarkerRegex();

    private sealed class ListState
    {
        private readonly List<int> _counters = [];

        public int LastLevel => _counters.Count - 1;

        public string NextMarker(int level, bool ordered)
        {
            while (_counters.Count > level + 1)
            {
                _counters.RemoveAt(_counters.Count - 1);
            }

            while (_counters.Count < level + 1)
            {
                _counters.Add(0);
            }

            _counters[level]++;

            return ordered ? _counters[level].ToString(CultureInfo.InvariantCulture) + "." : "-";
        }

        public void Reset() => _counters.Clear();
    }
}
