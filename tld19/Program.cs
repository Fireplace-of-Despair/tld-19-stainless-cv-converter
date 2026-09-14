// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using tld19.Composition;
using tld19.Features.Documents;

namespace tld19;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args is ["-h"] or ["--help"])
        {
            PrintUsage();
            return args.Length == 0 ? Globals.ExitCodes.Usage : Globals.ExitCodes.Success;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        var services = ServiceInjection.ConfigureServices();
        var mediator = services.GetRequiredService<IMediator>();
        var exitCode = Globals.ExitCodes.Success;

        foreach (var path in args)
        {
            try
            {
                var result = await mediator.Send(new ConvertDocumentFeature.Command { InputPath = path }, cancellation.Token);
                Console.WriteLine($"{path} -> {result.OutputPath}");
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("The conversion was cancelled.");
                return Globals.ExitCodes.Failure;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{path}: {ex.Message}");
                exitCode = Globals.ExitCodes.Failure;
            }
        }

        return exitCode;
    }

    private static void PrintUsage()
    {
        Console.WriteLine($"{Globals.Brand.Title} {Globals.Brand.Version}");
        Console.WriteLine();
        Console.WriteLine($"Usage: {Globals.Brand.Product} <file> [<file> ...]");
        Console.WriteLine();
        Console.WriteLine($"Converts each {Globals.Formats.Pdf}, {Globals.Formats.Doc} or {Globals.Formats.Docx} file into a {Globals.Markdown.Extension} file");
        Console.WriteLine("with the same name, in the same folder. An existing Markdown file of that name is replaced.");
    }
}
