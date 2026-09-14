// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System.Threading;
using System.Threading.Tasks;
using Mediator;
using tld19.Features.Shared.Markdown;

namespace tld19.Features.Pdf;

/// <summary>
/// Reads a PDF document into a <see cref="MarkdownDocument"/>. A PDF stores glyphs and lines, not structure,
/// so the feature infers headings from font size, lists from bullet glyphs, and tables from ruling lines.
/// </summary>
public sealed class ParsePdfFeature
{
    public sealed record Result
    {
        public required MarkdownDocument Document { get; init; }
    }

    public sealed record Query : IQuery<Result>
    {
        public required string Path { get; init; }
    }

    public sealed class Handler : IQueryHandler<Query, Result>
    {
        public ValueTask<Result> Handle(Query qry, CancellationToken ctn)
        {
            return ValueTask.FromResult(new Result { Document = PdfLayoutReader.Read(qry.Path, ctn) });
        }
    }
}
