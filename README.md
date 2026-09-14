<h1 align="center">CV Converter</h1>

<p align="center">
  <b>A console tool that turns a resume in PDF, DOC or DOCX into a Markdown file.</b><br/>
  It keeps the headings, the lists, the tables and the links. It drops the images.
</p>

<p align="center">
  <img alt="License" src="https://img.shields.io/badge/license-MPL--2.0-blue"/>
  <img alt="Platform" src="https://img.shields.io/badge/.NET-11-512BD4"/>
  <img alt="Formats" src="https://img.shields.io/badge/input-pdf%20%7C%20doc%20%7C%20docx-lightgrey"/>
</p>

---

## Why CV Converter

A resume arrives as a Word file or as a PDF. A language model, a search index or a diff tool reads
plain text much better. Copy and paste loses the structure: a list becomes loose lines, and a table
becomes a run of words.

CV Converter reads the document and writes Markdown. A heading stays a heading, a list stays a list,
and a table stays a table. Each image becomes the text `<image_missing>`, so a reader knows that the
original held a picture at that place.

| | |
|---|---|
| **Three formats, one output** | A `.docx`, a `.doc` and a `.pdf` of the same document give the same Markdown. The tests prove it on one sample. |
| **Local** | The tool reads a file and writes a file. It opens no network connection. |
| **No Office needed** | The tool reads each format with a managed library. It needs neither Word nor LibreOffice. |
| **Small** | One console command, one argument for each file. |

## Usage

```bash
tld19 cv.pdf
```

The tool writes `cv.md` next to `cv.pdf`. Give more than one file to convert each of them.

```bash
tld19 alice.docx bob.doc carol.pdf
```

| Exit code | Meaning |
|---|---|
| `0` | Every file converted. |
| `1` | No file given. The tool shows the usage. |
| `2` | At least one file failed. The error output names each file and the reason. |

**The tool replaces an existing Markdown file of the same name.** Keep that in mind when `cv.docx` and
`cv.pdf` sit in one folder: both write `cv.md`, and the last one wins.

## What the output looks like

Word saved one document as `.docx`, `.doc` and `.pdf`. All three give this file.

```markdown
# Jane Doe

Software engineer. Mail: [jane@example.com](mailto:jane@example.com). Site: [Portfolio](https://example.com/portfolio)

## Experience

### Acme Corp

- Built the billing service
    - Cut the invoice run from 4 hours to 20 minutes
- Led a team of 5

## Skills

| Skill | Level | Years |
| --- | --- | --- |
| C# | Expert | 10 |
| SQL | Advanced | 8 |

<image_missing>
```

`Tests/tld19Tests/Fixtures` holds the three source files and the full expected output.

## What the tool keeps

| Element | DOCX and DOC | PDF |
|---|---|---|
| Headings | The `Title` style and the `heading 1` to `heading 9` styles, also through `basedOn`, and the outline level. A document with a title moves each heading one level down. | A line in a font larger than the body font. A short bold line in capitals, such as `EXPERIENCE`. |
| Lists | The numbering of the paragraph or of its style. The number format decides between bullets and numbers. The level and the indent decide the nesting. | A line that starts with a bullet glyph, a dash, or a marker such as `1.` or `a)`. The indent of the bullet decides the nesting. |
| Tables | Every table with two or more columns. A merged cell keeps the column count. A table with one column is a frame, so its content stays as blocks. | A table with visible borders. A table without borders stays text. |
| Links | Hyperlinks and `HYPERLINK` fields. A link to a bookmark keeps its text only. | Link annotations. A period after a link leaves the link. |
| Images | Pictures, charts, diagrams and embedded objects become `<image_missing>`. The text of a text box follows its paragraph. | Each image becomes `<image_missing>`. |

## Limits

- **PDF structure is a guess.** A PDF stores glyphs and positions, not headings or lists. The reader
  infers them, so an unusual layout can give a wrong heading level or a missed list.
- **A PDF table needs borders.** The reader finds a table through its ruling lines.
- **A scanned PDF gives no text.** The tool does not run text recognition.
- **The tool drops formatting.** Bold, italic, color, font and alignment do not reach the output.
- **The tool reads the main body only.** Headers, footers, footnotes, endnotes and comments do not
  reach the output.
- **`<image_missing>` looks like an HTML tag.** A Markdown renderer can hide it. A text reader and a
  language model see it.
- **A password-protected document fails.**

## Repository layout

| Path | Contents |
|---|---|
| `tld19` | The console tool. |
| `tld19/Composition` | `Globals.cs` with every constant, and `ServiceInjection.cs` with the container. |
| `tld19/Features/Documents` | `ConvertDocumentFeature`: picks the reader by extension and writes the Markdown file. |
| `tld19/Features/Docx` | `ParseDocxFeature` and `DocxReader`. |
| `tld19/Features/Doc` | `ParseDocFeature`: translates `.doc` into a temporary `.docx`, then calls `ParseDocxFeature`. |
| `tld19/Features/Pdf` | `ParsePdfFeature`, `PdfLayoutReader` and `PdfTableDetector`. |
| `tld19/Features/_Shared/Markdown` | The document model that every reader builds, and `MarkdownWriter`. |
| `Tests/tld19Tests` | xUnit v3 tests. `Fixtures` holds the sample documents and the script that writes them. |

## Prerequisites

| Need | Item |
|---|---|
| Build | [.NET 11 SDK](https://dotnet.microsoft.com/download) |
| Editor | [Visual Studio](https://visualstudio.microsoft.com/) or [Visual Studio Code](https://code.visualstudio.com/) |
| New fixtures only | Windows and Microsoft Word |

## Build and run

```bash
dotnet run --project tld19/tld19.csproj -- path/to/cv.pdf
```

Do not point the command at a file in `Tests/tld19Tests/Fixtures`. The tool writes the Markdown file
next to its source, and a stray `sample.md` then sits among the fixtures.

To publish one self-contained executable:

```bash
dotnet publish tld19/tld19.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

```bash
dotnet publish tld19/tld19.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

The publish folder holds the executable, `LICENSE` and `NOTICE`.

## Testing

Tests run on Microsoft Testing Platform v2. The root `global.json` selects the runner.

```bash
dotnet test Tests/tld19Tests/tld19Tests.csproj
```

Apply the format rules from `.editorconfig` before a commit. Name one project.

```bash
dotnet format tld19/tld19.csproj
```

[CONTRIBUTING.md](CONTRIBUTING.md#tests) holds the test layout and the fixture rules.

## Architecture

**Vertical slices.** A feature is one action under `Features/<Area>/<Action>Feature.cs`. The file holds
the `Command` or `Query`, the `Result`, and the `Handler` in one class.
[Mediator](https://github.com/martinothamar/Mediator) dispatches it in process, and its source
generator writes the registration at compile time. The tool is small, so every feature lives in the
one project.

**One model between the readers and the writer.** Each reader builds a `MarkdownDocument`: a list of
headings, paragraphs, list items, tables and images. `MarkdownWriter` alone knows the Markdown syntax
and the escape rules. A new input format needs a new reader, and the writer stays the same.

```
tld19 <file>
  └─ ConvertDocumentFeature
       ├─ .docx → ParseDocxFeature ─────────────────┐
       ├─ .doc  → ParseDocFeature → ParseDocxFeature ┤→ MarkdownDocument → MarkdownWriter → <file>.md
       └─ .pdf  → ParsePdfFeature ──────────────────┘
```

**The PDF reader works in two passes.** The first pass cuts each page into tables, images and text
blocks, and sorts the text blocks into reading order. The second pass reads the font sizes of the whole
document, and then it decides which line is a heading, a list item or body text.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) first. The short version: never commit to `main` or `test`,
branch from `test`, and open one pull request per issue into `test`. Run `dotnet format` and
`dotnet test` before you commit.

## License

CV Converter is open source under the [MPL-2.0](LICENSE).

The MPL works on each file. Run the tool, fork it, or put this code into a closed product, and pay
nothing. When you change a file of this project, publish that file. Your own new files stay under
your own terms.

[NOTICE](NOTICE) lists every third-party component and its license.

Copyright (c) 2026 Shevtsov Stanislav ("Fireplace of Despair").
