using Microsoft.CodeAnalysis;

namespace CFITS.Generator;

public sealed partial class FitsContextGenerator
{
    private static string EmitDocument(DocumentModel document)
    {
        string name = document.Symbol.Name;
        string full = document.Symbol.ToDisplayString();
        string primary = document.KeepHeaders
            ? "        public CFITS.FitsHeader PrimaryHeader { get; private set; }" + (document.Kind is "class" or "record" ? " = null!;" : "")
            : "";
        string helpers = string.Join(NewLine, document.Members
            .Where(static member => member.Child is not null)
            .Select(static member => member.Child!.Table ? EmitTable(member.Child) : EmitImage(member.Child)));
        string copy = document.KeepHeaders ? EmitKeptHeaderCopy() : "";
        return $$"""
            #nullable enable
            {{NamespaceOf(document.Symbol)}}{{document.Accessibility}} partial {{document.Kind}} {{name}} : CFITS.IFitsDocument<{{full}}>
            {
            {{primary}}
            {{EmitDocumentRead(document, full)}}
            {{EmitDocumentWrite(document, full)}}
            {{EmitReader(document, full)}}
            {{copy}}
            {{helpers}}
            }
            """;
    }

    private static string EmitDocumentRead(DocumentModel document, string full)
    {
        var data = document.Members.Where(static member => member.Child is not null).ToList();
        bool hasOther = document.Members.Any(static member => member.Role == DocumentMemberRole.OtherHdus);
        bool hasSpecial = document.Members.Any(static member => member.Role == DocumentMemberRole.SpecialRecords);
        bool useDirect = data.Count == 1
            && !hasOther
            && !hasSpecial;
        string direct = useDirect ? EmitDirectDocumentRead(document, full, data[0]) : "        return ReadOpened(CFITS.FitsDocument.Open(stream, options));";
        string opened = useDirect
            ? ""
            : $$"""

                private static {{full}} ReadOpened(CFITS.FitsDocument document)
                {
            {{EmitOpenedRead(document, full)}}
                }
            """;
        return $$"""
                public static {{full}} Read(System.IO.Stream stream, CFITS.FitsOpenOptions options)
                {
                    System.ArgumentNullException.ThrowIfNull(stream);
                    System.ArgumentNullException.ThrowIfNull(options);
            {{direct}}
                }
            {{opened}}
            """;
    }

    private static string EmitDirectDocumentRead(DocumentModel document, string full, DocumentMemberModel member)
    {
        bool separatePrimary = member.Child!.HduIndex > 0
            && (document.KeepHeaders || document.PrimaryKeywords is not null);
        string value = member.Role == DocumentMemberRole.Image
            ? $"var __cfitsValue = {member.Child.InfoName}.Read(stream, options, out var __cfitsMemberHeader);"
            : $"var __cfitsRows = {member.Child.InfoName}.Read(stream, options, out var __cfitsMemberHeader);";
        string primary = separatePrimary
            ? """
                    stream.Position = 0;
                    CFITS.FitsDocument.TryReadPrimaryHeader(stream, out CFITS.FitsHeader? __cfitsPrimary);
            """
            : "            var __cfitsPrimary = __cfitsMemberHeader;";
        string body = member.Role == DocumentMemberRole.Image
            ? EmitImageDocumentInitializer(document, full, member, "__cfitsValue", "__cfitsPrimary")
            : EmitTableDocumentInitializer(document, full, member, "__cfitsRows", "__cfitsPrimary", "__cfitsMemberHeader");
        return $$"""
                    {{value}}
            {{primary}}
            {{EmitPrimaryRequired(document, "__cfitsPrimary")}}
                    return {{body}};
            """;
    }

    private static string EmitReader(DocumentModel document, string full)
    {
        var fields = new List<string>();
        var members = new List<string>();
        if (document.KeepHeaders)
        {
            fields.Add("            private bool __cfitsHasPrimaryHeader;");
            fields.Add("            private CFITS.FitsHeader __cfitsPrimaryHeader = null!;");
            members.Add("""
                        public CFITS.FitsHeader PrimaryHeader
                        {
                            get
                            {
                                if (__cfitsHasPrimaryHeader)
                                {
                                    return __cfitsPrimaryHeader;
                                }

                                EnsureOrigin();
                                if (!CFITS.FitsDocument.TryReadPrimaryHeader(_stream, out CFITS.FitsHeader? __cfitsHeader) || __cfitsHeader is null)
                                {
                                    throw new CFITS.FitsException("The primary header is missing.");
                                }

                                __cfitsPrimaryHeader = __cfitsHeader;
                                __cfitsHasPrimaryHeader = true;
                                return __cfitsPrimaryHeader;
                            }
                        }
            """);
        }

        if (document.PrimaryKeywords is { } primary)
        {
            string typeName = primary.Property.Type.ToDisplayString();
            fields.Add("            private bool __cfitsHasPrimaryKeywords;");
            fields.Add($"            private {typeName} __cfitsPrimaryKeywords = default!;");
            string creation = EmitKeywordObjectCreation(primary, "__cfitsHeader!", "__cfitsOpenKeyword");
            string required = primary.Required
                ? """
                                    if (__cfitsHeader is null)
                                    {
                                        throw new CFITS.FitsException("The primary header is missing.");
                                    }

                """
                : "";
            members.Add($$"""
                        public {{typeName}} {{primary.Property.Name}}
                        {
                            get
                            {
                                if (__cfitsHasPrimaryKeywords)
                                {
                                    return __cfitsPrimaryKeywords;
                                }

                                EnsureOrigin();
                                CFITS.FitsDocument.TryReadPrimaryHeader(_stream, out CFITS.FitsHeader? __cfitsHeader);
            {{required}}                    __cfitsPrimaryKeywords = __cfitsHeader is null ? default! : {{creation}};
                                __cfitsHasPrimaryKeywords = true;
                                return __cfitsPrimaryKeywords;
                            }
                        }
            """);
        }

        bool cacheDocument = document.Members.Any(static member =>
            member.Role is DocumentMemberRole.OtherHdus or DocumentMemberRole.SpecialRecords);
        if (cacheDocument)
        {
            fields.Add("            private bool __cfitsHasDocument;");
            fields.Add($"            private {full} __cfitsDocument = default!;");
            members.Add($$"""
                        private {{full}} ReadDocument()
                        {
                            if (__cfitsHasDocument)
                            {
                                return __cfitsDocument;
                            }

                            EnsureOrigin();
                            __cfitsDocument = Read(_stream, _options);
                            __cfitsHasDocument = true;
                            return __cfitsDocument;
                        }
            """);
        }

        foreach (DocumentMemberModel member in document.Members)
        {
            string typeName = member.Property.Type.ToDisplayString();
            string flag = $"__cfitsHas{member.Property.Name}";
            string slot = $"__cfitsSlot{member.Property.Name}";
            if (member.Child is not null)
            {
                fields.Add($"            private bool {flag};");
                fields.Add($"            private {typeName} {slot} = default!;");
                string load = member.Role == DocumentMemberRole.Image
                    ? $"{slot} = {member.Child.InfoName}.Read(_stream, _options, out _);"
                    : $$"""
                        var __cfitsRows = {{member.Child.InfoName}}.Read(_stream, _options, out var __cfitsHeader);
                                    {{slot}} = __cfitsRows is null ? default! : {{EmitTableWrapper(member, "__cfitsRows", "__cfitsHeader")}};
                        """;
                members.Add($$"""
                            public {{typeName}} {{member.Property.Name}}
                            {
                                get
                                {
                                    if ({{flag}})
                                    {
                                        return {{slot}};
                                    }

                                    EnsureOrigin();
                                    {{load}}
                                    {{flag}} = true;
                                    return {{slot}};
                                }
                            }
                """);
                continue;
            }

            if (member.Role is DocumentMemberRole.OtherHdus or DocumentMemberRole.SpecialRecords)
            {
                members.Add($"            public {typeName} {member.Property.Name} => ReadDocument().{member.Property.Name};");
            }
        }

        return $$"""
                public static Reader Open(System.IO.Stream stream) => Open(stream, new CFITS.FitsOpenOptions());

                public static Reader Open(System.IO.Stream stream, CFITS.FitsOpenOptions options)
                {
                    System.ArgumentNullException.ThrowIfNull(stream);
                    System.ArgumentNullException.ThrowIfNull(options);
                    return new Reader(stream, options, ownsStream: false);
                }

                public static Reader Open(string path)
                {
                    System.ArgumentNullException.ThrowIfNull(path);
                    return new Reader(System.IO.File.OpenRead(path), new CFITS.FitsOpenOptions(), ownsStream: true);
                }

                public static Reader Open(string path, CFITS.FitsOpenOptions options)
                {
                    System.ArgumentNullException.ThrowIfNull(path);
                    System.ArgumentNullException.ThrowIfNull(options);
                    return new Reader(System.IO.File.OpenRead(path), options, ownsStream: true);
                }

                public sealed class Reader : System.IDisposable
                {
                    private System.IO.Stream _stream;
                    private readonly CFITS.FitsOpenOptions _options;
                    private bool _owns;
            {{string.Join(NewLine, fields)}}
                    internal Reader(System.IO.Stream stream, CFITS.FitsOpenOptions options, bool ownsStream)
                    {
                        _stream = stream;
                        _options = options;
                        _owns = ownsStream;
                    }

                    public {{full}} Deserialize()
                    {
                        EnsureOrigin();
                        return Read(_stream, _options);
                    }
            {{string.Join(NewLine, members)}}
                    public void Dispose()
                    {
                        if (!_owns)
                        {
                            return;
                        }

                        _owns = false;
                        _stream.Dispose();
                    }

                    private void EnsureOrigin()
                    {
                        if (!_stream.CanSeek)
                        {
                            var buffer = new System.IO.MemoryStream();
                            _stream.CopyTo(buffer);
                            if (_owns)
                            {
                                _stream.Dispose();
                            }

                            _stream = buffer;
                            _owns = true;
                        }

                        _stream.Position = 0;
                    }
                }
            """;
    }

    private static string EmitOpenedRead(DocumentModel document, string full)
    {
        bool hasOther = document.Members.Any(static member => member.Role == DocumentMemberRole.OtherHdus);
        var statements = new List<string>();
        var assignments = new List<string>();
        statements.Add(hasOther
            ? "            var __cfitsMapped = new bool[document.Hdus.Count];"
            : "            bool[]? __cfitsMapped = null;");
        statements.Add("            CFITS.FitsHeader? __cfitsPrimary = document.Hdus.Count > 0 ? document.Hdus[0].Header : null;");
        if (document.PrimaryKeywords?.Required == true)
        {
            statements.Add("            if (__cfitsPrimary is null)");
            statements.Add("            {");
            statements.Add("                throw new CFITS.FitsException(\"The primary header is missing.\");");
            statements.Add("            }");
        }

        AddPrimaryAssignments(document, assignments, "__cfitsPrimary");
        foreach (DocumentMemberModel member in document.Members)
        {
            EmitOpenedMember(member, statements, assignments);
        }

        return $$"""
            {{string.Join(NewLine, statements)}}
                    return new {{full}}
                    {
            {{string.Join(NewLine, assignments)}}
                    };
            """;
    }

    private static void EmitOpenedMember(DocumentMemberModel member, List<string> statements, List<string> assignments)
    {
        if (member.Role == DocumentMemberRole.OtherHdus)
        {
            assignments.Add($"                {member.Property.Name} = document.SnapshotHdus(__cfitsMapped!),");
            return;
        }

        if (member.Role == DocumentMemberRole.SpecialRecords)
        {
            assignments.Add($"                {member.Property.Name} = document.SnapshotSpecialRecords(),");
            return;
        }

        if (member.Role == DocumentMemberRole.Image)
        {
            string image = $"__cfitsValue{member.Property.Name}";
            statements.Add($"            var {image} = {member.Child!.InfoName}.ReadDocument(document, __cfitsMapped, out _);");
            assignments.Add($"                {member.Property.Name} = {image},");
            return;
        }

        string rows = $"__cfitsRows{member.Property.Name}";
        string header = $"__cfitsHeader{member.Property.Name}";
        statements.Add($"            var {rows} = {member.Child!.InfoName}.ReadDocument(document, __cfitsMapped, out var {header});");
        string wrapper = EmitTableWrapper(member, rows, header);
        assignments.Add($"                {member.Property.Name} = {rows} is null ? default! : {wrapper},");
    }

    private static string EmitImageDocumentInitializer(
        DocumentModel document,
        string full,
        DocumentMemberModel member,
        string value,
        string header)
    {
        var assignments = new List<string>();
        AddPrimaryAssignments(document, assignments, header);
        assignments.Add($"                {member.Property.Name} = {value},");
        return $$"""
            new {{full}}
                    {
            {{string.Join(NewLine, assignments)}}
                    }
            """;
    }

    private static string EmitTableDocumentInitializer(
        DocumentModel document,
        string full,
        DocumentMemberModel member,
        string rows,
        string primaryHeader,
        string memberHeader)
    {
        var assignments = new List<string>();
        AddPrimaryAssignments(document, assignments, primaryHeader);
        string wrapper = EmitTableWrapper(member, rows, memberHeader);
        assignments.Add($"                {member.Property.Name} = {rows} is null ? default! : {wrapper},");
        return $$"""
            new {{full}}
                    {
            {{string.Join(NewLine, assignments)}}
                    }
            """;
    }

    private static string EmitTableWrapper(DocumentMemberModel member, string rows, string header)
    {
        string headerValue = header.EndsWith("!", StringComparison.Ordinal) ? header : header + "!";
        var assignments = new List<string>
        {
            $"                    {member.RowProperty} = {rows},",
        };
        if (member.AssignHeader)
        {
            assignments.Add($"                    Header = {headerValue},");
        }

        AddKeywordAssignments(assignments, member.Keywords, headerValue, $"__cfits{member.Property.Name}Keyword", "                    ");
        if (member.KeywordObject is { } keywordObject)
        {
            assignments.Add($"                    {keywordObject.Property.Name} = {EmitKeywordObjectCreation(keywordObject, headerValue, $"__cfits{member.Property.Name}Header")},");
        }

        return $$"""
            new {{member.Wrapper!.ToDisplayString()}}
                        {
            {{string.Join(NewLine, assignments)}}
                        }
            """;
    }

    private static void AddPrimaryAssignments(DocumentModel document, List<string> assignments, string header)
    {
        if (document.KeepHeaders)
        {
            assignments.Add($"                PrimaryHeader = {header}!,");
        }

        if (document.PrimaryKeywords is { } primary)
        {
            string creation = EmitKeywordObjectCreation(primary, header, "__cfitsPrimaryKeyword");
            assignments.Add($"                {primary.Property.Name} = {header} is null ? default! : {creation},");
        }
    }

    private static string EmitPrimaryRequired(DocumentModel document, string header)
    {
        if (document.PrimaryKeywords?.Required != true)
        {
            return "";
        }

        return $$"""
                    if ({{header}} is null)
                    {
                        throw new CFITS.FitsException("The primary header is missing.");
                    }

            """;
    }

    private static void AddKeywordAssignments(List<string> assignments, List<MemberModel> keywords, string header, string prefix, string indent)
    {
        foreach (MemberModel member in keywords)
        {
            assignments.Add($"{indent}{member.Symbol.Name} = {ReadKeyword(member, header, prefix)},");
        }
    }

    private static string EmitKeywordObjectCreation(KeywordObjectModel model, string header, string prefix)
    {
        var assignments = new List<string>();
        AddKeywordAssignments(assignments, model.Keywords, header, prefix, "                    ");
        return $$"""
            new {{model.Symbol.ToDisplayString()}}
                        {
            {{string.Join(NewLine, assignments)}}
                        }
            """;
    }

    private static string EmitDocumentWrite(DocumentModel document, string full)
    {
        string valueCheck = document.Kind is "class" or "record"
            ? "            System.ArgumentNullException.ThrowIfNull(value);"
            : "";
        var slots = new List<string>();
        int slot = 0;
        var cases = new List<string>();
        foreach (DocumentMemberModel member in document.Members.Where(static member => member.Child is not null))
        {
            slots.Add($"            __cfitsOrder.Add(({member.Child!.HduIndex}, {slot}, null));");
            cases.Add(EmitWriteCase(document, member, slot));
            slot++;
        }

        string other = document.Members.FirstOrDefault(static member => member.Role == DocumentMemberRole.OtherHdus) is { } otherMember
            ? $$"""
                        if (value.{{otherMember.Property.Name}} is not null)
                        {
                            foreach (var __cfitsSnapshot in value.{{otherMember.Property.Name}})
                            {
                                __cfitsOrder.Add((__cfitsSnapshot.Index, -1, __cfitsSnapshot));
                            }
                        }
            """
            : "";
        string special = document.Members.FirstOrDefault(static member => member.Role == DocumentMemberRole.SpecialRecords) is { } specialMember
            ? $"            builder.SpecialRecords(value.{specialMember.Property.Name});"
            : "";
        string dispatch = cases.Count == 0
            ? ""
            : "                        switch (__cfitsPiece.Slot)\n                        {\n"
                + string.Join(NewLine, cases)
                + "\n                        }";
        return $$"""
                public static void Write(System.IO.Stream stream, {{full}} value)
                {
                    System.ArgumentNullException.ThrowIfNull(stream);
            {{valueCheck}}
                    var builder = CFITS.FitsDocumentBuilder.Create();
                    Write(builder, value);
                    builder.Write(stream);
                }

                public static void Write(CFITS.FitsDocumentBuilder builder, {{full}} value)
                {
                    System.ArgumentNullException.ThrowIfNull(builder);
            {{valueCheck}}
                    var __cfitsOrder = new System.Collections.Generic.List<(int Index, int Slot, CFITS.FitsHduSnapshot? Snapshot)>();
            {{string.Join(NewLine, slots)}}
            {{other}}
                    __cfitsOrder.Sort(static (left, right) => left.Index.CompareTo(right.Index));
                    bool __cfitsWrotePrimary = false;
                    foreach (var __cfitsPiece in __cfitsOrder)
                    {
                        if (__cfitsPiece.Index > 0 && !__cfitsWrotePrimary)
                        {
                            builder.EnsurePrimaryHdu();
                            __cfitsWrotePrimary = true;
                        }

                        if (__cfitsPiece.Snapshot is not null)
                        {
                            builder.Hdu(__cfitsPiece.Snapshot);
                            __cfitsWrotePrimary |= __cfitsPiece.Index == 0;
                            continue;
                        }
            {{dispatch}}
                    }
            {{special}}
                }
            """;
    }

    private static string EmitWriteCase(DocumentModel document, DocumentMemberModel member, int slot)
    {
        string local = $"__cfits{member.Property.Name}";
        string overlay = EmitWriteOverlay(document, member, local);
        string call = member.Role == DocumentMemberRole.Image
            ? $"{member.Child!.InfoName}.WriteToBuilder(builder, {local}{(overlay.Length == 0 ? "" : $", {overlay}")});"
            : $$"""
                var __cfitsRows = {{local}}.{{member.RowProperty}} ?? System.Array.Empty<{{member.Child!.Symbol.ToDisplayString()}}>();
                            {{member.Child.InfoName}}.WriteToBuilder(builder, __cfitsRows{{(overlay.Length == 0 ? "" : $", {overlay}")}});
                """;
        string wrote = member.Child!.HduIndex == 0
            ? "                        __cfitsWrotePrimary = true;"
            : "";
        return $$"""
                            case {{slot}}:
                                if (value.{{member.Property.Name}} is { } {{local}})
                                {
                                    {{call}}
            {{wrote}}
                                }
                                break;
            """;
    }

    private static string EmitWriteOverlay(DocumentModel document, DocumentMemberModel member, string local)
    {
        var lines = new List<string>();
        if (document.KeepHeaders)
        {
            string kept = member.Child!.HduIndex == 0 ? "value.PrimaryHeader" : $"{local}.Header";
            string names = KeptKeywordNames(document, member);
            string arguments = names.Length == 0 ? "" : $", {names}";
            lines.Add($"                            if ({kept} is {{ }} __cfitsKept) CopyKeptHeader(__cfitsKept, header{arguments});");
        }

        if (member.Child!.HduIndex == 0 && document.PrimaryKeywords is { } primary)
        {
            lines.Add($"                            if (value.{primary.Property.Name} is {{ }} __cfitsPrimaryKeywords)");
            lines.Add("                            {");
            foreach (MemberModel keyword in primary.Keywords)
            {
                lines.Add($"                                {WriteKeyword(keyword, "__cfitsPrimaryKeywords")};");
            }

            lines.Add("                            }");
        }

        if (member.Keywords.Count > 0)
        {
            lines.Add($"                            if ({local} is not null)");
            lines.Add("                            {");
            foreach (MemberModel keyword in member.Keywords)
            {
                lines.Add($"                                {WriteKeyword(keyword, local)};");
            }

            lines.Add("                            }");
        }

        if (member.KeywordObject is { } keywordObject)
        {
            lines.Add($"                            if ({local}.{keywordObject.Property.Name} is {{ }} __cfitsTableKeywords)");
            lines.Add("                            {");
            foreach (MemberModel keyword in keywordObject.Keywords)
            {
                lines.Add($"                                {WriteKeyword(keyword, "__cfitsTableKeywords")};");
            }

            lines.Add("                            }");
        }

        if (lines.Count == 0)
        {
            return "";
        }

        return $$"""
            header =>
                            {
            {{string.Join(NewLine, lines)}}
                            }
            """;
    }

    private static string KeptKeywordNames(DocumentModel document, DocumentMemberModel member)
    {
        var names = new List<string>();
        if (member.Child is not null)
        {
            foreach (MemberModel keyword in member.Child.Members)
            {
                if (keyword.Role == MemberRole.Keyword)
                {
                    names.Add(Literal(keyword.ColumnName));
                }
            }

            if (member.Child.KeywordObject is { } imageKeywords)
            {
                foreach (MemberModel keyword in imageKeywords.Keywords)
                {
                    names.Add(Literal(keyword.ColumnName));
                }
            }
        }

        if (member.Child?.HduIndex == 0 && document.PrimaryKeywords is { } primary)
        {
            foreach (MemberModel keyword in primary.Keywords)
            {
                names.Add(Literal(keyword.ColumnName));
            }
        }

        foreach (MemberModel keyword in member.Keywords)
        {
            names.Add(Literal(keyword.ColumnName));
        }

        if (member.KeywordObject is { } tableKeywords)
        {
            foreach (MemberModel keyword in tableKeywords.Keywords)
            {
                names.Add(Literal(keyword.ColumnName));
            }
        }

        return string.Join(", ", names);
    }

    private static string EmitKeptHeaderCopy() => """
                private static void CopyKeptHeader(CFITS.FitsHeader source, CFITS.FitsHeaderBuilder header, params string[] replaced)
                {
                    foreach (var card in source.Cards)
                    {
                        if (IsStructuralKeyword(card.Keyword) || ReplacesKeyword(replaced, card.Keyword))
                        {
                            continue;
                        }

                        switch (card.Kind)
                        {
                            case CFITS.FitsValueKind.Logical:
                                header.Logical(card.Keyword, card.Logical, card.Comment);
                                break;
                            case CFITS.FitsValueKind.Integer:
                                header.Integer(card.Keyword, card.Integer, card.Comment);
                                break;
                            case CFITS.FitsValueKind.Real:
                                header.Real(card.Keyword, card.Real, card.Comment);
                                break;
                            case CFITS.FitsValueKind.String:
                                header.Text(card.Keyword, card.Text ?? "", card.Comment);
                                break;
                            case CFITS.FitsValueKind.Complex:
                                header.Complex(card.Keyword, card.Real, card.Imaginary, card.Comment);
                                break;
                            case CFITS.FitsValueKind.Commentary:
                                if (card.Keyword == "COMMENT") header.Comment(card.Text ?? "");
                                else if (card.Keyword == "HISTORY") header.History(card.Text ?? "");
                                else header.Blank(card.Text ?? "");
                                break;
                        }
                    }
                }

                private static bool ReplacesKeyword(string[] names, string keyword)
                {
                    foreach (var name in names)
                    {
                        if (string.Equals(name, keyword, System.StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                private static bool IsStructuralKeyword(string keyword)
                {
                    switch (keyword.ToUpperInvariant())
                    {
                        case "SIMPLE":
                        case "XTENSION":
                        case "BITPIX":
                        case "NAXIS":
                        case "PCOUNT":
                        case "GCOUNT":
                        case "EXTEND":
                        case "TFIELDS":
                        case "THEAP":
                        case "GROUPS":
                        case "END":
                        case "CHECKSUM":
                        case "DATASUM":
                        case "BSCALE":
                        case "BZERO":
                        case "BLANK":
                        case "EXTNAME":
                            return true;
                    }

                    string upper = keyword.ToUpperInvariant();
                    return upper.StartsWith("NAXIS", System.StringComparison.Ordinal)
                        || upper.StartsWith("TFORM", System.StringComparison.Ordinal)
                        || upper.StartsWith("TTYPE", System.StringComparison.Ordinal)
                        || upper.StartsWith("TBCOL", System.StringComparison.Ordinal)
                        || upper.StartsWith("TDIM", System.StringComparison.Ordinal);
                }
        """;
}
