# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
# SPDX-License-Identifier: MPL-2.0
# SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

# Writes sample.docx, sample.doc and sample.pdf into this folder. The three files hold one document, so
# the tests can compare the output of the three readers. The script needs Windows and Microsoft Word.

$ErrorActionPreference = 'Stop'

$folder = $PSScriptRoot
$picture = Join-Path ([IO.Path]::GetTempPath()) 'tld19-fixture.png'

# A 1 x 1 red PNG. Word scales it up.
[IO.File]::WriteAllBytes($picture, [Convert]::FromBase64String(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=='))

$wdStyleNormal = -1
$wdStyleHeading1 = -2
$wdStyleHeading2 = -3
$wdStyleTitle = -63
$wdStyleListNumber = -50
$wdStory = 6
$wdFormatDocument97 = 0
$wdFormatDocumentDefault = 16
$wdExportFormatPDF = 17
$wdDoNotSaveChanges = 0

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

try
{
    $document = $word.Documents.Add()
    $selection = $word.Selection

    function Write-Paragraph([int] $style, [string] $text)
    {
        $selection.Style = $document.Styles.Item($style)
        $selection.TypeText($text)
        $selection.TypeParagraph()
    }

    Write-Paragraph $wdStyleTitle 'Jane Doe'

    $selection.Style = $document.Styles.Item($wdStyleNormal)
    $selection.TypeText('Software engineer. Mail: ')
    [void]$document.Hyperlinks.Add($selection.Range, 'mailto:jane@example.com', [Type]::Missing, [Type]::Missing, 'jane@example.com')
    $selection.TypeText('. Site: ')
    [void]$document.Hyperlinks.Add($selection.Range, 'https://example.com/portfolio', [Type]::Missing, [Type]::Missing, 'Portfolio')
    $selection.TypeParagraph()

    Write-Paragraph $wdStyleHeading1 'Experience'
    Write-Paragraph $wdStyleHeading2 'Acme Corp'

    # Direct list formatting: the paragraph carries its own numbering properties.
    $selection.Style = $document.Styles.Item($wdStyleNormal)
    $selection.Range.ListFormat.ApplyBulletDefault()
    $selection.TypeText('Built the billing service')
    $selection.TypeParagraph()
    $selection.Range.ListFormat.ListIndent()
    $selection.TypeText('Cut the invoice run from 4 hours to 20 minutes')
    $selection.TypeParagraph()
    $selection.Range.ListFormat.ListOutdent()
    $selection.TypeText('Led a team of 5')
    $selection.TypeParagraph()
    $selection.Range.ListFormat.RemoveNumbers()

    Write-Paragraph $wdStyleHeading1 'Education'

    # Style list formatting: the List Number style carries the numbering properties.
    Write-Paragraph $wdStyleListNumber 'MSc Computer Science'
    Write-Paragraph $wdStyleListNumber 'BSc Mathematics'

    Write-Paragraph $wdStyleHeading1 'Skills'
    $selection.Style = $document.Styles.Item($wdStyleNormal)

    $table = $document.Tables.Add($selection.Range, 3, 3)
    $table.Borders.Enable = $true
    $cells = @(
        @('Skill', 'Level', 'Years'),
        @('C#', 'Expert', '10'),
        @('SQL', 'Advanced', '8')
    )
    for ($row = 0; $row -lt 3; $row++)
    {
        for ($column = 0; $column -lt 3; $column++)
        {
            $table.Cell($row + 1, $column + 1).Range.Text = $cells[$row][$column]
        }
    }

    [void]$selection.EndKey($wdStory)
    $selection.TypeParagraph()

    $shape = $selection.InlineShapes.AddPicture($picture)
    $shape.Width = 48
    $shape.Height = 48
    $selection.TypeParagraph()

    $selection.TypeText('Notes: use *stars* and [brackets] with care.')
    $selection.TypeParagraph()

    $document.SaveAs2((Join-Path $folder 'sample.docx'), $wdFormatDocumentDefault)
    $document.SaveAs2((Join-Path $folder 'sample.doc'), $wdFormatDocument97)
    $document.ExportAsFixedFormat((Join-Path $folder 'sample.pdf'), $wdExportFormatPDF)
    $document.Close($wdDoNotSaveChanges)
}
finally
{
    $word.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($word)
    Remove-Item $picture -ErrorAction SilentlyContinue
}
