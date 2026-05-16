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
    IReadOnlyList<ExtractedSymbol> Symbols,
    IReadOnlyList<string> UsingDirectives,
    IReadOnlyList<string> ExternalTypes);

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
                foreach (var ctor in t.Members.OfType<ConstructorDeclarationSyntax>())
                    symbols.Add(BuildConstructorSummary(ctor, typeName, nsName, source));
                foreach (var idx in t.Members.OfType<IndexerDeclarationSyntax>())
                    symbols.Add(BuildIndexerSummary(idx, typeName, nsName, source));
                foreach (var op in t.Members.OfType<OperatorDeclarationSyntax>())
                    symbols.Add(BuildOperatorSummary(op, typeName, nsName, source));
                foreach (var conv in t.Members.OfType<ConversionOperatorDeclarationSyntax>())
                    symbols.Add(BuildConversionSummary(conv, typeName, nsName, source));
                foreach (var dtor in t.Members.OfType<DestructorDeclarationSyntax>())
                    symbols.Add(BuildDestructorSummary(dtor, typeName, nsName, source));
                foreach (var field in t.Members.OfType<FieldDeclarationSyntax>())
                    foreach (var variable in field.Declaration.Variables)
                        symbols.Add(BuildFieldSummary(field, variable, typeName, nsName, source));
                foreach (var ev in t.Members.OfType<EventDeclarationSyntax>())
                    symbols.Add(BuildEventSummary(ev, typeName, nsName, source));
                foreach (var evf in t.Members.OfType<EventFieldDeclarationSyntax>())
                    foreach (var variable in evf.Declaration.Variables)
                        symbols.Add(BuildEventFieldSummary(evf, variable, typeName, nsName, source));
            }

            if (typeDecl is EnumDeclarationSyntax enumDecl)
            {
                foreach (var member in enumDecl.Members)
                    symbols.Add(BuildEnumMemberSummary(member, typeName, nsName, source));
            }
        }

        foreach (var del in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
        {
            var nsName = FindEnclosingNamespace(del);
            symbols.Add(BuildDelegateSummary(del, nsName, source));
            if (del.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax)
                topLevelTypes.Add(del.Identifier.Text);
        }

        foreach (var local in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            var enclosingMethod = local.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var enclosingType = local.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
            if (enclosingType is null) continue;
            var nsName = FindEnclosingNamespace(enclosingType);
            symbols.Add(BuildLocalFunctionSummary(local, enclosingType.Identifier.Text, enclosingMethod?.Identifier.Text, nsName, source));
        }

        var usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Select(u => u.Name?.ToString() ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();

        var externalTypes = CollectExternalTypeReferences(root);

        return new FileExtraction(lineCount, namespaces.Distinct().ToList(), topLevelTypes, symbols, usingDirectives, externalTypes);
    }

    internal static IReadOnlyList<string> CollectExternalTypeReferences(SyntaxNode root)
    {
        var refs = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (seen.Add(name!)) refs.Add(name!);
        }

        foreach (var bt in root.DescendantNodes().OfType<BaseListSyntax>())
            foreach (var t in bt.Types) Add(t.Type.ToString());
        foreach (var p in root.DescendantNodes().OfType<ParameterSyntax>())
            Add(p.Type?.ToString());
        foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            Add(m.ReturnType.ToString());
        foreach (var prop in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            Add(prop.Type.ToString());
        foreach (var f in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
            Add(f.Declaration.Type.ToString());
        foreach (var oc in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            Add(oc.Type.ToString());
        if (refs.Count >= 200) return refs.GetRange(0, 200);
        return refs;
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
        var (calls, instantiates) = CollectCallsAndInstantiations(method);

        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = method.ReturnType.ToString(),
            parameters,
            attributes,
            is_async = modifiers.Contains("async"),
            is_static = modifiers.Contains("static"),
            cyclomatic_complexity = complexity,
            calls,
            instantiates,
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

    internal static ExtractedSymbol BuildConstructorSummary(ConstructorDeclarationSyntax ctor, string typeName, string? ns, string source)
    {
        var span = ctor.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;

        var parameters = ctor.ParameterList.Parameters.Select(p => new
        {
            name = p.Identifier.Text,
            type = p.Type?.ToString() ?? "object",
        }).ToList();

        var signature = $"{ctor.Modifiers} {typeName}{ctor.ParameterList}".Trim();
        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);
        var modifiers = ctor.Modifiers.Select(m => m.Text).ToHashSet();
        var (calls, instantiates) = CollectCallsAndInstantiations(ctor);

        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = typeName,
            parameters,
            attributes = ctor.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = false,
            is_static = modifiers.Contains("static"),
            cyclomatic_complexity = 1,
            kind = "constructor",
            calls,
            instantiates,
        });

        var symbol = ns is null ? $"{typeName}.ctor" : $"{ns}.{typeName}.ctor";
        return new ExtractedSymbol(
            RecordType: RecordType.MethodSummary,
            SymbolName: symbol,
            SymbolKind: SymbolKind.Method,
            Namespace: ns,
            ClassName: typeName,
            MethodName: ".ctor",
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"constructor {typeName}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol BuildIndexerSummary(IndexerDeclarationSyntax idx, string typeName, string? ns, string source)
    {
        var span = idx.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;

        var parameters = idx.ParameterList.Parameters.Select(p => new
        {
            name = p.Identifier.Text,
            type = p.Type?.ToString() ?? "object",
        }).ToList();

        var signature = $"{idx.Modifiers} {idx.Type} this{idx.ParameterList}".Trim();
        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);
        var modifiers = idx.Modifiers.Select(m => m.Text).ToHashSet();

        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = idx.Type.ToString(),
            parameters,
            attributes = idx.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = false,
            is_static = modifiers.Contains("static"),
            cyclomatic_complexity = 1,
            kind = "indexer",
        });

        var symbol = ns is null ? $"{typeName}.this[]" : $"{ns}.{typeName}.this[]";
        return new ExtractedSymbol(
            RecordType: RecordType.MethodSummary,
            SymbolName: symbol,
            SymbolKind: SymbolKind.Property,
            Namespace: ns,
            ClassName: typeName,
            MethodName: "this[]",
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"indexer {typeName}.this[]",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol BuildOperatorSummary(OperatorDeclarationSyntax op, string typeName, string? ns, string source)
    {
        var span = op.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;

        var token = op.OperatorToken.Text;
        var name = $"operator {token}";
        var parameters = op.ParameterList.Parameters.Select(p => new
        {
            name = p.Identifier.Text,
            type = p.Type?.ToString() ?? "object",
        }).ToList();

        var signature = $"{op.Modifiers} {op.ReturnType} operator {token}{op.ParameterList}".Trim();
        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);

        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = op.ReturnType.ToString(),
            parameters,
            attributes = op.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = false,
            is_static = true,
            cyclomatic_complexity = 1,
            kind = "operator",
        });

        var symbol = ns is null ? $"{typeName}.{name}" : $"{ns}.{typeName}.{name}";
        return new ExtractedSymbol(
            RecordType: RecordType.MethodSummary,
            SymbolName: symbol,
            SymbolKind: SymbolKind.Method,
            Namespace: ns,
            ClassName: typeName,
            MethodName: name,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"{name} on {typeName}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol BuildConversionSummary(ConversionOperatorDeclarationSyntax conv, string typeName, string? ns, string source)
    {
        var span = conv.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;

        var direction = conv.ImplicitOrExplicitKeyword.Text;
        var name = $"{direction} operator {conv.Type}";
        var signature = $"{conv.Modifiers} {direction} operator {conv.Type}{conv.ParameterList}".Trim();
        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);

        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = conv.Type.ToString(),
            parameters = conv.ParameterList.Parameters.Select(p => new { name = p.Identifier.Text, type = p.Type?.ToString() ?? "object" }).ToList(),
            attributes = conv.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = false,
            is_static = true,
            cyclomatic_complexity = 1,
            kind = "conversion",
        });

        var symbol = ns is null ? $"{typeName}.{name}" : $"{ns}.{typeName}.{name}";
        return new ExtractedSymbol(
            RecordType: RecordType.MethodSummary,
            SymbolName: symbol,
            SymbolKind: SymbolKind.Method,
            Namespace: ns,
            ClassName: typeName,
            MethodName: name,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"{name} on {typeName}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol BuildDelegateSummary(DelegateDeclarationSyntax del, string? ns, string source)
    {
        var span = del.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;
        var name = del.Identifier.Text;

        var signature = $"{del.Modifiers} delegate {del.ReturnType} {name}{del.ParameterList}".Trim();
        var snippet = SnippetBuilder.BuildClassSignatures(signature, Array.Empty<string>());

        var metadata = JsonSerializer.Serialize(new
        {
            base_types = Array.Empty<string>(),
            implements = Array.Empty<string>(),
            attributes = del.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_abstract = false,
            is_sealed = false,
            is_partial = false,
            kind = "delegate",
            returns = del.ReturnType.ToString(),
        });

        return new ExtractedSymbol(
            RecordType: RecordType.ClassSummary,
            SymbolName: ns is null ? name : $"{ns}.{name}",
            SymbolKind: SymbolKind.Class,
            Namespace: ns,
            ClassName: name,
            MethodName: null,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"delegate {name}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol BuildDestructorSummary(DestructorDeclarationSyntax dtor, string typeName, string? ns, string source)
    {
        var span = dtor.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;

        var signature = $"~{typeName}()".Trim();
        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);
        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = "void",
            parameters = Array.Empty<object>(),
            attributes = dtor.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = false,
            is_static = false,
            cyclomatic_complexity = 1,
            kind = "destructor",
        });

        var symbol = ns is null ? $"{typeName}.~{typeName}" : $"{ns}.{typeName}.~{typeName}";
        return new ExtractedSymbol(
            RecordType: RecordType.MethodSummary,
            SymbolName: symbol,
            SymbolKind: SymbolKind.Method,
            Namespace: ns,
            ClassName: typeName,
            MethodName: $"~{typeName}",
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"finalizer {typeName}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol BuildFieldSummary(FieldDeclarationSyntax field, VariableDeclaratorSyntax variable, string typeName, string? ns, string source)
    {
        var span = variable.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;
        var name = variable.Identifier.Text;
        var type = field.Declaration.Type.ToString();

        var modifiers = field.Modifiers.Select(m => m.Text).ToHashSet();
        var signature = $"{field.Modifiers} {type} {name}".Trim();
        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);

        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = type,
            parameters = Array.Empty<object>(),
            attributes = field.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = false,
            is_static = modifiers.Contains("static"),
            is_const = modifiers.Contains("const"),
            is_readonly = modifiers.Contains("readonly"),
            cyclomatic_complexity = 1,
            kind = "field",
        });

        var symbol = ns is null ? $"{typeName}.{name}" : $"{ns}.{typeName}.{name}";
        return new ExtractedSymbol(
            RecordType: RecordType.MethodSummary,
            SymbolName: symbol,
            SymbolKind: SymbolKind.Field,
            Namespace: ns,
            ClassName: typeName,
            MethodName: name,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"field {typeName}.{name}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol BuildEventSummary(EventDeclarationSyntax ev, string typeName, string? ns, string source)
    {
        var span = ev.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;
        var name = ev.Identifier.Text;
        var type = ev.Type.ToString();

        var modifiers = ev.Modifiers.Select(m => m.Text).ToHashSet();
        var signature = $"{ev.Modifiers} event {type} {name}".Trim();
        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);

        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = type,
            parameters = Array.Empty<object>(),
            attributes = ev.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = false,
            is_static = modifiers.Contains("static"),
            cyclomatic_complexity = 1,
            kind = "event",
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
            Summary: $"event {typeName}.{name}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol BuildEventFieldSummary(EventFieldDeclarationSyntax evf, VariableDeclaratorSyntax variable, string typeName, string? ns, string source)
    {
        var span = variable.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;
        var name = variable.Identifier.Text;
        var type = evf.Declaration.Type.ToString();

        var modifiers = evf.Modifiers.Select(m => m.Text).ToHashSet();
        var signature = $"{evf.Modifiers} event {type} {name}".Trim();
        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);

        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = type,
            parameters = Array.Empty<object>(),
            attributes = evf.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = false,
            is_static = modifiers.Contains("static"),
            cyclomatic_complexity = 1,
            kind = "event_field",
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
            Summary: $"event {typeName}.{name}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol BuildEnumMemberSummary(EnumMemberDeclarationSyntax member, string typeName, string? ns, string source)
    {
        var span = member.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;
        var name = member.Identifier.Text;
        var value = member.EqualsValue?.Value.ToString();

        var signature = value is null ? name : $"{name} = {value}";
        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);

        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = typeName,
            parameters = Array.Empty<object>(),
            attributes = member.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = false,
            is_static = true,
            cyclomatic_complexity = 1,
            kind = "enum_member",
            value,
        });

        var symbol = ns is null ? $"{typeName}.{name}" : $"{ns}.{typeName}.{name}";
        return new ExtractedSymbol(
            RecordType: RecordType.MethodSummary,
            SymbolName: symbol,
            SymbolKind: SymbolKind.Field,
            Namespace: ns,
            ClassName: typeName,
            MethodName: name,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"enum member {typeName}.{name}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static ExtractedSymbol BuildLocalFunctionSummary(LocalFunctionStatementSyntax local, string typeName, string? enclosingMethod, string? ns, string source)
    {
        var span = local.GetLocation().GetLineSpan();
        var lineStart = span.StartLinePosition.Line + 1;
        var lineEnd = span.EndLinePosition.Line + 1;
        var name = local.Identifier.Text;
        var modifiers = local.Modifiers.Select(m => m.Text).ToHashSet();

        var signature = $"{local.Modifiers} {local.ReturnType} {name}{local.ParameterList}".Trim();
        var snippet = SnippetBuilder.BuildMethod(source, lineStart, lineEnd);
        var (calls, instantiates) = CollectCallsAndInstantiations(local);

        var qualifier = enclosingMethod is null ? typeName : $"{typeName}.{enclosingMethod}";
        var metadata = JsonSerializer.Serialize(new
        {
            signature,
            returns = local.ReturnType.ToString(),
            parameters = local.ParameterList.Parameters.Select(p => new { name = p.Identifier.Text, type = p.Type?.ToString() ?? "object" }).ToList(),
            attributes = local.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.Name.ToString()).ToList(),
            is_async = modifiers.Contains("async"),
            is_static = modifiers.Contains("static"),
            cyclomatic_complexity = 1,
            kind = "local_function",
            enclosing_method = enclosingMethod,
            calls,
            instantiates,
        });

        var symbol = ns is null ? $"{qualifier}.{name}" : $"{ns}.{qualifier}.{name}";
        return new ExtractedSymbol(
            RecordType: RecordType.MethodSummary,
            SymbolName: symbol,
            SymbolKind: SymbolKind.Method,
            Namespace: ns,
            ClassName: typeName,
            MethodName: name,
            LineStart: lineStart,
            LineEnd: lineEnd,
            Summary: $"local function {qualifier}.{name}",
            CodeSnippet: snippet,
            MetadataJson: metadata);
    }

    internal static (IReadOnlyList<string> calls, IReadOnlyList<string> instantiates) CollectCallsAndInstantiations(SyntaxNode? node)
    {
        if (node is null) return (Array.Empty<string>(), Array.Empty<string>());

        var calls = new List<string>();
        var seenCalls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var inv in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = ExtractCalleeName(inv.Expression);
            if (string.IsNullOrEmpty(name)) continue;
            if (seenCalls.Add(name)) calls.Add(name);
            if (calls.Count >= 50) break;
        }

        var instantiates = new List<string>();
        var seenNew = new HashSet<string>(StringComparer.Ordinal);
        foreach (var oc in node.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var name = oc.Type.ToString();
            if (string.IsNullOrEmpty(name)) continue;
            if (seenNew.Add(name)) instantiates.Add(name);
            if (instantiates.Count >= 50) break;
        }
        return (calls, instantiates);
    }

    internal static string ExtractCalleeName(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        MemberBindingExpressionSyntax mb => mb.Name.Identifier.Text,
        AliasQualifiedNameSyntax a => a.Name.Identifier.Text,
        _ => expr.ToString(),
    };

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
