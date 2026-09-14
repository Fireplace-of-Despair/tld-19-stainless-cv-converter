// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using tld19.Composition;
using tld19.Features.Shared.Markdown;
using MathText = DocumentFormat.OpenXml.Math.Text;

namespace tld19.Features.Docx;

/// <summary> Walks the main body of a WordprocessingML package and builds Markdown blocks. </summary>
internal sealed partial class DocxReader
{
    private const int MaxStyleDepth = 32;
    private const int BodyOutlineLevel = 9;

    // The .doc translator writes v:imageData, and Word writes v:imagedata. The search ignores case.
    private static readonly HashSet<string> s_imageElements = new(["blip", "imagedata", "chart", "relIds"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_textBoxElements = new(["txbxContent"], StringComparer.OrdinalIgnoreCase);

    private readonly MainDocumentPart _main;
    private readonly Dictionary<string, Style> _styles;
    private readonly Dictionary<ListItemBlock, int> _listIndents = new(ReferenceEqualityComparer.Instance);
    private readonly bool _hasTitle;

    private DocxReader(MainDocumentPart main, Body body)
    {
        _main = main;
        _styles = new(StringComparer.Ordinal);

        foreach (var style in main.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value is { } id)
            {
                _styles.TryAdd(id, style);
            }
        }

        _hasTitle = body.Descendants<Paragraph>().Any(IsTitle);
    }

    public static MarkdownDocument Read(string path)
    {
        using var package = WordprocessingDocument.Open(path, isEditable: false);

        var main = package.MainDocumentPart
            ?? throw new InvalidDataException("The document holds no main document part.");

        if (main.Document?.Body is not { } body)
        {
            return new MarkdownDocument([]);
        }

        var reader = new DocxReader(main, body);
        var blocks = new List<Block>();
        reader.ReadBlocks(body, blocks);

        return new MarkdownDocument(reader.NestListsByIndent(blocks));
    }

    /// <summary>
    /// Raises the level of a list item by its indent. Word often keeps the numbering level at 0 and moves the
    /// paragraph with an indent alone, so the numbering level cannot show the nesting that the reader sees.
    /// </summary>
    private List<Block> NestListsByIndent(List<Block> blocks)
    {
        var result = new List<Block>(blocks.Count);
        var run = new List<ListItemBlock>();

        void Flush()
        {
            var indents = run.Select(item => _listIndents.GetValueOrDefault(item)).Distinct().Order().ToList();
            foreach (var item in run)
            {
                var rank = indents.IndexOf(_listIndents.GetValueOrDefault(item));
                result.Add(rank > item.Level ? item with { Level = rank } : item);
            }

            run.Clear();
        }

        foreach (var block in blocks)
        {
            if (block is ListItemBlock item)
            {
                run.Add(item);
                continue;
            }

            Flush();
            result.Add(block);
        }

        Flush();
        return result;
    }

    private void ReadBlocks(OpenXmlElement container, List<Block> target)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Paragraph paragraph:
                    ReadParagraph(paragraph, target);
                    break;
                case Table table:
                    ReadTable(table, target);
                    break;
                case SdtBlock or SdtContentBlock or CustomXmlBlock:
                    ReadBlocks(child, target);
                    break;
                case AlternateContent alternate when PreferredBranch(alternate) is { } branch:
                    ReadBlocks(branch, target);
                    break;
            }
        }
    }

    private void ReadParagraph(Paragraph paragraph, List<Block> target)
    {
        var deferred = new List<Block>();
        var builder = new InlineBuilder(this, deferred);
        builder.Walk(paragraph);
        var inlines = builder.Finish();

        if (HasContent(inlines))
        {
            var heading = HeadingLevel(paragraph);
            if (heading > 0)
            {
                target.Add(new HeadingBlock(heading, inlines));
            }
            else if (ListInfo(paragraph) is { } list)
            {
                var item = new ListItemBlock(list.Level, list.Ordered, inlines);
                _listIndents[item] = list.Indent;
                target.Add(item);
            }
            else
            {
                target.Add(new ParagraphBlock(inlines));
            }
        }

        // A text box floats inside a paragraph. Its content follows the paragraph that anchors it.
        target.AddRange(deferred);
    }

    private void ReadTable(Table table, List<Block> target)
    {
        var rows = new List<List<TableCell?>>();

        foreach (var row in Rows(table))
        {
            var cells = new List<TableCell?>();
            var skipped = row.TableRowProperties?.GetFirstChild<GridBefore>()?.Val?.Value ?? 0;
            cells.AddRange(Enumerable.Repeat<TableCell?>(null, skipped));

            foreach (var cell in Cells(row))
            {
                var properties = cell.TableCellProperties;
                var merge = properties?.VerticalMerge;
                var continued = merge is not null && (merge.Val is null || merge.Val.Value == MergedCellValues.Continue);

                cells.Add(continued ? null : cell);

                var span = properties?.GridSpan?.Val?.Value ?? 1;
                cells.AddRange(Enumerable.Repeat<TableCell?>(null, Math.Max(0, span - 1)));
            }

            rows.Add(cells);
        }

        var columns = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
        if (columns == 0)
        {
            return;
        }

        // A table with one column is a frame for layout and not a grid of data. Its content keeps its own
        // headings and lists.
        if (columns == 1)
        {
            foreach (var cell in rows.SelectMany(row => row).OfType<TableCell>())
            {
                ReadBlocks(cell, target);
            }

            return;
        }

        IReadOnlyList<IReadOnlyList<IReadOnlyList<Inline>>> grid = rows
            .Select(row => (IReadOnlyList<IReadOnlyList<Inline>>)row
                .Select(cell => cell is null ? [] : CellInlines(cell))
                .ToList())
            .ToList();

        target.Add(new TableBlock(grid));
    }

    private List<Inline> CellInlines(TableCell cell)
    {
        var blocks = new List<Block>();
        ReadBlocks(cell, blocks);

        var inlines = new List<Inline>();
        foreach (var block in blocks)
        {
            if (inlines.Count > 0)
            {
                inlines.Add(new LineBreakInline());
            }

            inlines.AddRange(Flatten(block));
        }

        return inlines;
    }

    private static IEnumerable<Inline> Flatten(Block block) => block switch
    {
        HeadingBlock heading => heading.Inlines,
        ParagraphBlock paragraph => paragraph.Inlines,
        ListItemBlock item => [new TextInline("- "), .. item.Inlines],
        ImageBlock => [new ImageInline()],
        TableBlock table => table.Rows.SelectMany((row, index) =>
            (index == 0 ? [] : new Inline[] { new LineBreakInline() })
                .Concat(row.SelectMany((cell, column) =>
                    (column == 0 ? [] : new Inline[] { new TextInline("; ") }).Concat(cell)))),
        _ => [],
    };

    private int HeadingLevel(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        if (properties?.OutlineLevel?.Val?.Value is { } direct)
        {
            return ShiftBelowTitle(ToHeadingLevel(direct));
        }

        foreach (var style in StyleChain(properties?.ParagraphStyleId?.Val?.Value))
        {
            if (IsTitleStyle(style))
            {
                return 1;
            }

            var match = HeadingStyleRegex().Match(style.StyleName?.Val?.Value ?? string.Empty);
            if (!match.Success)
            {
                match = HeadingStyleRegex().Match(style.StyleId?.Value ?? string.Empty);
            }

            if (match.Success)
            {
                return ShiftBelowTitle(int.Parse(match.Groups[1].Value));
            }

            if (style.StyleParagraphProperties?.OutlineLevel?.Val?.Value is { } inherited)
            {
                return ShiftBelowTitle(ToHeadingLevel(inherited));
            }
        }

        return 0;
    }

    /// <summary> A document with a Title paragraph gives the title level 1, so each heading moves one level down. </summary>
    private int ShiftBelowTitle(int level) =>
        level > 0 && _hasTitle ? Math.Min(level + 1, Globals.Markdown.MaxHeadingLevel) : level;

    private bool IsTitle(Paragraph paragraph) =>
        StyleChain(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value).Any(IsTitleStyle);

    private static bool IsTitleStyle(Style style) =>
        string.Equals(style.StyleName?.Val?.Value, "Title", StringComparison.OrdinalIgnoreCase);

    private static int ToHeadingLevel(int outlineLevel) =>
        outlineLevel is >= 0 and < BodyOutlineLevel ? outlineLevel + 1 : 0;

    private (int Level, bool Ordered, int Indent)? ListInfo(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        var numberingId = properties?.NumberingProperties?.NumberingId?.Val?.Value;
        var level = properties?.NumberingProperties?.NumberingLevelReference?.Val?.Value;

        if (numberingId is null)
        {
            foreach (var style in StyleChain(properties?.ParagraphStyleId?.Val?.Value))
            {
                var inherited = style.StyleParagraphProperties?.NumberingProperties;
                level ??= inherited?.NumberingLevelReference?.Val?.Value;
                numberingId = inherited?.NumberingId?.Val?.Value;

                if (numberingId is not null)
                {
                    break;
                }
            }
        }

        // Numbering identifier 0 removes the numbering that a style gives.
        if (numberingId is null or 0)
        {
            return null;
        }

        var resolvedLevel = Math.Max(0, level ?? 0);
        var definition = FindLevel(numberingId.Value, resolvedLevel);
        var format = definition?.NumberingFormat?.Val?.Value;
        var ordered = format is { } value && value != NumberFormatValues.Bullet && value != NumberFormatValues.None;

        return (resolvedLevel, ordered, ListIndent(paragraph, definition));
    }

    private Level? FindLevel(int numberingId, int level)
    {
        var numbering = _main.NumberingDefinitionsPart?.Numbering;
        var instance = numbering?.Elements<NumberingInstance>().FirstOrDefault(item => item.NumberID?.Value == numberingId);
        if (numbering is null || instance is null)
        {
            return null;
        }

        var definition = instance.Elements<LevelOverride>()
            .FirstOrDefault(item => item.LevelIndex?.Value == level)?.Level;

        if (definition is not null)
        {
            return definition;
        }

        var abstractId = instance.AbstractNumId?.Val?.Value;
        return numbering.Elements<AbstractNum>()
            .FirstOrDefault(item => item.AbstractNumberId?.Value == abstractId)?
            .Elements<Level>()
            .FirstOrDefault(item => item.LevelIndex?.Value == level);
    }

    /// <summary> Reads the left indent of a list paragraph in twips. A direct indent wins over the numbering and the style. </summary>
    private int ListIndent(Paragraph paragraph, Level? definition)
    {
        if (Twips(paragraph.ParagraphProperties?.Indentation) is { } direct)
        {
            return direct;
        }

        if (Twips(definition?.PreviousParagraphProperties?.Indentation) is { } numbered)
        {
            return numbered;
        }

        foreach (var style in StyleChain(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value))
        {
            if (Twips(style.StyleParagraphProperties?.Indentation) is { } inherited)
            {
                return inherited;
            }
        }

        return 0;
    }

    private static int? Twips(Indentation? indentation) =>
        int.TryParse(indentation?.Left?.Value ?? indentation?.Start?.Value, out var value) ? value : null;

    private IEnumerable<Style> StyleChain(string? styleId)
    {
        for (var depth = 0; styleId is not null && depth < MaxStyleDepth; depth++)
        {
            if (!_styles.TryGetValue(styleId, out var style))
            {
                yield break;
            }

            yield return style;
            styleId = style.BasedOn?.Val?.Value;
        }
    }

    private string? ResolveHyperlink(Hyperlink link)
    {
        // A link with an anchor alone points inside the document. Markdown gets its text only.
        if (link.Id?.Value is not { } id)
        {
            return null;
        }

        var relationship = _main.HyperlinkRelationships.FirstOrDefault(item => item.Id == id);
        return relationship?.Uri.OriginalString;
    }

    private static bool HasContent(IReadOnlyList<Inline> inlines) =>
        inlines.Any(inline => inline switch
        {
            TextInline text => !string.IsNullOrWhiteSpace(text.Text),
            LinkInline or ImageInline => true,
            _ => false,
        });

    private static OpenXmlElement? PreferredBranch(AlternateContent alternate) =>
        alternate.GetFirstChild<AlternateContentChoice>() ?? (OpenXmlElement?)alternate.GetFirstChild<AlternateContentFallback>();

    private static IEnumerable<TableRow> Rows(OpenXmlElement container)
    {
        foreach (var child in container.ChildElements)
        {
            if (child is TableRow row)
            {
                yield return row;
            }
            else if (child is SdtRow or SdtContentRow or CustomXmlRow)
            {
                foreach (var nested in Rows(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<TableCell> Cells(OpenXmlElement container)
    {
        foreach (var child in container.ChildElements)
        {
            if (child is TableCell cell)
            {
                yield return cell;
            }
            else if (child is SdtCell or SdtContentCell or CustomXmlCell)
            {
                foreach (var nested in Cells(child))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// Finds the elements with a local name from <paramref name="names"/>, and does not look inside a match.
    /// The search skips the fallback branch of markup compatibility, because it repeats the chosen branch.
    /// </summary>
    private static IEnumerable<OpenXmlElement> FindTopLevel(OpenXmlElement root, HashSet<string> names)
    {
        foreach (var child in root.ChildElements)
        {
            if (child is AlternateContentFallback && child.Parent?.GetFirstChild<AlternateContentChoice>() is not null)
            {
                continue;
            }

            if (names.Contains(child.LocalName))
            {
                yield return child;
                continue;
            }

            foreach (var nested in FindTopLevel(child, names))
            {
                yield return nested;
            }
        }
    }

    /// <summary> Reads the target of a <c>HYPERLINK</c> field instruction. </summary>
    /// <returns> The address, or null when the instruction is not an external hyperlink. </returns>
    internal static string? ParseHyperlinkInstruction(string? instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return null;
        }

        var tokens = FieldTokenRegex().Matches(instruction)
            .Select(match => match.Groups[1].Success
                ? (Text: match.Groups[1].Value, Quoted: true)
                : (Text: match.Groups[2].Value, Quoted: false))
            .ToList();

        if (tokens.Count == 0 || !tokens[0].Text.Equals("HYPERLINK", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        for (var i = 1; i < tokens.Count; i++)
        {
            var (text, quoted) = tokens[i];
            if (!quoted && text.StartsWith('\\'))
            {
                // These switches take an argument. \l names an anchor inside the document.
                if (text is "\\l" or "\\o" or "\\t")
                {
                    i++;
                }

                continue;
            }

            if (text.Length > 0)
            {
                return text;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^heading\s*([1-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingStyleRegex();

    [GeneratedRegex("\"([^\"]*)\"|(\\S+)")]
    private static partial Regex FieldTokenRegex();

    private sealed class FieldFrame
    {
        public StringBuilder Instruction { get; } = new();
        public List<Inline> Result { get; } = [];
        public bool InResult { get; set; }
    }

    /// <summary> Collects the inlines of one paragraph. A complex field redirects its result text. </summary>
    private sealed class InlineBuilder(DocxReader reader, List<Block> deferred)
    {
        private readonly List<Inline> _inlines = [];
        private readonly Stack<FieldFrame> _fields = new();

        private List<Inline>? Target => _fields.Count == 0
            ? _inlines
            : _fields.Peek().InResult ? _fields.Peek().Result : null;

        public void Walk(OpenXmlElement element)
        {
            foreach (var child in element.ChildElements)
            {
                switch (child)
                {
                    case ParagraphProperties or RunProperties or DeletedRun or MoveFromRun:
                        break;
                    case Run run:
                        WalkRun(run);
                        break;
                    case Hyperlink link:
                        AddNested(link, reader.ResolveHyperlink(link));
                        break;
                    case SimpleField field:
                        AddNested(field, ParseHyperlinkInstruction(field.Instruction?.Value));
                        break;
                    case AlternateContent alternate:
                        if (PreferredBranch(alternate) is { } branch)
                        {
                            Walk(branch);
                        }

                        break;
                    case MathText math:
                        Target?.Add(new TextInline(math.Text));
                        break;
                    default:
                        if (child.HasChildren)
                        {
                            Walk(child);
                        }

                        break;
                }
            }
        }

        public List<Inline> Finish()
        {
            // A field that the paragraph does not close keeps its result text.
            while (_fields.Count > 0)
            {
                var frame = _fields.Pop();
                AddLinkOrContent(frame.Result, null);
            }

            return _inlines;
        }

        private void WalkRun(OpenXmlElement run)
        {
            foreach (var child in run.ChildElements)
            {
                switch (child)
                {
                    case Text text:
                        Target?.Add(new TextInline(text.Text));
                        break;
                    case TabChar or PositionalTab:
                        Target?.Add(new TextInline(" "));
                        break;
                    case Break br:
                        Target?.Add(br.Type?.Value is null || br.Type.Value == BreakValues.TextWrapping
                            ? new LineBreakInline()
                            : new TextInline(" "));
                        break;
                    case CarriageReturn:
                        Target?.Add(new LineBreakInline());
                        break;
                    case NoBreakHyphen:
                        Target?.Add(new TextInline("-"));
                        break;
                    case Drawing or Picture or EmbeddedObject:
                        AddGraphic(child);
                        break;
                    case AlternateContent alternate:
                        if (PreferredBranch(alternate) is { } branch)
                        {
                            WalkRun(branch);
                        }

                        break;
                    case FieldChar fieldChar:
                        HandleFieldChar(fieldChar);
                        break;
                    case FieldCode code:
                        if (_fields.Count == 0)
                        {
                            break;
                        }

                        // The .doc translator writes the result of a field as instrText and not as w:t. Word
                        // never puts instrText after the separator, so there the text is the result.
                        if (_fields.Peek().InResult)
                        {
                            _fields.Peek().Result.Add(new TextInline(code.Text));
                        }
                        else
                        {
                            _fields.Peek().Instruction.Append(code.Text);
                        }

                        break;
                }
            }
        }

        private void HandleFieldChar(FieldChar fieldChar)
        {
            var type = fieldChar.FieldCharType?.Value;

            if (type == FieldCharValues.Begin)
            {
                _fields.Push(new FieldFrame());
            }
            else if (type == FieldCharValues.Separate && _fields.Count > 0)
            {
                _fields.Peek().InResult = true;
            }
            else if (type == FieldCharValues.End && _fields.Count > 0)
            {
                var frame = _fields.Pop();
                AddLinkOrContent(frame.Result, ParseHyperlinkInstruction(frame.Instruction.ToString()));
            }
        }

        private void AddNested(OpenXmlElement element, string? url)
        {
            var inner = new InlineBuilder(reader, deferred);
            inner.Walk(element);
            AddLinkOrContent(inner.Finish(), url);
        }

        private void AddLinkOrContent(List<Inline> content, string? url)
        {
            var target = Target;
            if (target is null)
            {
                return;
            }

            var text = PlainText(content);
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(text))
            {
                target.AddRange(content);
                return;
            }

            target.Add(new LinkInline(text, url));

            if (content.Any(inline => inline is ImageInline))
            {
                target.Add(new ImageInline());
            }
        }

        private void AddGraphic(OpenXmlElement graphic)
        {
            var target = Target;
            if (target is null)
            {
                return;
            }

            if (graphic is EmbeddedObject || FindTopLevel(graphic, s_imageElements).Any())
            {
                target.Add(new ImageInline());
            }

            foreach (var box in FindTopLevel(graphic, s_textBoxElements))
            {
                reader.ReadBlocks(box, deferred);
            }
        }

        private static string PlainText(IEnumerable<Inline> inlines)
        {
            var text = new StringBuilder();
            foreach (var inline in inlines)
            {
                text.Append(inline switch
                {
                    TextInline plain => plain.Text,
                    LinkInline link => link.Text,
                    LineBreakInline => " ",
                    _ => string.Empty,
                });
            }

            return text.ToString();
        }
    }
}
