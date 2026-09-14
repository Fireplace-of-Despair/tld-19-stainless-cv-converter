// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System.Collections.Generic;

namespace tld19.Features.Shared.Markdown;

/// <summary> The structure that every reader produces and that <see cref="MarkdownWriter"/> writes. </summary>
public sealed record MarkdownDocument(IReadOnlyList<Block> Blocks);

public abstract record Block;

public sealed record HeadingBlock(int Level, IReadOnlyList<Inline> Inlines) : Block;

public sealed record ParagraphBlock(IReadOnlyList<Inline> Inlines) : Block;

/// <summary> One item of a list. Consecutive items form one list. <c>Level</c> starts at 0. </summary>
public sealed record ListItemBlock(int Level, bool Ordered, IReadOnlyList<Inline> Inlines) : Block;

/// <summary> A table. The first row is the header row. Each cell holds a list of inlines. </summary>
public sealed record TableBlock(IReadOnlyList<IReadOnlyList<IReadOnlyList<Inline>>> Rows) : Block;

public sealed record ImageBlock : Block;

public abstract record Inline;

public sealed record TextInline(string Text) : Inline;

public sealed record LinkInline(string Text, string Url) : Inline;

public sealed record ImageInline : Inline;

public sealed record LineBreakInline : Inline;
