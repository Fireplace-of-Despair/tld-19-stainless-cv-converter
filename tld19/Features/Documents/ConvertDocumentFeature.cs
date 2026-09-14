// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using tld19.Composition;
using tld19.Features.Doc;
using tld19.Features.Docx;
using tld19.Features.Pdf;
using tld19.Features.Shared.Markdown;

namespace tld19.Features.Documents;

/// <summary>
/// Converts one <c>.pdf</c>, <c>.doc</c> or <c>.docx</c> file into a Markdown file. The Markdown file takes
/// the name of the source file with the <c>.md</c> extension, in the same folder, and replaces a file of
/// that name.
/// </summary>
public sealed class ConvertDocumentFeature
{
    public sealed record Result
    {
        public required string OutputPath { get; init; }
    }

    public sealed record Command : ICommand<Result>
    {
        public required string InputPath { get; init; }
    }

    public sealed class Handler(IMediator mediator) : ICommandHandler<Command, Result>
    {
        private static readonly UTF8Encoding s_encoding = new(encoderShouldEmitUTF8Identifier: false);

        public async ValueTask<Result> Handle(Command cmd, CancellationToken ctn)
        {
            var input = Path.GetFullPath(cmd.InputPath);
            if (!File.Exists(input))
            {
                throw new FileNotFoundException("The input file does not exist.", input);
            }

            var document = await ParseAsync(input, ctn);
            var output = Path.ChangeExtension(input, Globals.Markdown.Extension);

            await File.WriteAllTextAsync(output, MarkdownWriter.Write(document), s_encoding, ctn);

            return new Result { OutputPath = output };
        }

        private async ValueTask<MarkdownDocument> ParseAsync(string input, CancellationToken ctn)
        {
            switch (Path.GetExtension(input).ToLowerInvariant())
            {
                case Globals.Formats.Pdf:
                    return (await mediator.Send(new ParsePdfFeature.Query { Path = input }, ctn)).Document;
                case Globals.Formats.Doc:
                    return (await mediator.Send(new ParseDocFeature.Query { Path = input }, ctn)).Document;
                case Globals.Formats.Docx:
                    return (await mediator.Send(new ParseDocxFeature.Query { Path = input }, ctn)).Document;
                default:
                    throw new NotSupportedException(
                        $"The file type '{Path.GetExtension(input)}' is not supported. Use {Globals.Formats.Pdf}, {Globals.Formats.Doc} or {Globals.Formats.Docx}.");
            }
        }
    }
}
