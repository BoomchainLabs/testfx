﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Testing.Platform.OutputDevice.Terminal;

internal sealed class TerminalTestReporterOptions
{
    /// <summary>
    /// Gets a value indicating whether we should show passed tests.
    /// </summary>
    public Func<bool> ShowPassedTests { get; init; } = () => true;

    /// <summary>
    /// Gets minimum amount of tests to run.
    /// </summary>
    public int MinimumExpectedTests { get; init; }

    /// <summary>
    /// Gets a value indicating whether we should write the progress periodically to screen. When ANSI is allowed we update the progress as often as we can. When ANSI is not allowed we update it every 3 seconds.
    /// This is a callback to nullable bool, because we don't know if we are running as test host controller until after we setup the console. So we should be polling for the value, until we get non-null boolean
    /// and then cache that value.
    /// </summary>
    public Func<bool?> ShowProgress { get; init; } = () => true;

    /// <summary>
    /// Gets a value indicating whether the active tests should be visible when the progress is shown.
    /// </summary>
    public bool ShowActiveTests { get; init; }

    /// <summary>
    /// Gets a value indicating whether we should use ANSI escape codes or disable them. When true the capabilities of the console are autodetected.
    /// </summary>
    public bool UseAnsi { get; init; }

    /// <summary>
    /// Gets a value indicating whether we are running in compatible CI, and should use simplified ANSI renderer, which colors output, but does not move cursor.
    /// Setting <see cref="UseAnsi"/> to false will disable this option.
    /// </summary>
    public bool UseCIAnsi { get; init; }

    /// <summary>
    /// Gets a value indicating whether we should force ANSI escape codes. When true the ANSI is used without auto-detecting capabilities of the console. This is needed only for testing.
    /// </summary>
    internal /* for testing */ bool? ForceAnsi { get; init; }
}
