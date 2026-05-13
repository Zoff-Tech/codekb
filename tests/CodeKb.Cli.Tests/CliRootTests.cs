using CodeKb.Cli;
using FluentAssertions;
using Xunit;

namespace CodeKb.Cli.Tests;

public class CliRootTests
{
    [Fact]
    public void BuildRootCommand_DeclaresScanAndAsk()
    {
        var root = CliRoot.BuildRootCommand();
        root.Subcommands.Should().Contain(c => c.Name == "scan");
        root.Subcommands.Should().Contain(c => c.Name == "ask");
    }

    [Fact]
    public void ScanCommand_HasExpectedOptions()
    {
        var root = CliRoot.BuildRootCommand();
        var scan = root.Subcommands.Single(c => c.Name == "scan");
        scan.Options.Select(o => o.Name).Should().Contain(new[] { "repo", "path", "branch", "search", "force" });
    }

    [Fact]
    public void AskCommand_HasExpectedOptions()
    {
        var root = CliRoot.BuildRootCommand();
        var ask = root.Subcommands.Single(c => c.Name == "ask");
        ask.Options.Select(o => o.Name).Should().Contain(new[] { "repo", "branch", "record-type", "feature-flag", "top-k" });
    }

    [Fact]
    public void ExitCodes_AreNotZero()
    {
        CliRoot.ExitOk.Should().Be(0);
        CliRoot.ExitUserError.Should().Be(2);
        CliRoot.ExitInfraError.Should().Be(3);
    }
}
