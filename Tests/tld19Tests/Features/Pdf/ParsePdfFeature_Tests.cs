// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using tld19.Features.Pdf;
using tld19.Features.Shared.Markdown;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace tld19Tests.Features.Pdf;

// LLM - Claude Opus 5
public class ParsePdfFeature_Tests
{
    private const string Body = "The body text of the document runs in this size across several lines.";

    // A 1 x 1 red PNG.
    private static readonly byte[] s_png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Handle_LargerFonts_WriteHeadingLevelsBySize()
    {
        var markdown = await ConvertAsync((page, fonts) =>
        {
            page.AddText("Jane Doe", 24, new PdfPoint(50, 780), fonts.Regular);
            page.AddText("Experience", 16, new PdfPoint(50, 740), fonts.Regular);
            AddBody(page, fonts, 715, 3);
        });

        Assert.StartsWith("# Jane Doe\n\n## Experience\n\n", markdown);
    }

    [Fact]
    public async Task Handle_BoldCapitalsAtBodySize_WriteHeading()
    {
        var markdown = await ConvertAsync((page, fonts) =>
        {
            page.AddText("EXPERIENCE", 11, new PdfPoint(50, 780), fonts.Bold);
            AddBody(page, fonts, 755, 3);
        });

        Assert.StartsWith("# EXPERIENCE\n\n", markdown);
    }

    [Fact]
    public async Task Handle_BulletWithGap_WritesListItems()
    {
        var markdown = await ConvertAsync((page, fonts) =>
        {
            AddBody(page, fonts, 780, 2);
            page.AddText("•", 11, new PdfPoint(50, 730), fonts.Regular);
            page.AddText("Built the billing service", 11, new PdfPoint(68, 730), fonts.Regular);
            page.AddText("•", 11, new PdfPoint(50, 716), fonts.Regular);
            page.AddText("Led a team of five", 11, new PdfPoint(68, 716), fonts.Regular);
        });

        Assert.EndsWith("- Built the billing service\n- Led a team of five\n", markdown);
    }

    [Fact]
    public async Task Handle_RuledGrid_WritesTable()
    {
        var markdown = await ConvertAsync((page, fonts) =>
        {
            AddBody(page, fonts, 780, 2);

            double[] xs = [50, 200, 350];
            double[] ys = [700, 680, 660];
            foreach (var y in ys)
            {
                page.DrawLine(new PdfPoint(xs[0], y), new PdfPoint(xs[^1], y), 0.5);
            }

            foreach (var x in xs)
            {
                page.DrawLine(new PdfPoint(x, ys[0]), new PdfPoint(x, ys[^1]), 0.5);
            }

            page.AddText("Skill", 11, new PdfPoint(55, 686), fonts.Regular);
            page.AddText("Years", 11, new PdfPoint(205, 686), fonts.Regular);
            page.AddText("SQL", 11, new PdfPoint(55, 666), fonts.Regular);
            page.AddText("8", 11, new PdfPoint(205, 666), fonts.Regular);
        });

        Assert.Contains("| Skill | Years |\n| --- | --- |\n| SQL | 8 |", markdown);
    }

    [Fact]
    public async Task Handle_ColumnsWithoutBorders_WriteNoTable()
    {
        var markdown = await ConvertAsync((page, fonts) =>
        {
            AddBody(page, fonts, 780, 2);
            page.AddText("Skill", 11, new PdfPoint(50, 700), fonts.Regular);
            page.AddText("Years", 11, new PdfPoint(200, 700), fonts.Regular);
        });

        Assert.DoesNotContain("|", markdown);
    }

    [Fact]
    public async Task Handle_LinkAnnotation_WritesLinkWithoutTrailingPeriod()
    {
        var markdown = await ConvertAsync((page, fonts) =>
        {
            AddBody(page, fonts, 780, 2);
            var letters = page.AddText("Mail: jane@example.com.", 11, new PdfPoint(50, 730), fonts.Regular);

            // The rectangle covers the period, as the rectangle of a PDF from Word does.
            var link = letters.Skip("Mail: ".Length).ToList();
            page.AddLink("mailto:jane@example.com", new PdfRectangle(
                link.Min(letter => letter.BoundingBox.Left) - 1,
                link.Min(letter => letter.BoundingBox.Bottom) - 2,
                link.Max(letter => letter.BoundingBox.Right) + 1,
                link.Max(letter => letter.BoundingBox.Top) + 2));
        });

        Assert.EndsWith("Mail: [jane@example.com](mailto:jane@example.com).\n", markdown);
    }

    [Fact]
    public async Task Handle_Image_WritesPlaceholder()
    {
        var markdown = await ConvertAsync((page, fonts) =>
        {
            page.AddPng(s_png, new PdfRectangle(50, 740, 98, 788));
            AddBody(page, fonts, 720, 2);
        });

        Assert.StartsWith("<image_missing>\n\n", markdown);
    }

    private static void AddBody(PdfPageBuilder page, Fonts fonts, double top, int lines)
    {
        for (var i = 0; i < lines; i++)
        {
            page.AddText(Body, 11, new PdfPoint(50, top - (i * 14)), fonts.Regular);
        }
    }

    private static async Task<string> ConvertAsync(Action<PdfPageBuilder, Fonts> build)
    {
        using var folder = new TestFolder();
        var path = folder.File("test.pdf");

        using (var builder = new PdfDocumentBuilder())
        {
            var fonts = new Fonts(
                builder.AddStandard14Font(Standard14Font.Helvetica),
                builder.AddStandard14Font(Standard14Font.HelveticaBold));

            build(builder.AddPage(PageSize.A4), fonts);
            await File.WriteAllBytesAsync(path, builder.Build(), TestContext.Current.CancellationToken);
        }

        var result = await new ParsePdfFeature.Handler().Handle(new ParsePdfFeature.Query { Path = path }, TestContext.Current.CancellationToken);
        return MarkdownWriter.Write(result.Document);
    }

    private sealed record Fonts(PdfDocumentBuilder.AddedFont Regular, PdfDocumentBuilder.AddedFont Bold);
}
