// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System.Reflection;

namespace tld19.Composition;

internal static class Globals
{
    internal static class Brand
    {
        public static string Product =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product
                ?? string.Empty;

        public static string Title =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyTitleAttribute>()?.Title
                ?? string.Empty;

        public static string Version =>
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
                ?? string.Empty;
    }

    internal static class ExitCodes
    {
        internal const int Success = 0;
        internal const int Usage = 1;
        internal const int Failure = 2;
    }

    /// <summary> The file extensions that the converter reads. Compare them in lower case. </summary>
    internal static class Formats
    {
        internal const string Pdf = ".pdf";
        internal const string Doc = ".doc";
        internal const string Docx = ".docx";
    }

    internal static class Markdown
    {
        internal const string Extension = ".md";
        internal const char NewLine = '\n';

        /// <summary> The text that takes the place of every image. The writer never escapes it. </summary>
        internal const string ImagePlaceholder = "<image_missing>";

        internal const string TableLineBreak = "<br>";
        internal const int ListIndent = 4;
        internal const int MaxHeadingLevel = 6;
    }

    /// <summary> Layout thresholds for the PDF reader. All lengths are PDF points. </summary>
    internal static class Pdf
    {
        /// <summary> A line with a font this much larger than the body font is a heading. </summary>
        internal const double HeadingSizeRatio = 1.15;

        internal const int MaxHeadingLength = 150;
        internal const int MaxCapsHeadingLength = 60;

        /// <summary> Two font sizes closer than this step belong to one heading level. </summary>
        internal const double SizeStep = 0.5;

        /// <summary> A filled rectangle thinner than this draws a ruling line. </summary>
        internal const double RuleThickness = 2.0;

        /// <summary> A ruling line shorter than this is decoration and not a table border. </summary>
        internal const double MinRuleLength = 5.0;

        /// <summary> Two coordinates closer than this are the same position. </summary>
        internal const double Tolerance = 2.0;

        /// <summary> A bullet glyph joins the first word to its right, up to this distance. </summary>
        internal const double MaxBulletGap = 72.0;

        /// <summary> Two bullets closer than this on the horizontal axis share one list level. </summary>
        internal const double ListLevelTolerance = 3.0;

        /// <summary> An image smaller than this area is a spacer, and the reader drops it. </summary>
        internal const double MinImageArea = 4.0;
    }
}
