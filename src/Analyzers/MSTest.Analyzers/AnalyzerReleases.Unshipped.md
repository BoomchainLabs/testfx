; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MSTEST0044 | Design | Warning | PreferTestMethodOverDataTestMethodAnalyzer, [Documentation](https://learn.microsoft.com/dotnet/core/testing/mstest-analyzers/mstest0044)
MSTEST0045 | Usage | Info | UseCooperativeCancellationForTimeoutAnalyzer, [Documentation](https://learn.microsoft.com/dotnet/core/testing/mstest-analyzers/mstest0045)
MSTEST0046 | Usage | Info | StringAssertToAssertAnalyzer, [Documentation](https://learn.microsoft.com/dotnet/core/testing/mstest-analyzers/mstest0046)
MSTEST0048 | Usage | Warning | TestContextPropertyUsageAnalyzer, [Documentation](https://learn.microsoft.com/dotnet/core/testing/mstest-analyzers/mstest0047)
MSTEST0049 | Usage | Info | FlowTestContextCancellationTokenAnalyzer, [Documentation](https://learn.microsoft.com/dotnet/core/testing/mstest-analyzers/mstest0049)

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
MSTEST0006 | Design | Warning | Design | Info | AvoidExpectedExceptionAttributeAnalyzer
MSTEST0039 | Usage | Warning | Usage | Info | UseNewerAssertThrowsAnalyzer
