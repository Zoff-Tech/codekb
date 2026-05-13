using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeKb.Core.Configuration;

public sealed class ConfigLoadException : Exception
{
    public ConfigLoadException(string message) : base(message) { }
    public ConfigLoadException(string message, Exception inner) : base(message, inner) { }
}

public static class ConfigLoader
{
    public const string DefaultConfigPath = "./config/codekb.yaml";

    private static readonly string[] ForbiddenGitCredentialKeys = new[]
    {
        "gitToken", "gitUsername", "git.token", "git.password", "git.username",
        "embeddingApiKey", "embedding.apiKey",
    };

    public static CodeKbOptions Load(string? configPath, IDictionary<string, string?>? env = null)
    {
        env ??= ReadEnv();

        var options = new CodeKbOptions();
        var path = configPath ?? DefaultConfigPath;

        if (File.Exists(path))
        {
            var raw = File.ReadAllText(path);
            EnsureNoCredentialsInYaml(raw, path);
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                options = deserializer.Deserialize<CodeKbOptions>(raw) ?? new CodeKbOptions();
            }
            catch (Exception ex)
            {
                throw new ConfigLoadException($"Failed to parse YAML config at {path}: {ex.Message}", ex);
            }
        }

        ApplyEnvOverrides(options, env);
        Validate(options);
        return options;
    }

    private static void EnsureNoCredentialsInYaml(string raw, string path)
    {
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("#")) continue;
            foreach (var key in ForbiddenGitCredentialKeys)
            {
                var token = key.Contains('.') ? key.Split('.')[^1] : key;
                if (trimmed.StartsWith(token, StringComparison.OrdinalIgnoreCase) &&
                    trimmed.Contains(':'))
                {
                    throw new ConfigLoadException(
                        $"Forbidden credential key '{token}' found in YAML config at {path}. " +
                        "Credentials must come from environment variables or ssh-agent.");
                }
            }
        }
    }

    private static void ApplyEnvOverrides(CodeKbOptions opts, IDictionary<string, string?> env)
    {
        TryGet(env, "EMBEDDING_API_KEY", v => opts.Embedding.ApiKey = v);
        TryGet(env, "EMBEDDING_ENDPOINT", v => opts.Embedding.Endpoint = v);
        TryGet(env, "EMBEDDING_MODEL", v => opts.Embedding.Model = v);
        TryGet(env, "EMBEDDING_PROVIDER", v => opts.Embedding.Provider = v);
        TryGet(env, "EMBEDDING_MODEL_DIMENSION", v =>
        {
            if (int.TryParse(v, out var d)) opts.Embedding.Dimension = d;
        });

        TryGet(env, "CODEKB__STORAGE__POSTGRESCONNECTIONSTRING",
            v => opts.Storage.PostgresConnectionString = v);
        TryGet(env, "CODEKB__EMBEDDING__APIKEY", v => opts.Embedding.ApiKey = v);
        TryGet(env, "CODEKB__EMBEDDING__ENDPOINT", v => opts.Embedding.Endpoint = v);
        TryGet(env, "CODEKB__EMBEDDING__MODEL", v => opts.Embedding.Model = v);
        TryGet(env, "CODEKB__EMBEDDING__PROVIDER", v => opts.Embedding.Provider = v);
        TryGet(env, "CODEKB__EMBEDDING__DIMENSION", v =>
        {
            if (int.TryParse(v, out var d)) opts.Embedding.Dimension = d;
        });
    }

    private static void TryGet(IDictionary<string, string?> env, string key, Action<string> apply)
    {
        if (env.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            apply(value);
    }

    public static void Validate(CodeKbOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.Embedding.Provider))
            throw new ConfigLoadException("embedding.provider must be set");
        if (string.IsNullOrWhiteSpace(opts.Embedding.Model))
            throw new ConfigLoadException("embedding.model must be set");
        if (opts.Embedding.Dimension <= 0)
            throw new ConfigLoadException("embedding.dimension must be > 0");
        if (opts.Embedding.BatchSize <= 0)
            throw new ConfigLoadException("embedding.batchSize must be > 0");
        if (opts.Embedding.MaxRetries < 0)
            throw new ConfigLoadException("embedding.maxRetries must be >= 0");
        if (opts.Scanner.Parallelism <= 0)
            throw new ConfigLoadException("scanner.parallelism must be > 0");
        if (opts.Scanner.MaxFileSizeKb <= 0)
            throw new ConfigLoadException("scanner.maxFileSizeKB must be > 0");
    }

    private static IDictionary<string, string?> ReadEnv()
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            dict[(string)kv.Key] = kv.Value?.ToString();
        }
        return dict;
    }
}
