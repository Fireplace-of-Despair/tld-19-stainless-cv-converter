// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using tld19.Features.Docx;
using tld19.Features.Shared.Markdown;
using A = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using Vml = DocumentFormat.OpenXml.Vml;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace tld19Tests.Features.Docx;

// LLM - Claude Opus 5
public class ParseDocxFeature_Tests
{
    private const int BulletList = 1;
    private const int NumberedList = 2;

    [Fact]
    public async Task Handle_StyleBasedOnHeading_WritesHeading()
    {
        var markdown = await ConvertAsync((_, body) =>
        {
            body.Append(StyledParagraph("Heading1", "Top"));
            body.Append(StyledParagraph("Section", "Nested style"));
        });

        Assert.Equal("# Top\n\n## Nested style\n", markdown);
    }

    [Fact]
    public async Task Handle_TitleStyle_MovesHeadingsOneLevelDown()
    {
        var markdown = await ConvertAsync((_, body) =>
        {
            body.Append(StyledParagraph("Title", "Jane Doe"));
            body.Append(StyledParagraph("Heading1", "Experience"));
        });

        Assert.Equal("# Jane Doe\n\n## Experience\n", markdown);
    }

    [Fact]
    public async Task Handle_FieldHyperlink_WritesLink()
    {
        var markdown = await ConvertAsync((_, body) => body.Append(new Paragraph(
            new Run(new Text("See ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" HYPERLINK \"https://example.com/\" \\o \"Tip\" ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("Example")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }),
            new Run(new Text(".")))));

        Assert.Equal("See [Example](https://example.com/).\n", markdown);
    }

    [Fact]
    public async Task Handle_NonLinkField_KeepsResultText()
    {
        var markdown = await ConvertAsync((_, body) => body.Append(new Paragraph(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" PAGE ")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("7")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }))));

        Assert.Equal("7\n", markdown);
    }

    [Fact]
    public async Task Handle_RelationshipHyperlink_WritesLink_AndAnchorHyperlink_WritesText()
    {
        var markdown = await ConvertAsync((main, body) =>
        {
            var relationship = main.AddHyperlinkRelationship(new Uri("https://example.org/cv"), isExternal: true);
            body.Append(new Paragraph(
                new Hyperlink(new Run(new Text("Profile"))) { Id = relationship.Id },
                new Run(new Text(" and ") { Space = SpaceProcessingModeValues.Preserve }),
                new Hyperlink(new Run(new Text("top"))) { Anchor = "_top" }));
        });

        Assert.Equal("[Profile](https://example.org/cv) and top\n", markdown);
    }

    [Fact]
    public async Task Handle_DeletedRevision_IsSkipped()
    {
        var markdown = await ConvertAsync((_, body) => body.Append(new Paragraph(
            new Run(new Text("Kept")),
            new DeletedRun(new Run(new DeletedText(" removed") { Space = SpaceProcessingModeValues.Preserve })),
            new InsertedRun(new Run(new Text(" added") { Space = SpaceProcessingModeValues.Preserve })))));

        Assert.Equal("Kept added\n", markdown);
    }

    [Fact]
    public async Task Handle_NumberingFormat_DecidesListType()
    {
        var markdown = await ConvertAsync((_, body) =>
        {
            body.Append(ListParagraph(NumberedList, 0, "First"));
            body.Append(ListParagraph(NumberedList, 0, "Second"));
            body.Append(ListParagraph(BulletList, 1, "Nested"));
        });

        Assert.Equal("1. First\n2. Second\n    - Nested\n", markdown);
    }

    [Fact]
    public async Task Handle_IndentWithoutLevel_NestsListItem()
    {
        var markdown = await ConvertAsync((_, body) =>
        {
            body.Append(ListParagraph(BulletList, 0, "Outer"));
            var inner = ListParagraph(BulletList, 0, "Inner");
            inner.ParagraphProperties!.Append(new Indentation { Left = "1440" });
            body.Append(inner);
        });

        Assert.Equal("- Outer\n    - Inner\n", markdown);
    }

    [Fact]
    public async Task Handle_MergedCells_KeepTheColumnCount()
    {
        var markdown = await ConvertAsync((_, body) => body.Append(new Table(
            new TableRow(
                Cell("Wide", new TableCellProperties(new GridSpan { Val = 2 })),
                Cell("Tall", new TableCellProperties(new VerticalMerge { Val = MergedCellValues.Restart }))),
            new TableRow(
                Cell("a"),
                Cell("b"),
                Cell("", new TableCellProperties(new VerticalMerge()))))));

        Assert.Equal("| Wide |  | Tall |\n| --- | --- | --- |\n| a | b |  |\n", markdown);
    }

    [Fact]
    public async Task Handle_SingleColumnTable_WritesContentAsBlocks()
    {
        var markdown = await ConvertAsync((_, body) => body.Append(new Table(
            new TableRow(new TableCell(StyledParagraph("Heading1", "Box"), new Paragraph(new Run(new Text("Inside"))))))));

        Assert.Equal("# Box\n\nInside\n", markdown);
    }

    [Fact]
    public async Task Handle_TextBox_FollowsItsParagraph()
    {
        var markdown = await ConvertAsync((_, body) => body.Append(new Paragraph(
            new Run(new Text("Anchor")),
            new Run(new Picture(new Vml.Shape(new Vml.TextBox(
                new TextBoxContent(new Paragraph(new Run(new Text("Boxed")))))))))));

        Assert.Equal("Anchor\n\nBoxed\n", markdown);
    }

    [Fact]
    public async Task Handle_Drawing_WritesImagePlaceholder()
    {
        var markdown = await ConvertAsync((_, body) => body.Append(new Paragraph(
            new Run(new Text("Photo")),
            new Run(new Drawing(new Wp.Inline(new A.Graphic(new A.GraphicData(
                new Pic.Picture(new Pic.BlipFill(new A.Blip { Embed = "rId99" })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })))))));

        Assert.Equal("Photo <image_missing>\n", markdown);
    }

    [Theory]
    [InlineData(" HYPERLINK \"https://a.example\" \\o \"tip\" ", "https://a.example")]
    [InlineData("HYPERLINK \\o \"tip\" \"https://b.example\"", "https://b.example")]
    [InlineData("hyperlink https://c.example", "https://c.example")]
    [InlineData("HYPERLINK \\l \"_Toc1\"", null)]
    [InlineData("PAGEREF _Toc1 \\h", null)]
    [InlineData("", null)]
    public void ParseHyperlinkInstruction_ReadsTheExternalAddress(string instruction, string? expected)
    {
        Assert.Equal(expected, DocxReader.ParseHyperlinkInstruction(instruction));
    }

    private static async Task<string> ConvertAsync(Action<MainDocumentPart, Body> build)
    {
        using var folder = new TestFolder();
        var path = folder.File("test.docx");

        using (var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new Document(new Body());
            AddStyles(main);
            AddNumbering(main);
            build(main, main.Document.Body!);
        }

        var result = await new ParseDocxFeature.Handler().Handle(new ParseDocxFeature.Query { Path = path }, TestContext.Current.CancellationToken);
        return MarkdownWriter.Write(result.Document);
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(
            ParagraphStyle("Normal", "Normal", null),
            ParagraphStyle("Title", "Title", "Normal"),
            ParagraphStyle("Heading1", "heading 1", "Normal"),
            ParagraphStyle("Heading2", "heading 2", "Normal"),
            ParagraphStyle("Section", "Section", "Heading2"));
    }

    private static Style ParagraphStyle(string id, string name, string? basedOn)
    {
        var style = new Style(new StyleName { Val = name }) { Type = StyleValues.Paragraph, StyleId = id };
        if (basedOn is not null)
        {
            style.Append(new BasedOn { Val = basedOn });
        }

        return style;
    }

    private static void AddNumbering(MainDocumentPart main)
    {
        var numbering = main.AddNewPart<NumberingDefinitionsPart>();
        numbering.Numbering = new Numbering(
            AbstractList(10, NumberFormatValues.Bullet),
            AbstractList(20, NumberFormatValues.Decimal),
            new NumberingInstance(new AbstractNumId { Val = 10 }) { NumberID = BulletList },
            new NumberingInstance(new AbstractNumId { Val = 20 }) { NumberID = NumberedList });
    }

    private static AbstractNum AbstractList(int id, NumberFormatValues format) =>
        new(
            new Level(new NumberingFormat { Val = format }) { LevelIndex = 0 },
            new Level(new NumberingFormat { Val = format }) { LevelIndex = 1 })
        { AbstractNumberId = id };

    private static Paragraph StyledParagraph(string styleId, string text) =>
        new(new ParagraphProperties(new ParagraphStyleId { Val = styleId }), new Run(new Text(text)));

    private static Paragraph ListParagraph(int numberingId, int level, string text) =>
        new(
            new ParagraphProperties(new NumberingProperties(
                new NumberingLevelReference { Val = level },
                new NumberingId { Val = numberingId })),
            new Run(new Text(text)));

    private static TableCell Cell(string text, TableCellProperties? properties = null)
    {
        var cell = new TableCell();
        if (properties is not null)
        {
            cell.Append(properties);
        }

        cell.Append(new Paragraph(new Run(new Text(text))));
        return cell;
    }
}
