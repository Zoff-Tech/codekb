using CodeKb.Scanner.Roslyn.Projects;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class ProjectScannerTests
{
    private readonly ProjectScanner _p = new();

    [Fact]
    public void ParseProject_BasicSdkProject()
    {
        var xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>Acme.Workflow</RootNamespace>
    <AssemblyName>Acme.Workflow</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12.0</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.CodeAnalysis.CSharp"" Version=""4.8.0"" />
    <PackageReference Include=""Npgsql"" Version=""8.0.0"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""..\CodeKb.Contracts\CodeKb.Contracts.csproj"" />
  </ItemGroup>
</Project>";
        var info = _p.ParseProject("src/Acme.Workflow/Acme.Workflow.csproj", xml);
        info.Should().NotBeNull();
        info!.Sdk.Should().Be("Microsoft.NET.Sdk");
        info.TargetFrameworks.Should().Contain("net8.0");
        info.RootNamespace.Should().Be("Acme.Workflow");
        info.AssemblyName.Should().Be("Acme.Workflow");
        info.LangVersion.Should().Be("12.0");
        info.NullableEnabled.Should().BeTrue();
        info.ImplicitUsings.Should().BeTrue();
        info.PackageReferences.Should().Contain(p => p.Name == "Microsoft.CodeAnalysis.CSharp" && p.Version == "4.8.0");
        info.PackageReferences.Should().Contain(p => p.Name == "Npgsql" && p.Version == "8.0.0");
        info.ProjectReferences.Should().Contain(r => r.Contains("CodeKb.Contracts"));
    }

    [Fact]
    public void ParseProject_MultipleTargetFrameworks()
    {
        var xml = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net6.0;net8.0;netstandard2.1</TargetFrameworks>
  </PropertyGroup>
</Project>";
        var info = _p.ParseProject("x.csproj", xml);
        info.Should().NotBeNull();
        info!.TargetFrameworks.Should().BeEquivalentTo(new[] { "net6.0", "net8.0", "netstandard2.1" });
    }

    [Fact]
    public void ParseProject_InvalidXml_ReturnsNull()
    {
        _p.ParseProject("x.csproj", "<not valid xml").Should().BeNull();
        _p.ParseProject("x.csproj", "").Should().BeNull();
    }

    [Fact]
    public void ParseProject_NonProjectRoot_ReturnsNull()
    {
        _p.ParseProject("x.csproj", "<Other />").Should().BeNull();
    }

    [Fact]
    public void ParseSolution_ExtractsProjectPaths()
    {
        var sln = @"Microsoft Visual Studio Solution File, Format Version 12.00
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""CodeKb.Core"", ""src\CodeKb.Core\CodeKb.Core.csproj"", ""{11111111-1111-1111-1111-111111111111}""
EndProject
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""CodeKb.Cli"", ""src\CodeKb.Cli\CodeKb.Cli.csproj"", ""{22222222-2222-2222-2222-222222222222}""
EndProject
";
        var info = _p.ParseSolution("codekb.sln", sln);
        info.Should().NotBeNull();
        info!.Projects.Should().Contain("src/CodeKb.Core/CodeKb.Core.csproj");
        info.Projects.Should().Contain("src/CodeKb.Cli/CodeKb.Cli.csproj");
    }
}
