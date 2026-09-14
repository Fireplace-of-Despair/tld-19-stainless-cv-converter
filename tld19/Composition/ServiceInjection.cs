// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
// SPDX-License-Identifier: MPL-2.0
// SPDX-FileCopyrightText: 2026 Shevtsov Stanislav (Fireplace of Despair)

using System;
using Microsoft.Extensions.DependencyInjection;

namespace tld19.Composition;

internal static class ServiceInjection
{
    public static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddMediator();

        return services.BuildServiceProvider();
    }
}
