# CLAUDE.md

This file guides Claude Code (claude.ai/code) in this repository.

Two other files hold the documentation. Read the one that covers your change.

| File | Contents |
|---|---|
| [CONTRIBUTING.md](CONTRIBUTING.md) | Every rule for code that lands here. Read it before a change. |
| [README.md](README.md) | The product, the usage, the limits, and the commands that build and test it. |

This file holds two things that the other files do not: the writing rules, and the traps that no other
file covers. Every other rule lives in the file above that owns it, and this file names the rule in one
line and links to it. When a fact belongs in one of those files, write it there.

---

## 1. Writing rules (mandatory)

**Write every piece of prose in ASD-STE100 Simplified Technical English.** This rule is not a style
preference. Apply it without a reminder.

| Applies to | Does not apply to |
|---|---|
| Every `.md` file of this repository | Code, identifiers, command syntax |
| Code comments and XML `<summary>` blocks | The Markdown that the tool writes |
| Commit messages and pull-request text | Quoted output from a tool |
| Error messages that the tool prints | Text inside a test fixture |

### Words

- Use one name for one thing. Never call the same item by two names.
- Give each word one meaning. "Fall" means to move down. It does not mean to decrease.
- Use the short common word: start, not initiate. use, not utilize. help, not facilitate. make sure,
  not ensure. before and after, not prior to and subsequent to. about, not regarding. get, not obtain.
  show, not demonstrate. also, not additionally.
- Never use a marketing adjective: seamless, robust, powerful, cutting-edge, effortless, world-class,
  next-generation, revolutionary.
- Use American spelling.

### Sentences

- Write in the active voice. "The parser reads the file." Not "the file is read by the parser."
- Use a verb for an action. "Analyze the log." Not "perform an analysis of the log."
- Never stack auxiliaries. Never use an `-ing` main verb where a simple tense works.
- Write one instruction per sentence. Keep an instruction under 20 words. Keep a descriptive sentence
  under 25 words.
- Never use a contraction. Write "does not", not "doesn't".
- Never use a semicolon in prose. Write two sentences. Avoid the em dash.

### Structure

- Cover one topic per paragraph. Keep a paragraph under six sentences.
- Write steps as a numbered vertical list. One action per item. Use the imperative form.
- Put a condition before its command. "When the outputs differ, fix the reader."

### Modes

| Mode | Use for | Rule set |
|---|---|---|
| **strict** | Procedures, error messages, security text | Every rule. Both length caps. |
| **STE-flavored** | `README.md`, pull-request text, architecture notes | The sentence, paragraph and active-voice rules. A wider vocabulary. |

Run the rules above over every text that you produce, before you return it. These rules fix the form
of the text. They cannot make a false statement true. Verify each claim against the code.

---

## 2. Overview

CV Converter is a console tool. It reads a `.pdf`, `.doc` or `.docx` file and writes Markdown with the
same name next to it. It keeps headings, lists, tables and links, and it writes `<image_missing>` in
the place of each image.

`tld19` is the only project. Do not move a feature into a separate assembly. The tool is too small for
that. Each reader builds a `MarkdownDocument`, and `MarkdownWriter` alone writes Markdown syntax.

[README.md](README.md#repository-layout) holds the layout of the repository.

---

## 3. Commands

Run these from the repository root.

```bash
dotnet build tld19.slnx
```

```bash
dotnet test Tests/tld19Tests/tld19Tests.csproj
```

```bash
dotnet format tld19/tld19.csproj
```

Run `dotnet format` on each project before a commit, and name one project.

| Read | For |
|---|---|
| [CONTRIBUTING.md](CONTRIBUTING.md#tests) | The test layout, the fixtures and the filter form |
| [CONTRIBUTING.md](CONTRIBUTING.md#code-style) | The style rules and the analysis settings |
| [CONTRIBUTING.md](CONTRIBUTING.md#readers) | The rules and the heuristics of each reader |

---

## 4. Traps: the doc translator

`b2xtranslator` turns a `.doc` file into a `.docx` package. Three of its defects look like defects of
this project.

**Never dispose the translated package.** `Converter.Convert` closes the package and writes every part.
A `using` block closes it a second time, and the second close writes every zip entry again. The package
then holds each part twice. `System.IO.Packaging` rejects that file with `Format error in package`. The
message names neither the translator nor the duplicate entries.

**The translator writes the result of a field as `w:instrText`.** Word writes the visible text of a
field as `w:t` after the `separate` mark. The translator writes it as `w:instrText`. A reader that takes
`w:instrText` for instruction text alone loses the text of every hyperlink in a `.doc`. The link and its
text both disappear, and nothing fails. `DocxReader` reads `w:instrText` after `separate` as result text.

**The translator writes `v:imageData`.** Word writes `v:imagedata`. XML names are case-sensitive, so a
search for the Word spelling finds no picture in a `.doc`. `DocxReader` compares the local names of
image elements without case.

| Trap | Effect |
|---|---|
| `InvariantGlobalization` set to `true` | The translator creates `CultureInfo("en-us")` and every `.doc` fails with `Only the invariant culture is supported`. Keep it `false`. |
| `WordprocessingDocument` | Two types carry this name: one in the Open XML SDK and one in `b2xtranslator.OpenXmlLib.WordprocessingML`. `ParseDocFeature.cs` imports the second through an alias. |

---

## 5. Traps: the PDF reader

**Do not use `NearestNeighbourWordExtractor`.** It joins letters by distance. On the sample PDF from
Word it wrote `JaneDoe` and `Builtthe billingservice`, and the
heading and list rules then see wrong text. `page.GetWords()` splits on the space glyphs.

**A bullet from Word is a line of its own.** Word puts a tab after a bullet glyph. The page segmenter
then puts the glyph into a separate line or block, and no line starts with a bullet. `AttachBullets`
joins the glyph with the first word to its right before the segmenter runs.

**A link rectangle covers the period after the link.** The rectangle of a PDF from Word reaches past
the last letter. The center of a following period sits inside it. `TrailingPunctuationLength` moves
punctuation that the address does not hold out of the link.

| Trap | Effect |
|---|---|
| `IPdfImage.Bounds`, `Letter.Font`, `Letter.GlyphRectangle` | Obsolete in PdfPig 0.1.16. Use `BoundingBox` and `FontDetails`. |
| PDF coordinates | The Y axis points up. A larger `Top` is higher on the page. Sort lines with `OrderByDescending`. |
| Symbol fonts | Symbol and Wingdings fonts put their bullets into the private use area, U+F000 to U+F0FF. Word draws the second list level as the letter `o` in Courier New. |
| The body size in a test | The reader takes the most frequent font size as the body size. A test page with a heading and no body text has no heading. Add body lines, as `AddBody` does. |

---

## 6. Traps: the DOCX reader

| Trap | Effect |
|---|---|
| `mc:AlternateContent` | The `Choice` and the `Fallback` hold the same text box. A reader that walks both writes it twice. `PreferredBranch` and `FindTopLevel` skip the `Fallback`. |
| `w:ilvl` | Word keeps level 0 when it indents a list item with the indent button. Only `w:ind` shows the nesting. See `NestListsByIndent`. |
| The `Title` style | A document with a title moves each heading one level down. A test that expects `# Heading` must not add a title. |
| A literal character in C# source | An editor can write `''` as the invisible character itself. Keep the escape. Search the sources for the private use area after an edit through a tool. |

---

## 7. Security invariants

[CONTRIBUTING.md](CONTRIBUTING.md#security) holds every invariant. A change that breaks one does not
merge. The input is a document from a stranger: never run its content, never open the network, never
follow a path from inside it, and delete every temporary file.

---

## 8. Conventions

[CONTRIBUTING.md](CONTRIBUTING.md) owns the rules for branches, comments, naming and project structure.
Two of them come up in almost every change.

- Never commit to `main` or to `test`. Branch from `test`, and open the pull request into `test`. The
  flow is `branch → test → main`. A pull request into `test` is a squash commit that names one issue.
- Write a comment only for a workaround, a hack, a quirk, complex logic, or a non-obvious decision.
  Every feature needs an XML `<summary>` comment.
