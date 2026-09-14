// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using tld19.Features.Shared.Markdown;

namespace tld19Tests.Features.Shared.Markdown;

// LLM - Claude Opus 5
public class MarkdownWriter_Tests
{
    private static string Write(params Block[] blocks) => MarkdownWriter.Write(new MarkdownDocument(blocks));

    private static Inline[] Text(string text) => [new TextInline(text)];

    [Fact]
    public void Write_Heading_ClampsLevelAndEscapesTrailingHash()
    {
        var markdown = Write(new HeadingBlock(9, Text("Skills C#")));

        Assert.Equal("###### Skills C\\#\n", markdown);
    }

    [Fact]
    public void Write_Paragraph_EscapesInlineSyntax()
    {
        var markdown = Write(new ParagraphBlock(Text("Use *stars*, [links], `code`, ~tilde~ and <tags>")));

        Assert.Equal("Use \\*stars\\*, \\[links\\], \\`code\\`, \\~tilde\\~ and \\<tags>\n", markdown);
    }

    [Fact]
    public void Write_UnderscoreInsideWord_StaysBare()
    {
        var markdown = Write(new ParagraphBlock(Text("first_last@example.com and _emphasis_")));

        Assert.Equal("first_last@example.com and \\_emphasis\\_\n", markdown);
    }

    [Fact]
    public void Write_BlockMarkerAtLineStart_IsEscaped()
    {
        var markdown = Write(new ParagraphBlock(
        [
            new TextInline("# not a heading"),
            new LineBreakInline(),
            new TextInline("- not a list"),
            new LineBreakInline(),
            new TextInline("2019. not a list"),
            new LineBreakInline(),
            new TextInline("> not a quote"),
        ]));

        Assert.Equal("\\# not a heading\n\\- not a list\n2019\\. not a list\n\\> not a quote\n", markdown);
    }

    [Fact]
    public void Write_Paragraph_CollapsesWhitespaceAndDropsEmptyLines()
    {
        var markdown = Write(new ParagraphBlock(
        [
            new TextInline("    indented\t\ttext  "),
            new LineBreakInline(),
            new LineBreakInline(),
            new TextInline("next"),
        ]));

        Assert.Equal("indented text\nnext\n", markdown);
    }

    [Fact]
    public void Write_NestedList_IndentsAndRestartsNumbering()
    {
        var markdown = Write(
            new ListItemBlock(0, true, Text("One")),
            new ListItemBlock(1, false, Text("Sub")),
            new ListItemBlock(0, true, Text("Two")),
            new ParagraphBlock(Text("Break")),
            new ListItemBlock(0, true, Text("New")));

        Assert.Equal("1. One\n    - Sub\n2. Two\n\nBreak\n\n1. New\n", markdown);
    }

    [Fact]
    public void Write_ListLevelJump_NestsOneLevelOnly()
    {
        var markdown = Write(
            new ListItemBlock(0, false, Text("A")),
            new ListItemBlock(3, false, Text("B")));

        Assert.Equal("- A\n    - B\n", markdown);
    }

    [Fact]
    public void Write_ListItemWithLineBreak_IndentsContinuation()
    {
        var markdown = Write(new ListItemBlock(0, true, [new TextInline("Line"), new LineBreakInline(), new TextInline("- more")]));

        Assert.Equal("1. Line\n   \\- more\n", markdown);
    }

    [Fact]
    public void Write_Table_PadsRowsAndEscapesPipes()
    {
        var markdown = Write(new TableBlock(
        [
            [Text("A"), Text("B")],
            [[new TextInline("x|y"), new LineBreakInline(), new TextInline("z")]],
        ]));

        Assert.Equal("| A | B |\n| --- | --- |\n| x\\|y<br>z |  |\n", markdown);
    }

    [Fact]
    public void Write_Image_WritesPlaceholderUnescaped()
    {
        var markdown = Write(
            new ImageBlock(),
            new ParagraphBlock([new TextInline("Logo"), new ImageInline()]));

        Assert.Equal("<image_missing>\n\nLogo <image_missing>\n", markdown);
    }

    [Fact]
    public void Write_Link_EncodesSpacesAndParentheses()
    {
        var markdown = Write(new ParagraphBlock([new LinkInline("Wiki [page]", "https://example.org/a b(c)")]));

        Assert.Equal("[Wiki \\[page\\]](https://example.org/a%20b%28c%29)\n", markdown);
    }

    [Fact]
    public void Write_LinkWithoutText_UsesAddress()
    {
        var markdown = Write(new ParagraphBlock([new LinkInline(" ", "https://example.org")]));

        Assert.Equal("[https://example.org](https://example.org)\n", markdown);
    }

    [Fact]
    public void Write_EmptyBlocks_WriteNothing()
    {
        var markdown = Write(
            new ParagraphBlock(Text("   ")),
            new HeadingBlock(1, []),
            new ListItemBlock(0, false, []),
            new TableBlock([[[], []]]));

        Assert.Equal(string.Empty, markdown);
    }
}
