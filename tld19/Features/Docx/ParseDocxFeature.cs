// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System.Threading;
using System.Threading.Tasks;
using Mediator;
using tld19.Features.Shared.Markdown;

namespace tld19.Features.Docx;

/// <summary>
/// Reads a Word 2007 or later document (<c>.docx</c>) into a <see cref="MarkdownDocument"/>. The feature
/// keeps headings, lists, tables and links, and puts a placeholder in the place of each image.
/// </summary>
public sealed class ParseDocxFeature
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
            ctn.ThrowIfCancellationRequested();

            return ValueTask.FromResult(new Result { Document = DocxReader.Read(qry.Path) });
        }
    }
}
