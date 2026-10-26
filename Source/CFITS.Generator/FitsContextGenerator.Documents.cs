using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CFITS.Generator;

public sealed partial class FitsContextGenerator
{
    private static string EmitDocument(DocumentModel document, IReadOnlyList<TypeModel> knownTypes)
    {
        string root = document.Symbol.ToDisplayString();
        bool hasUnknown = document.Members.Any(static member => member.Role == DocumentMemberRole.Unknown);
        bool canWriteChildrenDirectly = CanWriteChildrenDirectly(document);
        string mappedArgument = hasUnknown ? "mapped" : "null";
        string members = string.Join(NewLine, document.Members.Select(member =>
        {
            string assignment = member.Role switch
            {
                DocumentMemberRole.Image =>
                    $"{member.Property.Name} = {member.Child!.InfoName}.ReadDocument(document, {mappedArgument}),",
                DocumentMemberRole.Table =>
                    $"{member.Property.Name} = {member.Child!.InfoName}.ReadDocument(document, {mappedArgument}),",
                DocumentMemberRole.Unknown =>
                    $"{member.Property.Name} = document.SnapshotHdus(mapped!),",
                DocumentMemberRole.SpecialRecords =>
                    $"{member.Property.Name} = document.SnapshotSpecialRecords(),",
                _ => throw new InvalidOperationException(),
            };
            return $"                {assignment}";
        }));
        string helpers = string.Join(NewLine, document.Members
            .Where(static member => member.Child is not null)
            .Select(member => member.Child!.Table ? EmitTable(member.Child) : EmitImage(member.Child)));
        string unknownUse = hasUnknown
            ? "            var mapped = new bool[document.Hdus.Count];"
            : "";
        string writes = string.Join(NewLine, document.Members
            .Where(static member => member.Child is not null)
            .Select((member, index) =>
            {
                if (member.Child!.HduIndex < 0)
                {
                    return $"                        throw new System.InvalidOperationException(\"The mapped HDU for property '{member.Property.Name}' has no explicit HduIndex; name-based composite writes cannot preserve HDU order.\");";
                }

                return $$"""
                        var mappedDocument{{index}} = CFITS.FitsDocumentBuilder.Create();
                        {{(member.Child.HduIndex > 0 ? $"                        mappedDocument{index}.EnsurePrimaryHdu();" : "")}}
                        {{member.Child.InfoName}}.WriteToBuilder(mappedDocument{{index}}, value.{{member.Property.Name}});
                        using var mappedResult{{index}} = mappedDocument{{index}}.Build();
                        var mappedHdu{{index}} = mappedResult{{index}}.SnapshotHdus()[^1].WithIndex({{member.Child.HduIndex}});
                        ordered.Add((mappedHdu{{index}}.Index, mappedHdu{{index}}));
                """;
            }));
        string unknownWrites = document.Members.Any(static member => member.Role == DocumentMemberRole.Unknown)
            ? $"                        foreach (var snapshot in value.{document.Members.First(static member => member.Role == DocumentMemberRole.Unknown).Property.Name}) ordered.Add((snapshot.Index, snapshot));"
            : "";
        string specialWrite = document.Members.FirstOrDefault(static member => member.Role == DocumentMemberRole.SpecialRecords) is { } specialMember
            ? $"                        builder.SpecialRecords(value.{specialMember.Property.Name});"
            : "";
        string writeBody = canWriteChildrenDirectly
            ? EmitDirectDocumentWrite(document)
            : $$"""
                        var ordered = new System.Collections.Generic.List<(int Index, CFITS.FitsHduSnapshot Snapshot)>();
            {{writes}}
            {{unknownWrites}}
                        ordered.Sort(static (left, right) => left.Index.CompareTo(right.Index));
                        foreach (var item in ordered) builder.Hdu(item.Snapshot);
            {{specialWrite}}
            """;
        return $$"""
                private sealed class {{document.Symbol.Name}}Info : CFITS.FitsDocumentTypeInfo<{{root}}>
                {
                    public override {{root}} Read(CFITS.FitsDocument document)
                    {
                        {{unknownUse}}
                        return new {{root}}
                        {
            {{members}}
                        };
                    }

                    public override void Write(System.IO.Stream stream, {{root}} value)
                    {
                        var builder = CFITS.FitsDocumentBuilder.Create();
                        Write(builder, value);
                        builder.Write(stream);
                    }

                    public override void Write(CFITS.FitsDocumentBuilder builder, {{root}} value)
                    {
                        System.ArgumentNullException.ThrowIfNull(builder);
            {{writeBody}}
                    }
                }
            {{helpers}}
        """;
    }

    private static bool CanWriteChildrenDirectly(DocumentModel document)
    {
        if (document.Members.Any(static member =>
                member.Role is DocumentMemberRole.Unknown or DocumentMemberRole.SpecialRecords))
        {
            return false;
        }

        var children = document.Members
            .Where(static member => member.Child is not null)
            .Select(static member => member.Child!)
            .OrderBy(static child => child.HduIndex)
            .ToArray();
        return children.Length > 0
            && children.Select(static child => child.HduIndex).SequenceEqual(Enumerable.Range(0, children.Length));
    }

    private static string EmitDirectDocumentWrite(DocumentModel document)
    {
        var children = document.Members
            .Where(static member => member.Child is not null)
            .OrderBy(static member => member.Child!.HduIndex)
            .ToArray();
        var lines = new List<string>();
        foreach (DocumentMemberModel member in children)
        {
            if (member.Child!.HduIndex > 0)
            {
                lines.Add("                        builder.EnsurePrimaryHdu();");
            }

            lines.Add($"                        {member.Child.InfoName}.WriteToBuilder(builder, value.{member.Property.Name});");
        }

        return string.Join(NewLine, lines);
    }
}
