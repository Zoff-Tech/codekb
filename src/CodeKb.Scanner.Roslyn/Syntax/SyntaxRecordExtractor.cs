using System.Text.Json;
using CodeKb.Contracts;
using CodeKb.Scanner.Roslyn.Snippets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SymbolKind = CodeKb.Contracts.SymbolKind;

namespace CodeKb.Scanner.Roslyn.Syntax;

public sealed record ExtractedSymbol(
    RecordType RecordType,
    string SymbolName,
    SymbolKind SymbolKind,
    string? Namespace,
    string? ClassName,
    string? MethodName,
    int LineStart,
    int LineEnd,
    string Summary,
    string CodeSnippet,
    string MetadataJson);

public sealed record FileExtraction(
    int LineCount,
    IReadOnlyList<string> Namespaces,
    IReadOnlyList<string> TopLevelTypes,
    IReadOnlyList<ExtractedSymbol> Symbols);

public interface ISyntaxRecordExtractor
{
    FileExtraction Extract(string source, string relativePath);
}

public sealed class SyntaxRecordExtractor : ISyntaxRecordExtractor
{
    public FileExtraction Extract(string source, string relativePath)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var lineCount = SnippetBuilder.SplitLines(source).Length;

        var namespaces = new List<string>();
        foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            namespaces.Add(ns.Name.ToString());
        if (namespaces.Count == 0) namespaces.Add(string.Empty);

        var topLevelTypes = new List<string>();
        var symbols = new List<ExtractedSymbol>();

        foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var typeName = typeDecl.Identifier.Text;
            if (IsTopLevel(typeDecl)) topLevelTypes.Add(typeName);

            var nsName = FindEnclosingNamespace(typeDecl);
            var classSummary = BuildClassSummary(typeDecl, nsName, source);
            symbols.Add(classSummary);

            if (typeDecl is TypeDeclarationSyntax t)
            {
                foreach (var method in t.Members.OfType<MethodDeclarationSyntax>())
                    symbols.Add(BuildMethodSummary(method, typeName, nsName, source));
                foreach (var prop in t.Members.OfType<PropertyDeclarationSyntax>())
                {
                    var ms = BuildPropertySummary(prop, typeName, nsName, source);
                    if (ms != null) symbols.Add(ms);
                }
            }
        }

        return new FileExtraction(lineCount, namespaces.Distinct().ToList(), topLevelTypes, symbols);
    }

    internal static bool IsTopLevel(BaseTypeDeclarationSyntax decl)
        => decl.Parent is NamespaceDeclarationSyntax
        || decl.Parent is FileScopedNamespaceDeclarationSyntax
        || decl.Parent is CompilationUnitSyntax;

    internal static string? FindEnclosingNamespace(SyntaxNode node)
    {
        var cur = node.Parent;
        while (cur != null)
        {
            if (cur is BaseNamespaceDeclarationSyntax ns) return ns.Name.ToString();
            cur = cur.Parent;
        }
        return null;
    }

    internal static ExtractedSymbol BuildClassSummary(BaseTypeDeclarationSyntax decl, string? ns, string source)
    {
        var span = decl.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;
        var typeName = decl.Identifier.Text;

        var symbolKind = decl switch
        {
            ClassDeclarationSyntax => SymbolKind.Class,
            InterfaceDeclarationSyntax => SymbolKind.Interface,
            RecordDeclarationSyntax => SymbolKind.Record,
            EnumDeclarationSyntax => SymbolKind.Enum,
            StructDeclarationSyntax => SymbolKind.Struct,
            _ => SymbolKind.Class,
        };

        var baseTypes = new List<string>();
        var implements = new List<string>();
        if (decl.BaseList != null)
        {
            foreach (var bt in decl.BaseList.Types)
                baseTypes.Add(bt.Type.ToString());
            implements.AddRange(baseTypes);
        }

        var attributes = decl.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList();
        var modifiers = decl.Modifiers.Select(m => m.Text).ToHashSet();

        var memberSignatures = new List<string>();
        if (decl is TypeDeclarationSyntax t)
        {
            foreach (var m in t.Members)
            {
                memberSignatures.Add(BuildMemberSignature(m));
                if (memberSignatures.Count >= 50) break;
            }
        }

        var header = decl.Modifiers.ToString() + " " + symbolKind.ToString().ToLowerInvariant() + " " + typeName;
        if (decl.BaseList != null) header += " " + decl.BaseList.ToString();

        var snippet = SnippetBuilder.BuildClassSignatures(header.Trim(), memberSignatures);

        var metadata = JsonSerializer.Serialize(new
        {
            base_types = baseTypes,
            implements,
            attributes,
            is_abstract = modifiers.Contains("abstract"),
            is_sealed = modifiers.Contains("sealed"),
            is_partial = modifiers.Contains("partial"),
        });

        return new ExtractedSymbol(
            RecordType: RecordType.ClassSummary,
            SymbolName: ns is null ? typeName : $"{ns}.{typeName}",
            SymbolKind: symbolKind,
            Namespace: ns,
            ClassName: typeName,
            MethodName: null,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"{symbolKind} {typeName}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static string BuildMemberSignature(MemberDeclarationSyntax m) => m switch
    {
        MethodDeclarationSyntax mtd => $"{mtd.Modifiers} {mtd.ReturnType} {mtd.Identifier}{mtd.ParameterList};".Trim(),
        PropertyDeclarationSyntax p => $"{p.Modifiers} {p.Type} {p.Identifier} {{ get; set; }}".Trim(),
        FieldDeclarationSyntax f => $"{f.Modifiers} {f.Declaration};".Trim(),
        ConstructorDeclarationSyntax c => $"{c.Modifiers} {c.Identifier}{c.ParameterList};".Trim(),
        _ => m.ToString().Split('\n')[0].Trim(),
    };

    internal static ExtractedSymbol BuildMethodSummary(MethodDeclarationSyntax method, string typeName, string? ns, string source)
    {
        var span = method.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;
        var methodName = method.Identifier.Text;

        var attributes = method.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList();
        var modifiers = method.Modifiers.Select(m => m.Text).ToHashSet();

        var parameters = method.ParameterList.Parameters.Select(p => new
        {
            name = p.Identifier.Text,
            type = p.Type?.ToString() ?? "object",
        }).ToList();

        var signature = $"{method.Modifiers} {method.ReturnType} {methodName}{method.ParameterList}".Trim();
        var complexity = ComputeCyclomatic(method);

        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);

        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = method.ReturnType.ToString(),
            parameters,
            attributes,
            is_async = modifiers.Contains("async"),
            is_static = modifiers.Contains("static"),
            cyclomatic_complexity = complexity,
        });

        var symbol = ns is null ? $"{typeName}.{methodName}" : $"{ns}.{typeName}.{methodName}";
        return new ExtractedSymbol(
            RecordType: RecordType.MethodSummary,
            SymbolName: symbol,
            SymbolKind: SymbolKind.Method,
            Namespace: ns,
            ClassName: typeName,
            MethodName: methodName,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"method {typeName}.{methodName}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol? BuildPropertySummary(PropertyDeclarationSyntax prop, string typeName, string? ns, string source)
    {
        var span = prop.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;
        var name = prop.Identifier.Text;

        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);
        var metadata = JsonSerializer.Serialize(new
        {
            signature = $"{prop.Modifiers} {prop.Type} {name}".Trim(),
            returns = prop.Type.ToString(),
            parameters = Array.Empty<object>(),
            attributes = prop.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = false,
            is_static = prop.Modifiers.Any(m => m.Text == "static"),
            cyclomatic_complexity = 1,
        });

        var symbol = ns is null ? $"{typeName}.{name}" : $"{ns}.{typeName}.{name}";
        return new ExtractedSymbol(
            RecordType: RecordType.MethodSummary,
            SymbolName: symbol,
            SymbolKind: SymbolKind.Property,
            Namespace: ns,
            ClassName: typeName,
            MethodName: name,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"property {typeName}.{name}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static int ComputeCyclomatic(MethodDeclarationSyntax method)
    {
        var body = method.Body as SyntaxNode ?? method.ExpressionBody;
        if (body is null) return 1;
        int complexity = 1;
        foreach (var node in body.DescendantNodes())
        {
            switch (node.Kind())
            {
                case SyntaxKind.IfStatement:
                case SyntaxKind.CaseSwitchLabel:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForEachStatement:
                case SyntaxKind.ConditionalExpression:
                case SyntaxKind.CatchClause:
                case SyntaxKind.LogicalAndExpression:
                case SyntaxKind.LogicalOrExpression:
                    complexity++;
                    break;
            }
        }
        return complexity;
    }
}
