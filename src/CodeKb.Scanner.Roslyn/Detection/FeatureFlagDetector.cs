using CodeKb.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeKb.Scanner.Roslyn.Detection;

public sealed record FeatureFlagHit(
    string FlagName,
    FeatureFlagUsageType UsageType,
    int Line,
    int Column,
    string? ReceiverTypeName,
    string? MethodName,
    string? DefaultValue);

public interface IFeatureFlagDetector
{
    IReadOnlyList<FeatureFlagHit> Detect(SyntaxTree tree, SemanticModel? semanticModel, ScanOptions options);
}

public sealed class FeatureFlagDetector : IFeatureFlagDetector
{
    public IReadOnlyList<FeatureFlagHit> Detect(SyntaxTree tree, SemanticModel? semanticModel, ScanOptions options)
    {
        var root = tree.GetRoot();
        var hits = new List<FeatureFlagHit>();

        DetectConstantDefinitions(root, hits);
        DetectInvocations(root, semanticModel, options, hits);

        return hits;
    }

    private static void DetectConstantDefinitions(SyntaxNode root, List<FeatureFlagHit> hits)
    {
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (!field.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword))) continue;
            if (field.Declaration.Type is not PredefinedTypeSyntax pt) continue;
            if (!pt.Keyword.IsKind(SyntaxKind.StringKeyword)) continue;

            foreach (var v in field.Declaration.Variables)
            {
                if (v.Initializer?.Value is LiteralExpressionSyntax lit &&
                    lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var value = (string?)lit.Token.Value;
                    if (string.IsNullOrEmpty(value)) continue;
                    var pos = lit.GetLocation().GetLineSpan();
                    hits.Add(new FeatureFlagHit(
                        FlagName: value,
                        UsageType: FeatureFlagUsageType.ConstantDefinition,
                        Line: pos.StartLinePosition.Line + 1,
                        Column: pos.StartLinePosition.Character + 1,
                        ReceiverTypeName: null,
                        MethodName: null,
                        DefaultValue: null));
                }
            }
        }
    }

    private static void DetectInvocations(SyntaxNode root, SemanticModel? semanticModel, ScanOptions options, List<FeatureFlagHit> hits)
    {
        var methodSet = new HashSet<string>(options.FeatureFlagMethodNames, StringComparer.Ordinal);
        var clientSet = new HashSet<string>(options.FeatureFlagClientNames, StringComparer.Ordinal);

        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = ExtractMethodName(inv);
            if (methodName is null || !methodSet.Contains(methodName)) continue;

            var receiverText = ExtractReceiverText(inv);
            var receiverType = ResolveReceiverType(inv, semanticModel, clientSet);

            bool receiverMatches;
            if (semanticModel != null && receiverType != null)
                receiverMatches = TypeMatchesClient(receiverType, clientSet);
            else
                receiverMatches = receiverText != null && (clientSet.Contains(receiverText)
                    || HeuristicReceiverMatch(receiverText, clientSet));

            if (!receiverMatches) continue;

            var firstArg = inv.ArgumentList.Arguments.FirstOrDefault();
            if (firstArg?.Expression is not LiteralExpressionSyntax flagLit) continue;
            if (!flagLit.IsKind(SyntaxKind.StringLiteralExpression)) continue;
            var flagName = (string?)flagLit.Token.Value;
            if (string.IsNullOrEmpty(flagName)) continue;

            string? defaultValue = null;
            if (inv.ArgumentList.Arguments.Count >= 3 && inv.ArgumentList.Arguments[2].Expression is LiteralExpressionSyntax defLit)
                defaultValue = defLit.Token.ValueText;

            var pos = inv.GetLocation().GetLineSpan();
            hits.Add(new FeatureFlagHit(
                FlagName: flagName,
                UsageType: FeatureFlagUsageType.RuntimeBranch,
                Line: pos.StartLinePosition.Line + 1,
                Column: pos.StartLinePosition.Character + 1,
                ReceiverTypeName: receiverType?.Name ?? receiverText,
                MethodName: methodName,
                DefaultValue: defaultValue));
        }
    }

    internal static string? ExtractMethodName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        _ => null,
    };

    internal static string? ExtractReceiverText(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Expression.ToString(),
        _ => null,
    };

    internal static ITypeSymbol? ResolveReceiverType(InvocationExpressionSyntax inv, SemanticModel? sm, HashSet<string> clientSet)
    {
        if (sm == null) return null;
        if (inv.Expression is not MemberAccessExpressionSyntax m) return null;
        var info = sm.GetTypeInfo(m.Expression);
        return info.Type ?? info.ConvertedType;
    }

    internal static bool TypeMatchesClient(ITypeSymbol type, HashSet<string> clientSet)
    {
        if (clientSet.Contains(type.Name)) return true;
        foreach (var iface in type.AllInterfaces)
            if (clientSet.Contains(iface.Name)) return true;
        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (clientSet.Contains(baseType.Name)) return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    internal static bool HeuristicReceiverMatch(string receiver, HashSet<string> clientSet)
    {
        var name = receiver.TrimStart('_');
        if (name.Length == 0) return false;
        var pascal = char.ToUpperInvariant(name[0]) + (name.Length > 1 ? name.Substring(1) : "");
        if (clientSet.Contains("I" + pascal)) return true;
        if (clientSet.Contains(pascal)) return true;
        foreach (var c in clientSet)
        {
            var stripped = c.StartsWith("I") && c.Length > 1 && char.IsUpper(c[1]) ? c.Substring(1) : c;
            if (string.Equals(pascal, stripped, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
