// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using tld19.Composition;
using tld19.Features.Shared.Markdown;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;

namespace tld19.Features.Pdf;

/// <summary>
/// Turns the glyphs of a PDF into Markdown blocks. The reader works in two passes. The first pass cuts
/// each page into text blocks, tables and images. The second pass needs the font sizes of the whole
/// document, and it decides which line is a heading, a list item or body text.
/// </summary>
internal static partial class PdfLayoutReader
{
    private const string BulletCharacters = "•●▪■◦‣∙·○□◆◇►▸➢➤✓✔";
    private const string DashCharacters = "-–—*";
    private const string TrailingPunctuation = ".,;:!?)";

    public static MarkdownDocument Read(string path, CancellationToken ctn)
    {
        using var document = PdfDocument.Open(path);

        var items = new List<PageItem>();
        foreach (var page in document.GetPages())
        {
            ctn.ThrowIfCancellationRequested();
            items.AddRange(ReadPage(page));
        }

        var lines = items.OfType<TextItem>().SelectMany(item => item.Lines).ToList();
        var bodySize = BodySize(lines);
        var headingSizes = lines
            .Where(line => IsLargeHeading(line, bodySize))
            .Select(line => RoundSize(line.Size))
            .Distinct()
            .OrderByDescending(size => size)
            .ToList();

        var blocks = new List<Block>();
        foreach (var item in items)
        {
            switch (item)
            {
                case TextItem text:
                    blocks.AddRange(Classify(text, bodySize, headingSizes));
                    break;
                case TableItem table:
                    blocks.Add(table.Table);
                    break;
                case ImageItem:
                    blocks.Add(new ImageBlock());
                    break;
            }
        }

        return new MarkdownDocument(blocks);
    }

    private static List<PageItem> ReadPage(Page page)
    {
        var bullets = new HashSet<Word>();
        var words = AttachBullets(page.GetWords().Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList(), bullets);
        var links = page.GetHyperlinks().Where(link => !string.IsNullOrWhiteSpace(link.Uri)).ToList();
        var images = page.GetImages()
            .Where(image => image.BoundingBox.Width * image.BoundingBox.Height >= Globals.Pdf.MinImageArea)
            .ToList();

        var others = new List<PageItem>();
        var consumed = new HashSet<Word>();

        foreach (var grid in PdfTableDetector.Detect(page))
        {
            var inside = words.Where(word => !consumed.Contains(word) && grid.Contains(word.BoundingBox.Centroid)).ToList();
            var pictures = images.Where(image => grid.Contains(image.BoundingBox.Centroid)).ToList();

            if (BuildTable(grid, inside, pictures, links, bullets) is not { } table)
            {
                continue;
            }

            consumed.UnionWith(inside);
            images.RemoveAll(pictures.Contains);
            others.Add(new TableItem(grid.Top, table));
        }

        others.AddRange(images.Select(image => new ImageItem(image.BoundingBox.Top)));

        var free = words.Where(word => !consumed.Contains(word)).ToList();
        var texts = new List<TextItem>();
        if (free.Count > 0)
        {
            var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(free);
            texts = UnsupervisedReadingOrderDetector.Instance.Get(blocks)
                .OrderBy(block => block.ReadingOrder)
                .Select(block => new TextItem(block.BoundingBox.Top, block.TextLines
                    .OrderByDescending(line => line.BoundingBox.Centroid.Y)
                    .Select(line => MakeLine(line.Words, links, bullets))
                    .ToList()))
                .ToList();
        }

        // Reading order covers text blocks alone. A table or an image goes before the first text block
        // that starts below its top edge.
        var pending = new Queue<PageItem>(others.OrderByDescending(item => item.Top));
        var ordered = new List<PageItem>();

        foreach (var text in texts)
        {
            while (pending.Count > 0 && pending.Peek().Top > text.Top + Globals.Pdf.Tolerance)
            {
                ordered.Add(pending.Dequeue());
            }

            ordered.Add(text);
        }

        ordered.AddRange(pending);
        return ordered;
    }

    private static TableBlock? BuildTable(
        PdfTableDetector.TableGrid grid, List<Word> words, List<IPdfImage> images, List<Hyperlink> links, HashSet<Word> bullets)
    {
        var cellWords = new List<Word>[grid.Rows, grid.Columns];
        var cellImages = new int[grid.Rows, grid.Columns];

        foreach (var word in words)
        {
            var (row, column) = grid.Locate(word.BoundingBox.Centroid);
            (cellWords[row, column] ??= []).Add(word);
        }

        foreach (var image in images)
        {
            var (row, column) = grid.Locate(image.BoundingBox.Centroid);
            cellImages[row, column]++;
        }

        bool HasContent(int row, int column) => cellWords[row, column] is not null || cellImages[row, column] > 0;

        var rows = Enumerable.Range(0, grid.Rows)
            .Where(row => Enumerable.Range(0, grid.Columns).Any(column => HasContent(row, column)))
            .ToList();

        var columns = Enumerable.Range(0, grid.Columns)
            .Where(column => rows.Any(row => HasContent(row, column)))
            .ToList();

        // A bordered box with one cell, or a rule under a heading, is decoration and not a table.
        if (rows.Count < 2 || columns.Count < 2)
        {
            return null;
        }

        IReadOnlyList<IReadOnlyList<IReadOnlyList<Inline>>> table = rows
            .Select(row => (IReadOnlyList<IReadOnlyList<Inline>>)columns
                .Select(column => CellInlines(cellWords[row, column], cellImages[row, column], links, bullets))
                .ToList())
            .ToList();

        return new TableBlock(table);
    }

    private static List<Inline> CellInlines(List<Word>? words, int images, List<Hyperlink> links, HashSet<Word> bullets)
    {
        var inlines = new List<Inline>();

        if (words is not null)
        {
            var lines = new List<List<Word>>();
            foreach (var word in words.OrderByDescending(word => word.BoundingBox.Centroid.Y))
            {
                var last = lines.Count == 0 ? null : lines[^1];
                if (last is not null && Math.Abs(last[0].BoundingBox.Centroid.Y - word.BoundingBox.Centroid.Y)
                    <= Math.Max(last[0].BoundingBox.Height, word.BoundingBox.Height) / 2)
                {
                    last.Add(word);
                }
                else
                {
                    lines.Add([word]);
                }
            }

            foreach (var line in lines)
            {
                if (inlines.Count > 0)
                {
                    inlines.Add(new LineBreakInline());
                }

                inlines.AddRange(ToInlines(MakeLine(line, links, bullets).Tokens));
            }
        }

        for (var i = 0; i < images; i++)
        {
            inlines.Add(new ImageInline());
        }

        return inlines;
    }

    /// <summary>
    /// Joins each lone bullet glyph with the first word to its right. Word puts a tab after a bullet, and the
    /// page segmenter then reads the bullet as a line of its own. The joined word goes into
    /// <paramref name="bullets"/>, and its first letter is the bullet.
    /// </summary>
    private static List<Word> AttachBullets(List<Word> words, HashSet<Word> bullets)
    {
        var result = new List<Word>(words);

        foreach (var bullet in words.Where(IsBulletWord))
        {
            var box = bullet.BoundingBox;
            var baseline = bullet.Letters[0].StartBaseLine.Y;

            var next = result
                .Where(word => word != bullet && !bullets.Contains(word) && !IsBulletWord(word)
                    && word.BoundingBox.Left >= box.Right - Globals.Pdf.Tolerance
                    && word.BoundingBox.Left - box.Right <= Globals.Pdf.MaxBulletGap
                    && Math.Abs(word.Letters[0].StartBaseLine.Y - baseline) <= Math.Max(box.Height, word.BoundingBox.Height) / 2)
                .MinBy(word => word.BoundingBox.Left);

            if (next is null)
            {
                continue;
            }

            var joined = new Word([.. bullet.Letters, .. next.Letters]);
            result.Remove(bullet);
            result.Remove(next);
            result.Add(joined);
            bullets.Add(joined);
        }

        return result;
    }

    private static bool IsBulletWord(Word word)
    {
        if (word.Letters.Count != 1 || word.Text.Length != 1)
        {
            return false;
        }

        var c = word.Text[0];
        var font = word.Letters[0].FontName ?? string.Empty;

        // Word draws the second level with the letter o in Courier New, and deeper levels in Wingdings.
        return IsBulletGlyph(c)
            || (c == 'o' && font.Contains("Courier", StringComparison.OrdinalIgnoreCase))
            || SymbolFontRegex().IsMatch(font);
    }

    private static LineInfo MakeLine(IEnumerable<Word> source, List<Hyperlink> links, HashSet<Word> bullets)
    {
        var words = source.OrderBy(word => word.BoundingBox.Left).ToList();
        var letters = words.SelectMany(word => word.Letters).ToList();
        var sizes = letters.Select(letter => letter.PointSize).Order().ToList();
        var tokens = new List<Token>();

        foreach (var word in words)
        {
            var index = 0;
            if (bullets.Contains(word))
            {
                tokens.Add(new Token(word.Letters[0].Value, null, Glued: false, Bullet: true));
                index = 1;
            }

            // A link can end inside a word, such as the period after an address. Split the word there.
            var wordStart = index;
            while (index < word.Letters.Count)
            {
                var groupStart = index;
                var url = FindLink(word.Letters[index], links);
                var text = new StringBuilder();

                while (index < word.Letters.Count && FindLink(word.Letters[index], links) == url)
                {
                    text.Append(word.Letters[index].Value);
                    index++;
                }

                var value = text.ToString();
                var tail = url is null ? 0 : TrailingPunctuationLength(value, url);

                tokens.Add(new Token(value[..^tail], url, Glued: groupStart > wordStart, Bullet: false));
                if (tail > 0)
                {
                    tokens.Add(new Token(value[^tail..], null, Glued: true, Bullet: false));
                }
            }
        }

        return new LineInfo(
            Tokens: tokens,
            Left: words.Count == 0 ? 0 : words[0].BoundingBox.Left,
            Size: sizes.Count == 0 ? 0 : sizes[sizes.Count / 2],
            Length: letters.Count,
            Bold: letters.Count > 0 && letters.All(IsBold));
    }

    /// <summary>
    /// Counts the punctuation at the end of a link text that the address does not hold. The link rectangle of
    /// a PDF often covers the period after a link, so that period must leave the link.
    /// </summary>
    private static int TrailingPunctuationLength(string text, string url)
    {
        var tail = 0;
        while (tail < text.Length - 1
            && TrailingPunctuation.Contains(text[text.Length - 1 - tail])
            && !url.EndsWith(text[(text.Length - 1 - tail)..], StringComparison.Ordinal))
        {
            tail++;
        }

        return tail;
    }

    private static string? FindLink(Letter letter, List<Hyperlink> links)
    {
        var center = letter.BoundingBox.Centroid;

        foreach (var link in links)
        {
            var box = link.Bounds;
            var left = Math.Min(box.Left, box.Right);
            var right = Math.Max(box.Left, box.Right);
            var bottom = Math.Min(box.Bottom, box.Top) - Globals.Pdf.Tolerance;
            var top = Math.Max(box.Bottom, box.Top) + Globals.Pdf.Tolerance;

            if (center.X >= left && center.X <= right && center.Y >= bottom && center.Y <= top)
            {
                return link.Uri;
            }
        }

        return null;
    }

    private static bool IsBold(Letter letter) =>
        letter.FontDetails?.IsBold == true || BoldFontRegex().IsMatch(letter.FontName ?? string.Empty);

    /// <summary> The body font is the size that carries the most glyphs. </summary>
    private static double BodySize(List<LineInfo> lines) =>
        lines.Count == 0
            ? 0
            : lines.GroupBy(line => RoundSize(line.Size))
                .OrderByDescending(group => group.Sum(line => line.Length))
                .First()
                .Key;

    private static double RoundSize(double size) =>
        Math.Round(size / Globals.Pdf.SizeStep) * Globals.Pdf.SizeStep;

    private static bool IsLargeHeading(LineInfo line, double bodySize) =>
        bodySize > 0
        && line.Size >= bodySize * Globals.Pdf.HeadingSizeRatio
        && line.Text.Length <= Globals.Pdf.MaxHeadingLength
        && DetectBullet(line) is null;

    private static List<Block> Classify(TextItem item, double bodySize, List<double> headingSizes)
    {
        var output = new List<Block>();
        var bulletLefts = ClusterLefts(item.Lines.Where(line => DetectBullet(line) is not null).Select(line => line.Left));

        (int Level, List<Token> Tokens)? heading = null;
        List<LineInfo> paragraph = [];
        ListDraft? listItem = null;

        void FlushHeading()
        {
            if (heading is { } done)
            {
                output.Add(new HeadingBlock(done.Level, ToInlines(done.Tokens)));
            }

            heading = null;
        }

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                var inlines = new List<Inline>();
                foreach (var line in paragraph)
                {
                    if (inlines.Count > 0)
                    {
                        inlines.Add(new LineBreakInline());
                    }

                    inlines.AddRange(ToInlines(line.Tokens));
                }

                output.Add(new ParagraphBlock(inlines));
            }

            paragraph = [];
        }

        void FlushListItem()
        {
            if (listItem is not null)
            {
                output.Add(new ListItemBlock(listItem.Level, listItem.Ordered, ToInlines(listItem.Tokens)));
            }

            listItem = null;
        }

        foreach (var line in item.Lines)
        {
            if (HeadingLevel(line, bodySize, headingSizes) is { } level)
            {
                FlushParagraph();
                FlushListItem();

                if (heading is { } open && open.Level == level)
                {
                    open.Tokens.AddRange(line.Tokens);
                }
                else
                {
                    FlushHeading();
                    heading = (level, [.. line.Tokens]);
                }

                continue;
            }

            FlushHeading();

            if (DetectBullet(line) is { } bullet)
            {
                FlushParagraph();
                FlushListItem();

                var listLevel = bulletLefts.FindIndex(left => Math.Abs(left - line.Left) <= Globals.Pdf.ListLevelTolerance);
                listItem = new ListDraft(Math.Max(0, listLevel), bullet.Ordered, line.Left, [.. bullet.Tokens]);
                continue;
            }

            // A wrapped line of a list item starts right of the bullet.
            if (listItem is not null && line.Left > listItem.Left + Globals.Pdf.Tolerance)
            {
                listItem.Tokens.AddRange(line.Tokens);
                continue;
            }

            FlushListItem();
            paragraph.Add(line);
        }

        FlushHeading();
        FlushParagraph();
        FlushListItem();

        return output;
    }

    private static int? HeadingLevel(LineInfo line, double bodySize, List<double> headingSizes)
    {
        if (IsLargeHeading(line, bodySize))
        {
            var index = headingSizes.IndexOf(RoundSize(line.Size));
            return Math.Clamp(index + 1, 1, Globals.Markdown.MaxHeadingLevel);
        }

        // A short bold line in capitals is a section title at body size, such as EXPERIENCE in a resume.
        var text = line.Text;
        if (line.Bold
            && text.Length <= Globals.Pdf.MaxCapsHeadingLength
            && text.Any(char.IsLetter)
            && text.Where(char.IsLetter).All(char.IsUpper)
            && DetectBullet(line) is null)
        {
            return Math.Min(headingSizes.Count + 1, Globals.Markdown.MaxHeadingLevel);
        }

        return null;
    }

    private static (bool Ordered, List<Token> Tokens)? DetectBullet(LineInfo line)
    {
        if (line.Tokens.Count == 0)
        {
            return null;
        }

        var first = line.Tokens[0].Text;
        var rest = line.Tokens.Skip(1).ToList();
        var standalone = rest.Count > 0 && !rest[0].Glued;

        if (line.Tokens[0].Bullet && rest.Count > 0)
        {
            return (false, rest);
        }

        if (standalone && first.Length == 1 && (IsBulletGlyph(first[0]) || DashCharacters.Contains(first[0])))
        {
            return (false, rest);
        }

        if (first.Length > 1 && IsBulletGlyph(first[0]))
        {
            return (false, [line.Tokens[0] with { Text = first[1..] }, .. rest]);
        }

        if (standalone && OrderedMarkerRegex().IsMatch(first))
        {
            return (true, rest);
        }

        return null;
    }

    // Symbol and Wingdings fonts map their bullets into the private use area.
    private static bool IsBulletGlyph(char c) =>
        BulletCharacters.Contains(c) || c is >= '\uF000' and <= '\uF0FF';

    private static List<double> ClusterLefts(IEnumerable<double> lefts)
    {
        var clusters = new List<double>();
        foreach (var left in lefts.Order())
        {
            if (clusters.Count == 0 || left - clusters[^1] > Globals.Pdf.ListLevelTolerance)
            {
                clusters.Add(left);
            }
        }

        return clusters;
    }

    private static List<Inline> ToInlines(IReadOnlyList<Token> tokens)
    {
        var inlines = new List<Inline>();
        var index = 0;

        while (index < tokens.Count)
        {
            var url = tokens[index].Url;
            var group = tokens.Skip(index).TakeWhile(token => token.Url == url).ToList();

            if (inlines.Count > 0 && !group[0].Glued)
            {
                inlines.Add(new TextInline(" "));
            }

            var text = JoinTokens(group);
            inlines.Add(url is null ? new TextInline(text) : new LinkInline(text, url));
            index += group.Count;
        }

        return inlines;
    }

    private static string JoinTokens(IEnumerable<Token> tokens)
    {
        var text = new StringBuilder();
        foreach (var token in tokens)
        {
            if (text.Length > 0 && !token.Glued)
            {
                text.Append(' ');
            }

            text.Append(token.Text);
        }

        return text.ToString();
    }

    [GeneratedRegex("bold|black|heavy|semibold|demibold", RegexOptions.IgnoreCase)]
    private static partial Regex BoldFontRegex();

    [GeneratedRegex(@"^(\d{1,2}|[a-z])[.)]$")]
    private static partial Regex OrderedMarkerRegex();

    [GeneratedRegex("symbol|wingdings|webdings|dingbat", RegexOptions.IgnoreCase)]
    private static partial Regex SymbolFontRegex();

    /// <summary> A piece of a line. <c>Glued</c> means that no space goes before it. </summary>
    private readonly record struct Token(string Text, string? Url, bool Glued, bool Bullet);

    private sealed record LineInfo(IReadOnlyList<Token> Tokens, double Left, double Size, int Length, bool Bold)
    {
        public string Text { get; } = JoinTokens(Tokens);
    }

    private sealed record ListDraft(int Level, bool Ordered, double Left, List<Token> Tokens);

    private abstract record PageItem(double Top);

    private sealed record TextItem(double Top, IReadOnlyList<LineInfo> Lines) : PageItem(Top);

    private sealed record TableItem(double Top, TableBlock Table) : PageItem(Top);

    private sealed record ImageItem(double Top) : PageItem(Top);
}
