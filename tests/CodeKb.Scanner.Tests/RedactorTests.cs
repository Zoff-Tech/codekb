using CodeKb.Scanner.Roslyn.Redaction;
using FluentAssertions;
using Xunit;

namespace CodeKb.Scanner.Tests;

public class RedactorTests
{
    private readonly Redactor _r = new();

    [Fact]
    public void NoMatch_Returns_NoMatch()
    {
        var result = _r.Redact("var x = 42;");
        result.Status.Should().Be(RedactionStatus.NoMatch);
        result.Text.Should().Be("var x = 42;");
        result.PatternsHit.Should().BeEmpty();
    }

    [Fact]
    public void Null_OrEmpty_Returns_NoMatch()
    {
        _r.Redact("").Status.Should().Be(RedactionStatus.NoMatch);
        _r.Redact(null!).Status.Should().Be(RedactionStatus.NoMatch);
    }

    [Theory]
    [InlineData("password=hunter2", "password")]
    [InlineData("Secret = \"abc123\"", "secret")]
    [InlineData("api_key: \"deadbeef\"", "api_key")]
    [InlineData("ConnectionString=Host=db;Pwd=p", "connection_string")]
    [InlineData("client_secret = 'mysecret'", "client_secret")]
    public void KeyValuePatterns_AreRedacted(string snippet, string keyName)
    {
        var result = _r.Redact(snippet);
        result.Status.Should().Be(RedactionStatus.Redacted);
        result.Text.Should().Contain(Redactor.Token);
        result.PatternsHit.Should().Contain(p => p.Contains(keyName));
    }

    [Fact]
    public void AwsAccessKey_IsRedacted()
    {
        var result = _r.Redact("var k = \"AKIAABCDEFGHIJKLMNOP\";");
        result.Status.Should().Be(RedactionStatus.Redacted);
        result.Text.Should().Contain(Redactor.Token);
        result.PatternsHit.Should().Contain("aws_access_key");
    }

    [Fact]
    public void GitHubPat_IsRedacted()
    {
        var pat = "ghp_" + new string('a', 40);
        var result = _r.Redact($"var t = \"{pat}\";");
        result.Status.Should().Be(RedactionStatus.Redacted);
        result.Text.Should().Contain(Redactor.Token);
    }

    [Fact]
    public void Jwt_IsRedacted()
    {
        var jwt = "eyJhbGciOi.eyJzdWIiOi.SflKxw";
        var result = _r.Redact($"var t = \"{jwt}\";");
        result.Status.Should().Be(RedactionStatus.Redacted);
        result.Text.Should().Contain(Redactor.Token);
    }

    [Fact]
    public void PemBlock_IsRedacted()
    {
        var pem = "-----BEGIN RSA PRIVATE KEY-----\nMIIxxx\n-----END RSA PRIVATE KEY-----";
        var result = _r.Redact(pem);
        result.Status.Should().Be(RedactionStatus.Redacted);
        result.Text.Should().Contain(Redactor.Token);
        result.Text.Should().NotContain("MIIxxx");
    }

    [Fact]
    public void InterpolatedSecret_FailsRedaction_AndKeepsSnippet()
    {
        var snippet = "var msg = $\"the password is {password}\";";
        var result = _r.Redact(snippet);
        result.Status.Should().Be(RedactionStatus.Failed);
        result.PatternsHit.Should().Contain("interpolated_secret");
    }

    [Fact]
    public void MultiplePatterns_AllRedacted()
    {
        var snippet = "password=foo\ntoken=bar";
        var result = _r.Redact(snippet);
        result.Text.Split(Redactor.Token).Length.Should().BeGreaterThan(2);
    }
}
