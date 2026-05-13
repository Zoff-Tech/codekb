using System.CommandLine;
using CodeKb.Cli;
using FluentAssertions;
using Xunit;

namespace CodeKb.Cli.Tests;

public class CliInvocationTests
{
    [Fact]
    public async Task Scan_NoArgs_ReturnsUserError()
    {
        var root = CliRoot.BuildRootCommand();
        var rc = await root.InvokeAsync(new[] { "scan", "--config", "/nonexistent.yaml" });
        // user error path -> non-zero
        rc.Should().NotBe(0);
    }

    [Fact]
    public async Task Scan_BothRepoAndPath_ReturnsUserError()
    {
        var root = CliRoot.BuildRootCommand();
        var rc = await root.InvokeAsync(new[] { "scan", "--repo", "x", "--path", "y" });
        rc.Should().Be(CliRoot.ExitUserError);
    }

    [Fact]
    public async Task Ask_EmptyQuestion_NonZero()
    {
        var root = CliRoot.BuildRootCommand();
        var rc = await root.InvokeAsync(new[] { "ask", "" });
        rc.Should().NotBe(0);
    }

    [Fact]
    public async Task Ask_InvalidRecordType_NonZero()
    {
        var root = CliRoot.BuildRootCommand();
        var rc = await root.InvokeAsync(new[] { "ask", "q", "--record-type", "not-a-real-type" });
        rc.Should().Be(CliRoot.ExitUserError);
    }

    [Fact]
    public async Task Ask_ValidRecordTypes_ParsedOk()
    {
        // This will fail downstream (no DB) but should parse cleanly and exit infra error, not user error.
        var root = CliRoot.BuildRootCommand();
        var rc = await root.InvokeAsync(new[] { "ask", "q", "--record-type", "method_summary", "--record-type", "feature_flag_usage" });
        rc.Should().Be(CliRoot.ExitInfraError);
    }

    [Fact]
    public async Task Help_Succeeds()
    {
        var root = CliRoot.BuildRootCommand();
        var rc = await root.InvokeAsync(new[] { "--help" });
        rc.Should().Be(0);
    }
}
