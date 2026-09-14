// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using tld19.Composition;
using tld19.Features.Documents;

namespace tld19Tests.Features.Documents;

/// <summary>
/// The three fixtures hold one document that Word saved as .docx, as .doc and as .pdf. Each reader must
/// write the same Markdown, so one expected file serves all three.
/// </summary>
// LLM - Claude Opus 5
public class ConvertDocumentFeature_Tests
{
    private readonly IMediator _mediator = ServiceInjection.ConfigureServices().GetRequiredService<IMediator>();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("sample.docx")]
    [InlineData("sample.doc")]
    [InlineData("sample.pdf")]
    public async Task Handle_WordFixture_WritesExpectedMarkdown(string fixture)
    {
        using var folder = new TestFolder();
        var input = folder.CopyFixture(fixture);

        var result = await SendAsync(input);

        Assert.Equal(folder.File("sample.md"), result.OutputPath);
        Assert.Equal(await File.ReadAllTextAsync(TestFolder.Fixture("expected.md"), Token), await File.ReadAllTextAsync(result.OutputPath, Token));
    }

    [Fact]
    public async Task Handle_Output_HasNoByteOrderMark()
    {
        using var folder = new TestFolder();
        var input = folder.CopyFixture("sample.docx");

        var result = await SendAsync(input);
        var bytes = await File.ReadAllBytesAsync(result.OutputPath, Token);

        Assert.Equal((byte)'#', bytes[0]);
    }

    [Fact]
    public async Task Handle_ExistingMarkdown_IsReplaced()
    {
        using var folder = new TestFolder();
        var input = folder.CopyFixture("sample.docx");
        await File.WriteAllTextAsync(folder.File("sample.md"), "stale content that is much longer than nothing", Token);

        var result = await SendAsync(input);

        Assert.StartsWith("# Jane Doe\n", await File.ReadAllTextAsync(result.OutputPath, Token));
        Assert.DoesNotContain("stale", await File.ReadAllTextAsync(result.OutputPath, Token));
    }

    [Fact]
    public async Task Handle_UpperCaseExtension_IsSupported()
    {
        using var folder = new TestFolder();
        var input = folder.File("CV.DOCX");
        File.Copy(TestFolder.Fixture("sample.docx"), input);

        var result = await SendAsync(input);

        Assert.Equal(".md", Path.GetExtension(result.OutputPath));
        Assert.True(File.Exists(result.OutputPath));
    }

    [Fact]
    public async Task Handle_MissingFile_ThrowsFileNotFound()
    {
        using var folder = new TestFolder();

        await Assert.ThrowsAsync<FileNotFoundException>(() => SendAsync(folder.File("absent.pdf")).AsTask());
    }

    [Fact]
    public async Task Handle_UnsupportedExtension_ThrowsNotSupported_AndWritesNothing()
    {
        using var folder = new TestFolder();
        var input = folder.File("cv.txt");
        await File.WriteAllTextAsync(input, "plain text", Token);

        await Assert.ThrowsAsync<NotSupportedException>(() => SendAsync(input).AsTask());
        Assert.False(File.Exists(folder.File("cv.md")));
    }

    [Fact]
    public async Task Handle_BrokenDocument_Throws_AndWritesNothing()
    {
        using var folder = new TestFolder();
        var input = folder.File("broken.docx");
        await File.WriteAllTextAsync(input, "not a zip package", Token);

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(input).AsTask());
        Assert.False(File.Exists(folder.File("broken.md")));
    }

    private ValueTask<ConvertDocumentFeature.Result> SendAsync(string path) =>
        _mediator.Send(new ConvertDocumentFeature.Command { InputPath = path }, Token);
}
