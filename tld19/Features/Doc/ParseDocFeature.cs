// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using b2xtranslator.DocFileFormat;
using b2xtranslator.StructuredStorage.Reader;
using b2xtranslator.WordprocessingMLMapping;
using Mediator;
using tld19.Features.Docx;
using tld19.Features.Shared.Markdown;
using WordprocessingDocument = b2xtranslator.OpenXmlLib.WordprocessingML.WordprocessingDocument;

namespace tld19.Features.Doc;

/// <summary>
/// Reads a Word 97-2003 document (<c>.doc</c>). The feature translates the binary document into a temporary
/// WordprocessingML package, and <see cref="ParseDocxFeature"/> reads that package.
/// </summary>
public sealed class ParseDocFeature
{
    public sealed record Result
    {
        public required MarkdownDocument Document { get; init; }
    }

    public sealed record Query : IQuery<Result>
    {
        public required string Path { get; init; }
    }

    public sealed class Handler(IMediator mediator) : IQueryHandler<Query, Result>
    {
        public async ValueTask<Result> Handle(Query qry, CancellationToken ctn)
        {
            var folder = Directory.CreateTempSubdirectory("tld19-");

            try
            {
                var package = Translate(qry.Path, folder.FullName);
                var parsed = await mediator.Send(new ParseDocxFeature.Query { Path = package }, ctn);

                return new Result { Document = parsed.Document };
            }
            finally
            {
                try
                {
                    folder.Delete(recursive: true);
                }
                catch (IOException)
                {
                    // A failed translation can keep a handle on the package. An exception here would hide
                    // the real error of the translation, so the folder stays for the system to clean.
                }
            }
        }

        private static string Translate(string source, string folder)
        {
            using var storage = new StructuredStorageReader(source);
            var document = new WordDocument(storage);
            var type = Converter.DetectOutputType(document);

            // The translator picks the extension from the content: a document with macros becomes .docm.
            var target = Converter.GetConformFilename(Path.Combine(folder, "document.docx"), type);

            // Convert closes the package and writes every part. Never dispose the package as well: a second
            // close writes every zip entry again, and System.IO.Packaging then rejects the file with
            // "Format error in package".
            var package = WordprocessingDocument.Create(target, type);
            Converter.Convert(document, package);

            return target;
        }
    }
}
