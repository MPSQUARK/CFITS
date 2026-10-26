using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CFITS.Generator;

public sealed partial class FitsContextGenerator
{
    private static string EmitTable(TypeModel type)
    {
        string name = type.Symbol.ToDisplayString();
        var columnMembers = type.Members.Where(static member => member.Role == MemberRole.Column).ToList();
        MemberModel? unsupported = type.Members.FirstOrDefault(static member =>
            member.Role is MemberRole.Column or MemberRole.Keyword
            && !IsSupportedTableMember(member.Symbol.Type));
        if (unsupported is not null)
        {
            return EmitUnsupportedTable(type, unsupported);
        }

        string cachedColumns = string.Join(NewLine, columnMembers.Select((member, index) =>
            $"                        var __cfitsColumn{index} = table.Column({Literal(member.ColumnName)});"));
        string bulkCapabilities = string.Join(NewLine, columnMembers
            .Select((member, index) => EmitBulkColumn(member, index))
            .Where(static text => text.Length > 0));
        string bulkRents = string.Join(NewLine, columnMembers
            .Select((member, index) => EmitBulkRent(member, index))
            .Where(static text => text.Length > 0));
        string bulkCopies = string.Join(NewLine, columnMembers
            .Select((member, index) => EmitBulkCopy(member, index))
            .Where(static text => text.Length > 0));
        string bulkReturns = string.Join(NewLine, columnMembers
            .Select((member, index) => EmitBulkReturn(member, index))
            .Where(static text => text.Length > 0));
        string scalarColumns = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Column)
            .Select((member, index) => $"                    {member.Symbol.Name} = {ReadColumn(member, index, bulk: false)},"));
        string bulkColumnsRead = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Column)
            .Select((member, index) => $"                    {member.Symbol.Name} = {ReadColumn(member, index, bulk: true)},"));
        string scalarLoop = string.Join(NewLine,
            "                            for (int row = 0; row < table.RowCount; row++)",
            "                            {",
            $"                                rows[row] = new {name}",
            "                                {",
            scalarColumns,
            "                                };",
            "                            }");
        string bulkLoop = string.Join(NewLine,
            "                            for (int row = 0; row < table.RowCount; row++)",
            "                            {",
            $"                                rows[row] = new {name}",
            "                                {",
            bulkColumnsRead,
            "                                };",
            "                            }");
        string rowLoops = bulkCapabilities.Length == 0
            ? scalarLoop
            : string.Join(NewLine,
                $"                        if (table.RowCount >= {BulkReadRowThreshold})",
                "                        {",
                bulkRents,
                "                            try",
                "                            {",
                bulkCopies,
                bulkLoop,
                "                            }",
                "                            finally",
                "                            {",
                bulkReturns,
                "                            }",
                "                        }",
                "                        else",
                "                        {",
                scalarLoop,
                "                        }");
        string writes = string.Join(NewLine, type.Members
            .Where(static member => member.Role == MemberRole.Column)
            .Select(EmitColumnWrite));
        string extensionName = string.IsNullOrEmpty(type.HduName)
            ? ""
            : $"                header.Text(\"EXTNAME\", {Literal(type.HduName!)});";
        string nameArgument = string.IsNullOrEmpty(type.HduName) ? "null" : Literal(type.HduName!);
        string variableHelpers = EmitVariableColumnHelpers(columnMembers);
        string columnNameField = columnMembers.Count == 0
            ? ""
            : "                    private static readonly string[] __cfitsColumns = new string[] { "
                + string.Join(", ", columnMembers.Select(member => Literal(member.ColumnName)))
                + " };";
        string directColumns = columnMembers.Count == 0 ? "" : ", __cfitsColumns";
        return $$"""
                private sealed class {{type.InfoName}} : CFITS.FitsTableTypeInfo<{{name}}>
                {
            {{columnNameField}}
            {{variableHelpers}}
                    public override System.Collections.Generic.IReadOnlyList<{{name}}> Read(System.IO.Stream stream)
                    {
                        return Read(stream, new CFITS.FitsOpenOptions());
                    }

                    public override System.Collections.Generic.IReadOnlyList<{{name}}> Read(System.IO.Stream stream, CFITS.FitsOpenOptions options)
                    {
                        if (CFITS.FitsDirectTable.TryOpen(stream, {{type.HduIndex}}, {{nameArgument}}, options{{directColumns}}, out CFITS.FitsDirectTable? __cfitsDirect))
                        {
                            using (__cfitsDirect)
                            {
                                return ReadTable(__cfitsDirect!.Table);
                            }
                        }

                        using var document = CFITS.FitsDocument.Open(stream, options);
                        return ReadDocument(document);
                    }

                    internal static System.Collections.Generic.IReadOnlyList<{{name}}> ReadDocument(CFITS.FitsDocument document, bool[]? mapped = null)
                    {
            {{FindTable(type.HduName, type.HduIndex)}}
                        if (mapped is not null) mapped[hdu.Index] = true;
                        return ReadTable(hdu.Table ?? throw new CFITS.FitsException("The HDU is not a table."));
                    }

                    internal static System.Collections.Generic.IReadOnlyList<{{name}}> ReadTable(CFITS.FitsTable table)
                    {
                        var rows = new {{name}}[table.RowCount];
            {{cachedColumns}}
            {{bulkCapabilities}}
            {{rowLoops}}
                        return rows;
                    }
                    public override void Write(System.IO.Stream stream, System.Collections.Generic.IReadOnlyList<{{name}}> rows)
                    {
                        var builder = CFITS.FitsDocumentBuilder.Create();
                        Write(builder, rows);
                        builder.Write(stream);
                    }
                    public override void Write(CFITS.FitsDocumentBuilder builder, System.Collections.Generic.IReadOnlyList<{{name}}> rows)
                    {
                        WriteToBuilder(builder, rows);
                    }
                    internal static void WriteToBuilder(CFITS.FitsDocumentBuilder builder, System.Collections.Generic.IReadOnlyList<{{name}}> rows)
                    {
                        var table = new CFITS.BinaryTableBuilder();
            {{writes}}
                        builder.BinaryTable(table, header =>
                        {
            {{extensionName}}
                        });
                    }
                }
            """;
    }
    private static string EmitVariableColumnHelpers(IReadOnlyList<MemberModel> members)
    {
        bool hasInteger = members.Any(static member =>
            member.Symbol.Type is IArrayTypeSymbol array
            && array.ElementType.SpecialType == SpecialType.System_Int32);
        bool hasDouble = members.Any(static member =>
            member.Symbol.Type is IArrayTypeSymbol array
            && array.ElementType.SpecialType == SpecialType.System_Double);
        bool hasFloat = members.Any(static member =>
            member.Symbol.Type is IArrayTypeSymbol array
            && array.ElementType.SpecialType == SpecialType.System_Single);
        var helpers = new StringBuilder();
        if (hasInteger)
        {
            helpers.AppendLine("""
                    private static int[] ReadInt32Array(CFITS.FitsColumn column, int row)
                    {
                        var values = new int[column.Count(row)];
                        column.CopyIntegers(row, values);
                        return values;
                    }
                """);
        }

        if (hasDouble)
        {
            helpers.AppendLine("""
                    private static double[] ReadDoubleArray(CFITS.FitsColumn column, int row)
                    {
                        return column.ReadRealVector(row);
                    }
                """);
        }

        if (hasFloat)
        {
            helpers.AppendLine("""
                    private static float[] ReadSingleArray(CFITS.FitsColumn column, int row)
                    {
                        var values = new float[column.Count(row)];
                        for (int index = 0; index < values.Length; index++)
                        {
                            values[index] = (float)column.ReadReal(row, index);
                        }

                        return values;
                    }
                """);
        }

        return helpers.ToString();
    }

    private static string EmitColumnWrite(MemberModel member)
    {
        string local = member.Symbol.Name.ToLowerInvariant() + "Values";
        if (member.Symbol.Type is IArrayTypeSymbol array)
        {
            string arrayElement = ElementName(array.ElementType);
            return $$"""
                        var {{local}} = new {{arrayElement}}[rows.Count][];
                        for (int index = 0; index < rows.Count; index++)
                        {
                            {{local}}[index] = rows[index].{{member.Symbol.Name}};
                        }
                        table.AddVariable({{Literal(member.ColumnName)}}, {{local}});
            """;
        }

        string element = ElementName(member.Symbol.Type);
        if (member.Symbol.Type.SpecialType == SpecialType.System_String)
        {
            return $$"""
                        var {{local}} = new {{element}}[rows.Count];
                        for (int index = 0; index < rows.Count; index++) {{local}}[index] = rows[index].{{member.Symbol.Name}};
                        int {{local}}Width = 1;
                        foreach (var item in {{local}}) if (item is not null && item.Length > {{local}}Width) {{local}}Width = item.Length;
                        table.Add({{Literal(member.ColumnName)}}, {{local}}, {{local}}Width);
                """;
        }
        return $$"""
                    var {{local}} = new {{element}}[rows.Count];
                    for (int index = 0; index < rows.Count; index++) {{local}}[index] = rows[index].{{member.Symbol.Name}};
                    table.Add({{Literal(member.ColumnName)}}, {{local}});
            """;
    }

    private static string EmitUnsupportedTable(TypeModel type, MemberModel member)
    {
        string name = type.Symbol.ToDisplayString();
        string message = Literal($"Generated table serialization does not support member '{member.ColumnName}' of type '{member.Symbol.Type.ToDisplayString()}'.");
        return $$"""
                private sealed class {{type.InfoName}} : CFITS.FitsTableTypeInfo<{{name}}>
                {
                    public override System.Collections.Generic.IReadOnlyList<{{name}}> Read(System.IO.Stream stream)
                    {
                        throw new System.NotSupportedException({{message}});
                    }
                    public override void Write(System.IO.Stream stream, System.Collections.Generic.IReadOnlyList<{{name}}> rows)
                    {
                        throw new System.NotSupportedException({{message}});
                    }
                }
            """;
    }

    private static string EmitKeywordWrites(TypeModel type, string variable)
    {
        return string.Join(NewLine, type.Members
            .Where(static item => item.Role == MemberRole.Keyword)
            .Select(member => $"                {WriteKeyword(member, variable)};"));
    }

    private static string WriteKeyword(MemberModel member, string variable)
    {
        string value = variable + "." + member.Symbol.Name;
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"header.Logical({Literal(member.ColumnName)}, {value})",
            SpecialType.System_Byte or SpecialType.System_Int32 or SpecialType.System_Int16 => $"header.Integer({Literal(member.ColumnName)}, {value})",
            SpecialType.System_Int64 => $"header.Integer({Literal(member.ColumnName)}, {value})",
            SpecialType.System_Double or SpecialType.System_Single => $"header.Real({Literal(member.ColumnName)}, {value})",
            SpecialType.System_String => $"header.Text({Literal(member.ColumnName)}, {value} ?? \"\")",
            _ => $"header.Text({Literal(member.ColumnName)}, {value}?.ToString() ?? \"\")",
        };
    }

    private static string KeywordKind(MemberModel member) => member.Symbol.Type.SpecialType switch
    {
        SpecialType.System_Boolean => "CFITS.FitsKeywordKind.Logical",
        SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64 => "CFITS.FitsKeywordKind.Integer",
        SpecialType.System_Double or SpecialType.System_Single => "CFITS.FitsKeywordKind.Real",
        _ => "CFITS.FitsKeywordKind.Text",
    };

    private static string ReadDirectKeyword(MemberModel member, int index)
    {
        string query = $"__cfitsKeywords[{index}]";
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{query}.Logical",
            SpecialType.System_Byte => $"(byte){query}.Integer",
            SpecialType.System_Int32 => $"(int){query}.Integer",
            SpecialType.System_Int16 => $"(short){query}.Integer",
            SpecialType.System_Int64 => $"{query}.Integer",
            SpecialType.System_Double => $"{query}.Real",
            SpecialType.System_Single => $"(float){query}.Real",
            SpecialType.System_String => $"{query}.Text",
            _ => $"{query}.Text",
        };
    }

    private static string ReadKeyword(MemberModel member, string header)
    {
        string keyword = member.ColumnName;
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{header}.GetBoolean({Literal(keyword)})",
            SpecialType.System_Byte => $"(byte){header}.GetInt64({Literal(keyword)})",
            SpecialType.System_Int32 => $"(int){header}.GetInt64({Literal(keyword)})",
            SpecialType.System_Int16 => $"(short){header}.GetInt64({Literal(keyword)})",
            SpecialType.System_Int64 => $"{header}.GetInt64({Literal(keyword)})",
            SpecialType.System_Double => $"{header}.GetDouble({Literal(keyword)})",
            SpecialType.System_Single => $"(float){header}.GetDouble({Literal(keyword)})",
            SpecialType.System_String => $"{header}.GetString({Literal(keyword)})",
            _ => $"{header}.GetString({Literal(keyword)})",
        };
    }

    private static string ReadColumn(MemberModel member, int index, bool bulk = true)
    {
        string column = $"__cfitsColumn{index}";
        string integer = $"__cfitsInteger{index}![row]";
        string real = $"__cfitsReal{index}![row]";
        if (member.Symbol.Type is IArrayTypeSymbol array)
        {
            return array.ElementType.SpecialType switch
            {
                SpecialType.System_Double => $"ReadDoubleArray({column}, row)",
                SpecialType.System_Single => $"ReadSingleArray({column}, row)",
                _ => $"ReadInt32Array({column}, row)",
            };
        }

        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{column}.ReadLogical(row)",
            SpecialType.System_Int32 => bulk ? $"(int)(__cfitsInteger{index}Bulk ? {integer} : {column}.ReadInteger(row))" : $"(int){column}.ReadInteger(row)",
            SpecialType.System_Int16 => bulk ? $"(short)(__cfitsInteger{index}Bulk ? {integer} : {column}.ReadInteger(row))" : $"(short){column}.ReadInteger(row)",
            SpecialType.System_Int64 => bulk ? $"(__cfitsInteger{index}Bulk ? {integer} : {column}.ReadInteger(row))" : $"{column}.ReadInteger(row)",
            SpecialType.System_Byte => bulk ? $"(byte)(__cfitsInteger{index}Bulk ? {integer} : {column}.ReadInteger(row))" : $"(byte){column}.ReadInteger(row)",
            SpecialType.System_Double => bulk ? $"(__cfitsReal{index}Bulk ? {real} : {column}.ReadReal(row))" : $"{column}.ReadReal(row)",
            SpecialType.System_Single => bulk ? $"(__cfitsReal{index}Bulk ? (float){real} : (float){column}.ReadReal(row))" : $"(float){column}.ReadReal(row)",
            SpecialType.System_String => $"{column}.ReadString(row)",
            _ => $"{column}.ReadString(row)",
        };
    }

    private static string EmitBulkColumn(MemberModel member, int index)
    {
        return member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_Int64 or SpecialType.System_Byte =>
                $"                        bool __cfitsInteger{index}Bulk = __cfitsColumn{index}.CanCopyInteger;",
            SpecialType.System_Double or SpecialType.System_Single =>
                $"                        bool __cfitsReal{index}Bulk = __cfitsColumn{index}.CanCopyReal;",
            _ => "",
        };
    }

    private static string EmitBulkRent(MemberModel member, int index) =>
        member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Int32 =>
                $"                            int[]? __cfitsInteger{index} = __cfitsInteger{index}Bulk ? System.Buffers.ArrayPool<int>.Shared.Rent(table.RowCount) : null;",
            SpecialType.System_Int16 =>
                $"                            short[]? __cfitsInteger{index} = __cfitsInteger{index}Bulk ? System.Buffers.ArrayPool<short>.Shared.Rent(table.RowCount) : null;",
            SpecialType.System_Int64 =>
                $"                            long[]? __cfitsInteger{index} = __cfitsInteger{index}Bulk ? System.Buffers.ArrayPool<long>.Shared.Rent(table.RowCount) : null;",
            SpecialType.System_Byte =>
                $"                            byte[]? __cfitsInteger{index} = __cfitsInteger{index}Bulk ? System.Buffers.ArrayPool<byte>.Shared.Rent(table.RowCount) : null;",
            SpecialType.System_Double =>
                $"                            double[]? __cfitsReal{index} = __cfitsReal{index}Bulk ? System.Buffers.ArrayPool<double>.Shared.Rent(table.RowCount) : null;",
            SpecialType.System_Single =>
                $"                            float[]? __cfitsReal{index} = __cfitsReal{index}Bulk ? System.Buffers.ArrayPool<float>.Shared.Rent(table.RowCount) : null;",
            _ => "",
        };

    private static string EmitBulkCopy(MemberModel member, int index) =>
        member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Int32 =>
                $"                                if (__cfitsInteger{index}Bulk && !__cfitsColumn{index}.TryCopyInteger(__cfitsInteger{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            SpecialType.System_Int16 =>
                $"                                if (__cfitsInteger{index}Bulk && !__cfitsColumn{index}.TryCopyInteger(__cfitsInteger{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            SpecialType.System_Int64 =>
                $"                                if (__cfitsInteger{index}Bulk && !__cfitsColumn{index}.TryCopyInteger(__cfitsInteger{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            SpecialType.System_Byte =>
                $"                                if (__cfitsInteger{index}Bulk && !__cfitsColumn{index}.TryCopyInteger(__cfitsInteger{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            SpecialType.System_Double =>
                $"                                if (__cfitsReal{index}Bulk && !__cfitsColumn{index}.TryCopyReal(__cfitsReal{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            SpecialType.System_Single =>
                $"                                if (__cfitsReal{index}Bulk && !__cfitsColumn{index}.TryCopyReal(__cfitsReal{index}!.AsSpan(0, table.RowCount))) throw new CFITS.FitsException(\"The column changed bulk-copy capability.\");",
            _ => "",
        };

    private static string EmitBulkReturn(MemberModel member, int index) =>
        member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Int32 =>
                $"                                if (__cfitsInteger{index} is not null) System.Buffers.ArrayPool<int>.Shared.Return(__cfitsInteger{index});",
            SpecialType.System_Int16 =>
                $"                                if (__cfitsInteger{index} is not null) System.Buffers.ArrayPool<short>.Shared.Return(__cfitsInteger{index});",
            SpecialType.System_Int64 =>
                $"                                if (__cfitsInteger{index} is not null) System.Buffers.ArrayPool<long>.Shared.Return(__cfitsInteger{index});",
            SpecialType.System_Byte =>
                $"                                if (__cfitsInteger{index} is not null) System.Buffers.ArrayPool<byte>.Shared.Return(__cfitsInteger{index});",
            SpecialType.System_Double =>
                $"                                if (__cfitsReal{index} is not null) System.Buffers.ArrayPool<double>.Shared.Return(__cfitsReal{index});",
            SpecialType.System_Single =>
                $"                                if (__cfitsReal{index} is not null) System.Buffers.ArrayPool<float>.Shared.Return(__cfitsReal{index});",
            _ => "",
        };

    private static string BulkCapability(MemberModel member, int index) =>
        member.Symbol.Type.SpecialType switch
        {
            SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_Int64 or SpecialType.System_Byte =>
                $"__cfitsInteger{index}Bulk",
            SpecialType.System_Double or SpecialType.System_Single =>
                $"__cfitsReal{index}Bulk",
            _ => "false",
        };
    private static bool IsSupportedTableMember(ITypeSymbol type) =>
        type is IArrayTypeSymbol array
            && array.Rank == 1
            && array.ElementType.SpecialType is (SpecialType.System_Int32 or SpecialType.System_Single or SpecialType.System_Double)
        || type.SpecialType is SpecialType.System_Boolean
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_Int32
            or SpecialType.System_Int64
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_String;
}
