using CodeKb.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace CodeKb.Core.Tests;

public class ConfigLoaderTests : IDisposable
{
    private readonly string _dir;

    public ConfigLoaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "codekb-cfg-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string WriteYaml(string content)
    {
        var p = Path.Combine(_dir, "codekb.yaml");
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Load_NoConfigFile_ReturnsDefaultsAndValidates()
    {
        var opts = ConfigLoader.Load(Path.Combine(_dir, "missing.yaml"), new Dictionary<string, string?>());
        opts.Embedding.Provider.Should().Be("openai");
        opts.Embedding.Dimension.Should().Be(1536);
    }

    [Fact]
    public void Load_ValidYaml_BindsValues()
    {
        var path = WriteYaml(@"
storage:
  postgresConnectionString: ""Host=db""
embedding:
  provider: azure
  model: text-3
  dimension: 2048
scanner:
  parallelism: 8
  maxFileSizeKb: 1024
");
        var opts = ConfigLoader.Load(path, new Dictionary<string, string?>());
        opts.Storage.PostgresConnectionString.Should().Be("Host=db");
        opts.Embedding.Provider.Should().Be("azure");
        opts.Embedding.Dimension.Should().Be(2048);
        opts.Scanner.Parallelism.Should().Be(8);
    }

    [Fact]
    public void Load_BadYaml_Throws()
    {
        var path = WriteYaml("not: valid: yaml:\n  -");
        Assert.Throws<ConfigLoadException>(() => ConfigLoader.Load(path, new Dictionary<string, string?>()));
    }

    [Fact]
    public void Load_YamlContainsGitToken_Throws()
    {
        var path = WriteYaml(@"
git:
  token: should-not-be-here
embedding:
  provider: openai
");
        Assert.Throws<ConfigLoadException>(() => ConfigLoader.Load(path, new Dictionary<string, string?>()));
    }

    [Fact]
    public void Load_EnvVar_OverridesYaml()
    {
        var path = WriteYaml(@"embedding:
  provider: openai
  model: text-3
");
        var env = new Dictionary<string, string?>
        {
            ["EMBEDDING_API_KEY"] = "sk-xxx",
            ["EMBEDDING_MODEL"] = "text-overridden",
            ["EMBEDDING_MODEL_DIMENSION"] = "768",
            ["CODEKB__STORAGE__POSTGRESCONNECTIONSTRING"] = "Host=override",
        };
        var opts = ConfigLoader.Load(path, env);
        opts.Embedding.ApiKey.Should().Be("sk-xxx");
        opts.Embedding.Model.Should().Be("text-overridden");
        opts.Embedding.Dimension.Should().Be(768);
        opts.Storage.PostgresConnectionString.Should().Be("Host=override");
    }

    [Fact]
    public void Validate_NegativeDimension_Throws()
    {
        var opts = new CodeKbOptions();
        opts.Embedding.Dimension = 0;
        Assert.Throws<ConfigLoadException>(() => ConfigLoader.Validate(opts));
    }

    [Fact]
    public void Validate_EmptyProvider_Throws()
    {
        var opts = new CodeKbOptions();
        opts.Embedding.Provider = "";
        Assert.Throws<ConfigLoadException>(() => ConfigLoader.Validate(opts));
    }

    [Fact]
    public void Validate_NegativeRetries_Throws()
    {
        var opts = new CodeKbOptions();
        opts.Embedding.MaxRetries = -1;
        Assert.Throws<ConfigLoadException>(() => ConfigLoader.Validate(opts));
    }

    [Fact]
    public void Validate_ZeroParallelism_Throws()
    {
        var opts = new CodeKbOptions();
        opts.Scanner.Parallelism = 0;
        Assert.Throws<ConfigLoadException>(() => ConfigLoader.Validate(opts));
    }

    [Fact]
    public void Validate_ZeroMaxFileSize_Throws()
    {
        var opts = new CodeKbOptions();
        opts.Scanner.MaxFileSizeKb = 0;
        Assert.Throws<ConfigLoadException>(() => ConfigLoader.Validate(opts));
    }

    [Fact]
    public void Validate_EmptyModel_Throws()
    {
        var opts = new CodeKbOptions();
        opts.Embedding.Model = "";
        Assert.Throws<ConfigLoadException>(() => ConfigLoader.Validate(opts));
    }

    [Fact]
    public void Load_CommentedCredentialKey_Ignored()
    {
        var path = WriteYaml("# gitToken: comment\nembedding:\n  provider: openai");
        var opts = ConfigLoader.Load(path, new Dictionary<string, string?>());
        opts.Should().NotBeNull();
    }
}
