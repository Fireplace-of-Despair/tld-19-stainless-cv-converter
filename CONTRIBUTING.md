<h1 align="center">Contributing to CV Converter</h1>

<p align="center">
  Rules for code that lands in this repository. Read this file before your first pull request.
</p>

---

## Before you start

1. Install the [.NET 11 SDK](https://dotnet.microsoft.com/download).
2. Read the rules below for the area that you touch.
3. Run `dotnet format` and `dotnet test` before you push.

<details>
<summary><kbd>Table of contents</kbd></summary>

- [License](#license)
- [Branches and pull requests](#branches-and-pull-requests)
- [Code style](#code-style)
- [Comments](#comments)
- [Project structure](#project-structure)
- [Constants](#constants)
- [Features](#features)
- [The Markdown model](#the-markdown-model)
- [Readers](#readers)
- [Errors](#errors)
- [Tests](#tests)
- [Security](#security)
- [Pull request checklist](#pull-request-checklist)

</details>

---

## License

The MPL-2.0 license covers this repository. You license your contribution under the same license.

[NOTICE](NOTICE) lists every third-party component and its license. Add your package to that list when
your pull request adds a dependency. A new dependency needs a license that the MPL accepts, such as
MIT, BSD or Apache 2.0. Name the license in the pull request.

Every new source file carries this header. The first three lines are Exhibit A of the MPL. A `.ps1` file
writes each line with `#` in place of `//`.

```csharp
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)
```

The header is not a formality. The MPL works on each file, and section 3.4 asks every copy to keep the
notice. A file without the header gives a reader no way to find the license.

Do not copy code from another project into this one. When you port an idea from a licensed source, name
the source in the pull request and wait for an answer before you continue.

## Branches and pull requests

**Never commit to `main` or to `test`.** Branch from `test`. Open the pull request into `test`.

```
your branch --PR--> test --PR--> main
```

| Step | Merge type | Rule |
|---|---|---|
| branch → `test` | Squash | One issue per pull request. The message starts with the issue number and adds a description. |
| `test` → `main` | Merge commit | Open a pull request. |

`main` carries the release. Keep the change small. A large diff hides the defect that a reviewer looks
for.

## Code style

`.editorconfig` is the single source of truth.

```bash
dotnet format tld19/tld19.csproj
```

Format one project at a time.

The project sets `EnforceCodeStyleInBuild` and `AnalysisLevel=latest-minimum`. A style violation appears
during a build. Fix it. Do not suppress it.

| Rule | Value |
|---|---|
| Indentation | 4 spaces. Never tabs. |
| Line ending | CRLF |
| Braces | Allman. The opening brace goes on the next line. |
| `var` | Allowed. Use it when the right side names the type. |
| Target-typed `new()` | Use it when the left side names the type. |
| Collection expressions | Use `[...]`. Do not use `new List<T>()`. |
| Final newline | Required. |
| Trailing whitespace | Trimmed. |
| Characters in source | Write an invisible or private-use character as an escape, such as `''`. |

Write guard clauses. Return early. Keep the happy path at the lowest indentation.

Reformat only the lines that you change. Do not reformat a whole file in a feature pull request.

## Comments

Write a comment only for one of these cases:

- A workaround or a hack.
- A quick fix that a later change must remove.
- A special condition that the code does not show.
- Complex logic, such as a layout heuristic.
- An optimization or a strange decision that a reader will question.

Do not comment self-explanatory code. The code must carry the meaning. Keep comments to a minimum.

**Exception.** Every feature carries an XML `<summary>` comment that describes the feature.

**Exception.** A workaround for a defect of a library names the library and the defect. The next
reader must know when the workaround can go.

Use the imperative mood in a comment of a method: "Gets the value", not "Get the value". Link related
types with `<see cref=""/>`.

## Project structure

The tool is one project. Do not move a feature into a separate assembly.

```
tld19/Program.cs                        // reads the arguments, sends one command per file
tld19/Composition/Globals.cs            // constants
tld19/Composition/ServiceInjection.cs   // DI wiring
tld19/Features/<Area>/                  // one folder per feature area
tld19/Features/_Shared/                 // code that more than one feature uses
```

Keep `Program.cs` thin. It parses the arguments, sends a command, and prints the result.

`ServiceInjection.ConfigureServices` builds the container. The application calls it. The tests call the
same method.

## Constants

Declare every constant in `Composition/Globals.cs`. The class is `internal static class Globals`. Split
the contents into nested static classes by domain: `ExitCodes`, `Formats`, `Markdown`, `Pdf`.

A threshold of the PDF reader is a constant too. Give it a `<summary>` that states the unit and the
effect. A reader that tunes the value must see what it controls.

## Features

A feature is one self-contained action. Put it in one file.

```
tld19/Features/<Area>/<Action>Feature.cs
```

The file holds the `Command` or `Query`, the `Result`, and the `Handler` in one class.

```csharp
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

    public sealed class Handler : IQueryHandler<Query, Result> { }
}
```

The project uses [Mediator](https://github.com/martinothamar/Mediator), a source-generated dispatcher.
The packages are `Mediator.Abstractions` and `Mediator.SourceGenerator`. The generator finds every
handler at compile time and writes the registration into `AddMediator()`. Do not wire a handler into DI
by hand. Dispatch with `IMediator.Send`.

| Case | Name | Kind |
|---|---|---|
| Read a format into the model | `Parse<Format>Feature` | `Query` |
| Write a file | `Convert<Entity>Feature` | `Command` |

A chain of features is acceptable. `ParseDocFeature` translates the file and sends `ParseDocxFeature`.

A feature can keep internal helper classes in its own folder when the feature file grows too large.
`Features/Pdf/PdfLayoutReader.cs` and `Features/Pdf/PdfTableDetector.cs` are examples. A helper serves
its own feature only. Code that two features use goes into `Features/_Shared/<Area>/`.

The namespace of a shared folder ends with `.Shared`. Visual Studio writes `._Shared` instead. Fix it by
hand. The folder name starts with an underscore so that it sorts to the top.

## The Markdown model

`Features/_Shared/Markdown/MarkdownDocument.cs` is the seam between the readers and the writer.

| Block | Inline |
|---|---|
| `HeadingBlock`, `ParagraphBlock`, `ListItemBlock`, `TableBlock`, `ImageBlock` | `TextInline`, `LinkInline`, `ImageInline`, `LineBreakInline` |

Rules for the model:

- A reader never writes Markdown syntax. It puts plain text into `TextInline`. `MarkdownWriter` escapes
  the text.
- A reader never escapes text. An escape in a reader appears twice in the output.
- `ImagePlaceholder` in `Globals.Markdown` is the only text that stands for an image. The writer never
  escapes it.
- A list is a run of consecutive `ListItemBlock` values. `Level` starts at 0. The writer limits a jump
  to one level deeper than the item before.
- The first row of a `TableBlock` is the header row. The writer pads a short row with empty cells.
- A reader can leave an empty block in the model. The writer skips a block without text.

Add a block type only when Markdown has a construct for it and a reader can find it. Update the writer
and its tests in the same pull request.

## Readers

A reader turns one format into a `MarkdownDocument`. Each reader follows the same rules.

- Read the file. Never change it. Never write next to it, except the Markdown file.
- Put a temporary file into a folder from `Directory.CreateTempSubdirectory`. Delete the folder in a
  `finally` block.
- Read the main body of the document. Headers, footers, footnotes and comments stay out.
- Turn every image, chart and embedded object into `ImageInline` or `ImageBlock`.

### DOCX

`DocxReader` walks the body with the Open XML SDK. Five points cost time.

1. **Styles inherit.** A heading or a numbering can come from a style through `basedOn`. Read the whole
   chain through `StyleChain`.
2. **A hyperlink has two forms.** A `w:hyperlink` element names a relationship. A complex field puts
   `HYPERLINK "address"` into `w:instrText` between `begin` and `separate`, and the visible text between
   `separate` and `end`. Fields nest.
3. **Markup compatibility repeats content.** `mc:AlternateContent` holds a `Choice` and a `Fallback`
   with the same content. Read the `Choice` alone, or each text box appears twice.
4. **Word nests a list by indent.** Word often keeps `w:ilvl` at 0 and moves the paragraph with
   `w:ind` alone. `NestListsByIndent` ranks the indents of each run of list items.
5. **A table with one column is a frame.** Its cells go out as blocks, so a heading inside the frame
   stays a heading.

### DOC

`ParseDocFeature` translates the binary file with `b2xtranslator` into a temporary `.docx`, and then
`DocxReader` reads it. Fix a `.doc` defect in `DocxReader` only when Word can write the same markup.
Otherwise mark the workaround as a defect of the translator. [CLAUDE.md](CLAUDE.md#4-traps-the-doc-translator)
lists the known defects.

### PDF

`PdfLayoutReader` infers the structure from geometry. Every rule is a heuristic, so every rule needs a
test with a PDF that shows the case.

| Step | Rule |
|---|---|
| Words | `page.GetWords()` splits on the space glyphs of the file. The nearest-neighbour extractor joins words in Word output, so do not use it. |
| Bullets | A lone bullet glyph joins the first word to its right on the same baseline, up to `MaxBulletGap`. |
| Tables | `PdfTableDetector` joins crossing ruling lines into a grid. A grid with fewer than two filled rows or two filled columns is decoration. |
| Blocks | `DocstrumBoundingBoxes` cuts the free words into blocks. `UnsupervisedReadingOrderDetector` orders them. A table or an image goes before the first text block below its top edge. |
| Body size | The font size that carries the most glyphs in the whole document. |
| Headings | A line at `HeadingSizeRatio` times the body size or larger. The largest size is level 1. A short bold line in capitals takes the level below the smallest size. |
| Links | A letter belongs to a link when its center is inside the link rectangle. Trailing punctuation that the address does not hold leaves the link. |

## Errors

Throw a standard exception with a message that a user can act on.

| Case | Exception |
|---|---|
| The input file does not exist | `FileNotFoundException` |
| The extension is not `.pdf`, `.doc` or `.docx` | `NotSupportedException` |
| A reader cannot read the file | The exception of the library |

`Program` catches every exception for each file, prints the path and the message, and goes on with the
next file. The exit code then becomes `Globals.ExitCodes.Failure`.

A feature never writes a partial Markdown file. Build the whole text first, then write the file once.

## Tests

The repository runs on Microsoft Testing Platform v2. The root `global.json` selects the runner.

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

A test project references `xunit.v3.mtp-v2`, sets `<OutputType>Exe</OutputType>` and
`<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`, and adds none of `xunit`,
`xunit.v3`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` or `coverlet.collector`.

### Layout

The test path mirrors the target path.

```
target:  tld19/Features/Pdf/ParsePdfFeature.cs
test:    Tests/tld19Tests/Features/Pdf/ParsePdfFeature_Tests.cs
```

| Kind | How |
|---|---|
| Writer | Build a `MarkdownDocument` by hand. Compare the whole output string. |
| DOCX reader | Build the package in the test with the Open XML SDK. Add only the parts that the case needs. |
| PDF reader | Build the file in the test with `PdfDocumentBuilder`. Draw the text, the lines, the links and the images that the case needs. |
| Conversion | Send `ConvertDocumentFeature.Command` through the container from `ServiceInjection`. |

Write each file into a `TestFolder`. The folder deletes itself on dispose. Never write into the output
folder of the test project.

### Fixtures

`Tests/tld19Tests/Fixtures` holds one document in three forms: `sample.docx`, `sample.doc` and
`sample.pdf`. Word wrote all three. `expected.md` holds the one output that each form must give.

`New-Fixtures.ps1` writes the three files through Word automation. Run it on Windows with Word.

```bash
pwsh Tests/tld19Tests/Fixtures/New-Fixtures.ps1
```

When you change the script, run it, convert the three files, and compare the three outputs. When they
agree and they are correct, replace `expected.md`. When they disagree, fix the reader that differs.
Never edit `expected.md` to match one reader alone.

### Commands

```bash
dotnet test Tests/tld19Tests/tld19Tests.csproj
```

```bash
dotnet test Tests/tld19Tests/tld19Tests.csproj -- --filter-class "tld19Tests.Features.Pdf.ParsePdfFeature_Tests"
```

A filter that matches nothing ends the run with exit code 8, not with exit code 0.

### Writing tests

- Mark a generated test class with a comment: `// LLM - <model-name> <model-version>`.
- Name a test `Subject_Condition_Result`. The name reads as a sentence.

  ```csharp
  Handle_LinkAnnotation_WritesLinkWithoutTrailingPeriod
  Write_UnderscoreInsideWord_StaysBare
  ```

- Pass `TestContext.Current.CancellationToken` to every call that takes a token.
- Read the existing tests in the target folder before you add a new one.

## Security

The tool reads documents from strangers. Treat every input as hostile.

- **Never run content from a document.** Macros, fields that start a program, and embedded objects stay
  data.
- **Never open a network connection.** A remote image, a linked template, and an external field stay
  unresolved.
- **Never follow a path from inside a document.** An `INCLUDEPICTURE` or `INCLUDETEXT` field names a
  file of the attacker. Read its result text alone.
- **Never write outside the folder of the source file and the temporary folder of the system.**
- **Delete every temporary file.** A translated `.doc` holds the full text of the resume.
- **Never log the text of a document.** A resume holds personal data.

Report a vulnerability in private. Do not open a public issue for it.

## Pull request checklist

- [ ] Every new source file carries the MPL header.
- [ ] The branch starts from `test` and the pull request targets `test`.
- [ ] The change covers one issue. The message names the issue number.
- [ ] `dotnet format` reports no change.
- [ ] `dotnet build` reports no warning.
- [ ] `dotnet test` passes.
- [ ] A new reader rule has a test that shows the case.
- [ ] A change of the fixture script comes with new fixtures and one `expected.md` for all three.
- [ ] A new dependency appears in `NOTICE` with its license.
- [ ] No text of a real resume appears in the diff or in a test.
- [ ] Every new file uses CRLF line endings.
