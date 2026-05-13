using System.CommandLine;
using System.Diagnostics.CodeAnalysis;

namespace CodeKb.Cli;

[ExcludeFromCodeCoverage(Justification = "Process entry point.")]
internal static class CliEntryPoint
{
    public static Task<int> RunAsync(string[] args) => CliRoot.BuildRootCommand().InvokeAsync(args);
}
