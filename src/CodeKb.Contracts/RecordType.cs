namespace CodeKb.Contracts;

public enum RecordType
{
    FileSummary,
    ClassSummary,
    MethodSummary,
    FeatureFlagUsage,
    SearchTermMatch,
    TestReference,
    ConfigurationReference,
}

public static class RecordTypes
{
    public static string ToWire(this RecordType type) => type switch
    {
        RecordType.FileSummary => "file_summary",
        RecordType.ClassSummary => "class_summary",
        RecordType.MethodSummary => "method_summary",
        RecordType.FeatureFlagUsage => "feature_flag_usage",
        RecordType.SearchTermMatch => "search_term_match",
        RecordType.TestReference => "test_reference",
        RecordType.ConfigurationReference => "configuration_reference",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown record type"),
    };

    public static RecordType FromWire(string wire) => wire switch
    {
        "file_summary" => RecordType.FileSummary,
        "class_summary" => RecordType.ClassSummary,
        "method_summary" => RecordType.MethodSummary,
        "feature_flag_usage" => RecordType.FeatureFlagUsage,
        "search_term_match" => RecordType.SearchTermMatch,
        "test_reference" => RecordType.TestReference,
        "configuration_reference" => RecordType.ConfigurationReference,
        _ => throw new ArgumentException($"Unknown record type wire value: {wire}", nameof(wire)),
    };
}

public enum SymbolKind
{
    None,
    Class,
    Interface,
    Record,
    Enum,
    Struct,
    Method,
    Property,
    Field,
    File,
}

public enum FileKind
{
    Production,
    Test,
    Generated,
    Configuration,
}

public enum FeatureFlagUsageType
{
    RuntimeBranch,
    ConstantDefinition,
    Injection,
    Config,
}

public static class FeatureFlagUsageTypes
{
    public static string ToWire(this FeatureFlagUsageType t) => t switch
    {
        FeatureFlagUsageType.RuntimeBranch => "runtime_branch",
        FeatureFlagUsageType.ConstantDefinition => "constant_definition",
        FeatureFlagUsageType.Injection => "injection",
        FeatureFlagUsageType.Config => "config",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Unknown usage type"),
    };
}

public enum SearchMatchKind
{
    Identifier,
    StringLiteral,
    Comment,
    XmlDoc,
}

public static class SearchMatchKinds
{
    public static string ToWire(this SearchMatchKind k) => k switch
    {
        SearchMatchKind.Identifier => "identifier",
        SearchMatchKind.StringLiteral => "string_literal",
        SearchMatchKind.Comment => "comment",
        SearchMatchKind.XmlDoc => "xml_doc",
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, "Unknown match kind"),
    };
}

public enum EmbeddingStatus
{
    Pending,
    Embedded,
    Failed,
}

public enum ScanStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}
