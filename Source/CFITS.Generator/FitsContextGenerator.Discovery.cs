using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CFITS.Generator;

public sealed partial class FitsContextGenerator
{
    private const string AttributeNamespace = "CFITS";

    private static bool IsDocumentDeclaration(SyntaxNode node) =>
        node is TypeDeclarationSyntax { AttributeLists.Count: > 0 } declaration
        && declaration is ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax;

    private static DocumentEmitResult? Describe(GeneratorSyntaxContext syntax)
    {
        if (syntax.SemanticModel.GetDeclaredSymbol(syntax.Node) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        AttributeData? documentAttribute = FindAttribute(symbol, "FitsDocumentAttribute");
        if (documentAttribute is null)
        {
            return null;
        }

        var diagnostics = new List<Diagnostic>();
        if (symbol.ContainingType is not null)
        {
            Report(diagnostics, FitsDiagnostics.NotTopLevel, symbol, symbol.Name);
        }

        if (symbol.IsStatic)
        {
            Report(diagnostics, FitsDiagnostics.StaticDocument, symbol, symbol.Name);
        }

        if (!IsPartial(symbol))
        {
            Report(diagnostics, FitsDiagnostics.NotPartial, symbol, symbol.ToDisplayString());
        }

        string kind = KindOf(symbol);
        if (kind.Length == 0)
        {
            Report(diagnostics, FitsDiagnostics.UnsupportedMember, symbol, symbol.Name, "document");
            return new DocumentEmitResult(null, diagnostics);
        }

        DocumentModel? model = DescribeDocument(symbol, documentAttribute, kind, diagnostics);
        return new DocumentEmitResult(diagnostics.Count == 0 ? model : null, diagnostics);
    }

    private static DocumentModel? DescribeDocument(
        INamedTypeSymbol symbol,
        AttributeData documentAttribute,
        string kind,
        List<Diagnostic> diagnostics)
    {
        bool ignoreOtherHdus = NamedBoolean(documentAttribute, "IgnoreOtherHdus", fallback: true);
        bool keepHeaders = !NamedBoolean(documentAttribute, "IgnoreUnmappedHeaders", fallback: true);
        var hdus = new List<HduDraft>();
        var members = new List<DocumentMemberModel>();
        KeywordObjectModel? primary = null;
        int otherCount = 0;
        int specialCount = 0;
        int primaryCount = 0;

        foreach (IPropertySymbol property in InstanceProperties(symbol))
        {
            AttributeData? image = FindAttribute(property, "FitsImageAttribute");
            AttributeData? table = FindAttribute(property, "FitsTableAttribute");
            AttributeData? other = FindAttribute(property, "FitsOtherHdusAttribute");
            AttributeData? special = FindAttribute(property, "FitsSpecialRecordsAttribute");
            AttributeData? primaryAttribute = FindAttribute(property, "FitsPrimaryHeaderAttribute");
            AttributeData? header = FindAttribute(property, "FitsHeaderAttribute");
            int selected = Count(image, table, other, special, primaryAttribute, header);
            if (selected > 1)
            {
                Report(diagnostics, FitsDiagnostics.DuplicateMember, property, property.Name, "FITS role");
                continue;
            }

            if (image is not null)
            {
                hdus.Add(DescribeImage(property, image, diagnostics));
            }
            else if (table is not null)
            {
                hdus.Add(DescribeTable(property, table, diagnostics));
            }
            else if (other is not null)
            {
                otherCount++;
                DescribeOther(property, members, diagnostics);
            }
            else if (special is not null)
            {
                specialCount++;
                DescribeSpecial(property, members, diagnostics);
            }
            else if (primaryAttribute is not null)
            {
                primaryCount++;
                primary = DescribePrimary(property, diagnostics);
            }
            else if (header is not null)
            {
                Report(diagnostics, FitsDiagnostics.UnsupportedMember, property, property.Name, "document header; use FitsPrimaryHeader");
            }
        }

        if (otherCount > 1)
        {
            Report(diagnostics, FitsDiagnostics.DuplicateMember, symbol, symbol.Name, "[FitsOtherHdus] property");
        }

        if (specialCount > 1)
        {
            Report(diagnostics, FitsDiagnostics.DuplicateMember, symbol, symbol.Name, "[FitsSpecialRecords] property");
        }

        if (primaryCount > 1)
        {
            Report(diagnostics, FitsDiagnostics.DuplicateMember, symbol, symbol.Name, "[FitsPrimaryHeader] property");
        }

        if (!ignoreOtherHdus && otherCount == 0)
        {
            Report(diagnostics, FitsDiagnostics.MissingOtherHdus, symbol, symbol.Name);
        }

        if (ignoreOtherHdus && otherCount > 0)
        {
            Report(diagnostics, FitsDiagnostics.OtherHdusWhileIgnored, symbol, symbol.Name);
        }

        hdus.Sort(static (left, right) => Position(left.Property).CompareTo(Position(right.Property)));
        AssignIndexes(hdus, diagnostics);
        ReportKeywordCollisions(primary, hdus, diagnostics);

        bool soleDataHdu = hdus.Count == 1;
        var headerTargets = new List<INamedTypeSymbol>();
        if (keepHeaders)
        {
            if (HasProperty(symbol, "PrimaryHeader"))
            {
                Report(diagnostics, FitsDiagnostics.GeneratedNameClash, symbol, symbol.ToDisplayString(), "PrimaryHeader");
            }
        }

        foreach (HduDraft hdu in hdus)
        {
            if (hdu.Failed)
            {
                continue;
            }

            bool needsHeader = keepHeaders && hdu.Index > 0;
            INamedTypeSymbol headerModel = hdu.Model;
            if (needsHeader)
            {
                if (HasProperty(headerModel, "Header"))
                {
                    Report(diagnostics, FitsDiagnostics.GeneratedNameClash, headerModel, headerModel.ToDisplayString(), "Header");
                }
                else if (!IsPartial(headerModel))
                {
                    Report(diagnostics, FitsDiagnostics.HeaderModelNotPartial, headerModel, headerModel.ToDisplayString(), "Header");
                }
                else if (!headerTargets.Contains(headerModel))
                {
                    headerTargets.Add(headerModel);
                }
            }

            bool materializeHeader = keepHeaders
                || hdu.KeywordObject is not null
                || hdu.WrapperKeywords.Count > 0
                || (soleDataHdu && primary is not null && hdu.Index == 0);
            bool hduRequired = hdu.Required || hdu.KeywordObject?.Required == true;
            // An explicit index selects that HDU even when EXTNAME is absent.
            // Declaration order still matches and writes the property name.
            string? readName = hdu.ExplicitName;
            if (!hdu.IndexExplicit && hdu.Index > 0 && string.IsNullOrEmpty(readName))
            {
                readName = hdu.Property.Name;
            }

            string? writeName = hdu.ExplicitName;
            if (hdu.Index > 0 && string.IsNullOrEmpty(writeName))
            {
                writeName = hdu.Property.Name;
            }

            INamedTypeSymbol childSymbol = hdu.Table ? hdu.Row! : hdu.Model;
            var child = new TypeModel(
                childSymbol,
                hdu.Table,
                readName,
                writeName,
                hdu.Index,
                hdu.Members,
                InfoName(childSymbol, hdu.Property, symbol),
                hduRequired,
                materializeHeader,
                assignHeader: needsHeader && !hdu.Table,
                keywordObject: hdu.Table ? null : hdu.KeywordObject,
                ownerName: hdu.Property.Name);
            members.Add(new DocumentMemberModel(
                hdu.Property,
                child,
                hdu.Table ? DocumentMemberRole.Table : DocumentMemberRole.Image,
                hdu.Required,
                hdu.Table ? hdu.Model : null,
                hdu.RowProperty,
                needsHeader,
                hdu.Table ? hdu.KeywordObject : null,
                hdu.Table ? hdu.WrapperKeywords : new List<MemberModel>()));
        }

        return new DocumentModel(
            symbol,
            AccessibilityOf(symbol),
            kind,
            members,
            ignoreOtherHdus,
            keepHeaders,
            primary,
            headerTargets);
    }

    private static HduDraft DescribeImage(IPropertySymbol property, AttributeData attribute, List<Diagnostic> diagnostics)
    {
        var draft = DraftFromAttribute(property, attribute, table: false);
        if (property.Type is not INamedTypeSymbol model)
        {
            Report(diagnostics, FitsDiagnostics.ImageData, property, property.Name);
            draft.Failed = true;
            return draft;
        }

        draft.Model = model;
        if (!HasAccessibleSetter(property, sameType: true))
        {
            Report(diagnostics, FitsDiagnostics.InaccessibleSetter, property, property.Name);
        }

        int dataCount = 0;
        int headerObjects = 0;
        foreach (IPropertySymbol member in InstanceProperties(model))
        {
            if (FindAttribute(member, "FitsDataAttribute") is not null)
            {
                dataCount++;
                DescribeData(member, draft.Members, diagnostics);
            }
            else if (FindAttribute(member, "FitsKeywordAttribute") is { } keyword)
            {
                DescribeKeyword(member, keyword, draft.Members, diagnostics);
            }
            else if (FindAttribute(member, "FitsColumnAttribute") is not null)
            {
                Report(diagnostics, FitsDiagnostics.UnsupportedMember, member, member.Name, "image");
            }
        }

        draft.KeywordObject = DescribeKeywordObject(model, diagnostics, out headerObjects);
        draft.KeywordObjectCount = (draft.Members.Any(static member => member.Role == MemberRole.Keyword) ? 1 : 0) + headerObjects;
        if (dataCount != 1)
        {
            Report(diagnostics, FitsDiagnostics.ImageData, model, model.ToDisplayString());
            draft.Failed = true;
        }

        return draft;
    }

    private static HduDraft DescribeTable(IPropertySymbol property, AttributeData attribute, List<Diagnostic> diagnostics)
    {
        var draft = DraftFromAttribute(property, attribute, table: true);
        if (property.Type is not INamedTypeSymbol model)
        {
            Report(diagnostics, FitsDiagnostics.TableShape, property, property.Name);
            draft.Failed = true;
            return draft;
        }

        draft.Model = model;
        if (!HasAccessibleSetter(property, sameType: true))
        {
            Report(diagnostics, FitsDiagnostics.InaccessibleSetter, property, property.Name);
        }

        var rowProperties = InstanceProperties(model).Where(static candidate => IsRowList(candidate.Type, out _)).ToList();
        if (rowProperties.Count != 1)
        {
            Report(diagnostics, FitsDiagnostics.TableShape, property, property.Name);
            draft.Failed = true;
            return draft;
        }

        IPropertySymbol rowProperty = rowProperties[0];
        draft.RowProperty = rowProperty.Name;
        if (!HasAccessibleSetter(rowProperty, sameType: false))
        {
            Report(diagnostics, FitsDiagnostics.InaccessibleSetter, rowProperty, rowProperty.Name);
        }

        IsRowList(rowProperty.Type, out ITypeSymbol? element);
        if (element is not INamedTypeSymbol row)
        {
            Report(diagnostics, FitsDiagnostics.TableShape, property, property.Name);
            draft.Failed = true;
            return draft;
        }

        draft.Row = row;
        foreach (IPropertySymbol member in InstanceProperties(row))
        {
            if (FindAttribute(member, "FitsColumnAttribute") is { } column)
            {
                DescribeColumn(member, column, draft.Members, diagnostics);
            }
            else if (FindAttribute(member, "FitsKeywordAttribute") is not null)
            {
                Report(diagnostics, FitsDiagnostics.KeywordOnRow, row, row.ToDisplayString());
            }
        }

        if (!draft.Members.Any(static member => member.Role == MemberRole.Column))
        {
            Report(diagnostics, FitsDiagnostics.RowColumns, row, row.ToDisplayString());
            draft.Failed = true;
        }

        draft.WrapperKeywords = DescribeKeywords(model, diagnostics);
        draft.KeywordObject = DescribeKeywordObject(model, diagnostics, out int headerObjects);
        draft.KeywordObjectCount = (draft.WrapperKeywords.Count > 0 ? 1 : 0) + headerObjects;
        return draft;
    }

    private static void DescribeOther(IPropertySymbol property, List<DocumentMemberModel> members, List<Diagnostic> diagnostics)
    {
        if (!IsSnapshotList(property.Type))
        {
            Report(diagnostics, FitsDiagnostics.OtherHduType, property, property.Name);
        }

        if (!HasAccessibleSetter(property, sameType: true))
        {
            Report(diagnostics, FitsDiagnostics.InaccessibleSetter, property, property.Name);
        }

        members.Add(EmptyMember(property, DocumentMemberRole.OtherHdus));
    }

    private static void DescribeSpecial(IPropertySymbol property, List<DocumentMemberModel> members, List<Diagnostic> diagnostics)
    {
        if (!IsSpecialRecords(property.Type))
        {
            Report(diagnostics, FitsDiagnostics.SpecialRecordsType, property, property.Name);
        }

        if (!HasAccessibleSetter(property, sameType: true))
        {
            Report(diagnostics, FitsDiagnostics.InaccessibleSetter, property, property.Name);
        }

        members.Add(EmptyMember(property, DocumentMemberRole.SpecialRecords));
    }

    private static KeywordObjectModel? DescribePrimary(IPropertySymbol property, List<Diagnostic> diagnostics)
    {
        if (property.Type is not INamedTypeSymbol named)
        {
            Report(diagnostics, FitsDiagnostics.UnsupportedMember, property, property.Name, "primary header");
            return null;
        }

        if (!HasAccessibleSetter(property, sameType: true))
        {
            Report(diagnostics, FitsDiagnostics.InaccessibleSetter, property, property.Name);
        }

        return new KeywordObjectModel(property, named, DescribeKeywords(named, diagnostics), IsRequired(property));
    }

    private static KeywordObjectModel? DescribeKeywordObject(INamedTypeSymbol owner, List<Diagnostic> diagnostics, out int count)
    {
        var properties = InstanceProperties(owner).Where(static property => FindAttribute(property, "FitsHeaderAttribute") is not null).ToList();
        count = properties.Count;
        if (count != 1)
        {
            return null;
        }

        IPropertySymbol property = properties[0];
        if (property.Type is not INamedTypeSymbol named)
        {
            Report(diagnostics, FitsDiagnostics.UnsupportedMember, property, property.Name, "header");
            return null;
        }

        if (!HasAccessibleSetter(property, sameType: false))
        {
            Report(diagnostics, FitsDiagnostics.InaccessibleSetter, property, property.Name);
        }

        return new KeywordObjectModel(property, named, DescribeKeywords(named, diagnostics), IsRequired(property));
    }

    private static List<MemberModel> DescribeKeywords(INamedTypeSymbol type, List<Diagnostic> diagnostics)
    {
        var keywords = new List<MemberModel>();
        foreach (IPropertySymbol property in InstanceProperties(type))
        {
            if (FindAttribute(property, "FitsKeywordAttribute") is { } attribute)
            {
                DescribeKeyword(property, attribute, keywords, diagnostics);
            }
        }

        return keywords;
    }

    private static void DescribeKeyword(IPropertySymbol property, AttributeData attribute, List<MemberModel> members, List<Diagnostic> diagnostics)
    {
        if (attribute.ConstructorArguments.FirstOrDefault().Value is not string name || name.Length == 0)
        {
            Report(diagnostics, FitsDiagnostics.UnsupportedMember, property, property.Name, "keyword");
            return;
        }

        if (!HasAccessibleSetter(property, sameType: false))
        {
            Report(diagnostics, FitsDiagnostics.InaccessibleSetter, property, property.Name);
        }

        members.Add(new MemberModel(property, MemberRole.Keyword, name, IsRequired(property)));
    }

    private static void DescribeData(IPropertySymbol property, List<MemberModel> members, List<Diagnostic> diagnostics)
    {
        if (property.Type is not IArrayTypeSymbol array
            || array.Rank > 3
            || !IsSupportedImageElement(array.ElementType))
        {
            Report(diagnostics, FitsDiagnostics.UnsupportedMember, property, property.Name, "image");
            return;
        }

        if (!HasAccessibleSetter(property, sameType: false))
        {
            Report(diagnostics, FitsDiagnostics.InaccessibleSetter, property, property.Name);
        }

        members.Add(new MemberModel(property, MemberRole.Data, property.Name, IsRequired(property)));
    }

    private static void DescribeColumn(IPropertySymbol property, AttributeData attribute, List<MemberModel> members, List<Diagnostic> diagnostics)
    {
        if (attribute.ConstructorArguments.FirstOrDefault().Value is not string name || name.Length == 0)
        {
            Report(diagnostics, FitsDiagnostics.UnsupportedMember, property, property.Name, "column");
            return;
        }

        if (!IsSupportedTableMember(property.Type))
        {
            Report(diagnostics, FitsDiagnostics.UnsupportedMember, property, property.Name, "column");
            return;
        }

        if (!HasAccessibleSetter(property, sameType: false))
        {
            Report(diagnostics, FitsDiagnostics.InaccessibleSetter, property, property.Name);
        }

        members.Add(new MemberModel(property, MemberRole.Column, name, IsRequired(property)));
    }

    private static void AssignIndexes(List<HduDraft> hdus, List<Diagnostic> diagnostics)
    {
        if (hdus.Count == 0)
        {
            return;
        }

        bool anyExplicit = false;
        bool anyOmitted = false;
        foreach (HduDraft hdu in hdus)
        {
            if (hdu.IndexExplicit)
            {
                anyExplicit = true;
                if (hdu.Index < 0)
                {
                    Report(diagnostics, FitsDiagnostics.NegativeHduIndex, hdu.Property, hdu.Property.Name);
                }
            }
            else
            {
                anyOmitted = true;
            }
        }

        if (anyExplicit && anyOmitted)
        {
            HduDraft omitted = hdus.First(static hdu => !hdu.IndexExplicit);
            Report(diagnostics, FitsDiagnostics.MixedHduIndexes, omitted.Property);
        }
        else if (anyOmitted)
        {
            for (int index = 0; index < hdus.Count; index++)
            {
                hdus[index].Index = index;
            }
        }

        var seen = new HashSet<int>();
        foreach (HduDraft hdu in hdus)
        {
            if (hdu.Index < 0 || !seen.Add(hdu.Index))
            {
                if (hdu.Index >= 0)
                {
                    Report(diagnostics, FitsDiagnostics.DuplicateHduIndex, hdu.Property, hdu.Index);
                }
            }
        }
    }

    private static void ReportKeywordCollisions(
        KeywordObjectModel? primary,
        List<HduDraft> hdus,
        List<Diagnostic> diagnostics)
    {
        var counts = new Dictionary<int, int>();
        if (primary is not null)
        {
            NoteKeywordObject(counts, diagnostics, 0, primary.Property);
        }

        foreach (HduDraft hdu in hdus)
        {
            for (int index = 0; index < hdu.KeywordObjectCount; index++)
            {
                NoteKeywordObject(counts, diagnostics, hdu.Index, hdu.Property);
            }
        }
    }

    private static void NoteKeywordObject(Dictionary<int, int> counts, List<Diagnostic> diagnostics, int index, ISymbol symbol)
    {
        counts.TryGetValue(index, out int count);
        count++;
        counts[index] = count;
        if (count == 2)
        {
            Report(diagnostics, FitsDiagnostics.TwoKeywordObjects, symbol, index);
        }
    }

    private static HduDraft DraftFromAttribute(IPropertySymbol property, AttributeData attribute, bool table)
    {
        bool explicitIndex = TryGetExplicitIndex(attribute, out int index);
        return new HduDraft
        {
            Property = property,
            Table = table,
            IndexExplicit = explicitIndex,
            Index = explicitIndex ? index : -1,
            ExplicitName = ExplicitName(attribute),
            Required = IsRequired(property),
            Members = new List<MemberModel>(),
            WrapperKeywords = new List<MemberModel>(),
        };
    }

    private static DocumentMemberModel EmptyMember(IPropertySymbol property, DocumentMemberRole role) =>
        new(property, null, role, IsRequired(property), null, null, false, null, []);

    private static bool TryGetExplicitIndex(AttributeData attribute, out int index)
    {
        foreach (TypedConstant argument in attribute.ConstructorArguments)
        {
            if (argument.Value is int value)
            {
                index = value;
                return true;
            }
        }

        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == "HduIndex" && argument.Value.Value is int named)
            {
                index = named;
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static string? ExplicitName(AttributeData attribute)
    {
        foreach (TypedConstant argument in attribute.ConstructorArguments)
        {
            if (argument.Value is string name && name.Length > 0)
            {
                return name;
            }
        }

        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == "Name" && argument.Value.Value is string named && named.Length > 0)
            {
                return named;
            }
        }

        return null;
    }

    private static bool NamedBoolean(AttributeData attribute, string name, bool fallback = false)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool value)
            {
                return value;
            }
        }

        return fallback;
    }

    private static bool IsRowList(ITypeSymbol type, out ITypeSymbol? element)
    {
        element = null;
        if (type is not INamedTypeSymbol named || !IsConstructed(named, "System.Collections.Generic", "IReadOnlyList", "List"))
        {
            return false;
        }

        element = named.TypeArguments[0];
        return true;
    }

    private static bool IsSnapshotList(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || !IsConstructed(named, "System.Collections.Generic", "IReadOnlyList"))
        {
            return false;
        }

        ITypeSymbol element = named.TypeArguments[0];
        return element.Name == "FitsHduSnapshot" && element.ContainingNamespace.ToDisplayString() == AttributeNamespace;
    }

    private static bool IsConstructed(INamedTypeSymbol named, string namespaceName, params string[] typeNames)
    {
        INamedTypeSymbol definition = named.OriginalDefinition;
        if (definition.TypeParameters.Length != 1 || named.TypeArguments.Length != 1)
        {
            return false;
        }

        if (definition.ContainingNamespace.ToDisplayString() != namespaceName)
        {
            return false;
        }

        foreach (string typeName in typeNames)
        {
            if (definition.Name == typeName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSpecialRecords(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            return array.Rank == 1 && array.ElementType.SpecialType == SpecialType.System_Byte;
        }

        return type is INamedTypeSymbol named
            && named.Name == "ReadOnlyMemory"
            && named.ContainingNamespace.ToDisplayString() == "System"
            && named.TypeArguments.Length == 1
            && named.TypeArguments[0].SpecialType == SpecialType.System_Byte;
    }

    private static bool IsRequired(ISymbol symbol) => FindAttribute(symbol, "FitsRequiredAttribute") is not null;

    private static bool HasProperty(INamedTypeSymbol symbol, string name) =>
        InstanceProperties(symbol).Any(property => property.Name == name);

    private static bool HasAccessibleSetter(IPropertySymbol property, bool sameType)
    {
        if (property.SetMethod is not { } setter)
        {
            return false;
        }

        return setter.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
            || (sameType && setter.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedOrInternal or Accessibility.ProtectedAndInternal);
    }

    private static bool IsPartial(INamedTypeSymbol symbol)
    {
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax type
                && type.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }

        return false;
    }

    private static string KindOf(INamedTypeSymbol symbol)
    {
        if (symbol.TypeKind == TypeKind.Struct)
        {
            return symbol.IsRecord ? "record struct" : "struct";
        }

        if (symbol.TypeKind == TypeKind.Class)
        {
            return symbol.IsRecord ? "record" : "class";
        }

        return "";
    }

    private static string AccessibilityOf(INamedTypeSymbol symbol) => symbol.DeclaredAccessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "internal",
    };

    private static int Position(IPropertySymbol property)
    {
        int position = int.MaxValue;
        foreach (SyntaxReference reference in property.DeclaringSyntaxReferences)
        {
            if (reference.Span.Start < position)
            {
                position = reference.Span.Start;
            }
        }

        return position;
    }

    private static string InfoName(INamedTypeSymbol child, IPropertySymbol property, INamedTypeSymbol document) =>
        $"{child.Name}_{property.Name}_{document.Name}Info";

    private static IEnumerable<IPropertySymbol> InstanceProperties(INamedTypeSymbol symbol) =>
        symbol.GetMembers().OfType<IPropertySymbol>().Where(static property => !property.IsStatic && !property.IsImplicitlyDeclared);

    private static AttributeData? FindAttribute(ISymbol symbol, string name)
    {
        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            if (IsAttribute(attribute, name))
            {
                return attribute;
            }
        }

        return null;
    }

    private static bool IsAttribute(AttributeData attribute, string name) =>
        attribute.AttributeClass is { } type
        && type.Name == name
        && type.ContainingNamespace.Name == AttributeNamespace
        && type.ContainingNamespace.ContainingNamespace.IsGlobalNamespace;

    private static int Count(params object?[] values)
    {
        int count = 0;
        foreach (object? value in values)
        {
            if (value is not null)
            {
                count++;
            }
        }

        return count;
    }

    private static void Report(List<Diagnostic> diagnostics, DiagnosticDescriptor descriptor, ISymbol symbol, params object[] args) =>
        diagnostics.Add(Diagnostic.Create(descriptor, LocationOf(symbol), args));

    private static Location LocationOf(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(static location => location.IsInSource) ?? Location.None;

    private sealed class HduDraft
    {
        public IPropertySymbol Property { get; set; } = null!;

        public bool Table { get; set; }

        public bool IndexExplicit { get; set; }

        public int Index { get; set; }

        public string? ExplicitName { get; set; }

        public bool Required { get; set; }

        public INamedTypeSymbol Model { get; set; } = null!;

        public INamedTypeSymbol? Row { get; set; }

        public string? RowProperty { get; set; }

        public List<MemberModel> Members { get; set; } = new();

        public List<MemberModel> WrapperKeywords { get; set; } = new();

        public KeywordObjectModel? KeywordObject { get; set; }

        public int KeywordObjectCount { get; set; }

        public bool Failed { get; set; }
    }
}
