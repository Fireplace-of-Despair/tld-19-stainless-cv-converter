// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using System.IO;

namespace tld19Tests;

/// <summary> A temporary folder for one test. The folder and its content go away on dispose. </summary>
// LLM - Claude Opus 5
internal sealed class TestFolder : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("tld19Tests-");

    public string Path => _folder.FullName;

    public string File(string name) => System.IO.Path.Combine(_folder.FullName, name);

    /// <summary> Copies a file from the Fixtures folder into this folder. </summary>
    public string CopyFixture(string name)
    {
        var target = File(name);
        System.IO.File.Copy(Fixture(name), target);
        return target;
    }

    public static string Fixture(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public void Dispose()
    {
        try
        {
            _folder.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A folder that another process still holds stays in the temporary folder of the system.
        }
    }
}
