using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace ConfigKit;

/// <summary>
/// A member's <c>&lt;summary&gt;</c> doc comment, read out of the XML documentation file the
/// compiler emits beside the assembly.
///
/// Most config classes are already documented - a <c>///</c> line above each field saying what
/// it does is how anyone writes one - and asking an author to repeat that text in a
/// <c>[Description]</c> so it reaches the screen is exactly the duplication this library exists
/// to avoid. Where a member has both, the attribute wins: it was written for this purpose and
/// the doc comment was written for a reader of the source.
///
/// This costs the author one line in their csproj:
///
///     &lt;GenerateDocumentationFile&gt;true&lt;/GenerateDocumentationFile&gt;
///
/// and shipping the resulting .xml in the zip beside the .dll. Nothing here fails when it is
/// missing, which is the common case - the member simply has no tooltip, as before.
///
/// Vintage Story unpacks a mod zip to Cache/unpack/&lt;zip&gt;_&lt;hash&gt;/ and loads the assembly
/// from there, so Assembly.Location is a real path and the .xml sits next to it. AutoConfigLib
/// reads the same file the same way, so this is a well-trodden path on this platform rather
/// than a new trick.
/// </summary>
public static class XmlDocs
{
    private static readonly ConcurrentDictionary<Assembly, XmlDocument?> _documents = new();

    /// <summary>
    /// The summary text for one member, or null if there is none - no documentation file, no
    /// entry for this member, or an empty summary.
    /// </summary>
    public static string? Summary(MemberInfo member)
    {
        if (member.DeclaringType == null) return null;

        XmlDocument? document = DocumentFor(member.DeclaringType.Assembly);

        return document == null ? null : SummaryIn(document, member);
    }

    /// <summary>
    /// The same lookup against a documentation file supplied outright, rather than found next
    /// to an assembly. The suite is compiled in memory by the game's own Roslyn, so its own
    /// types have no assembly file and no .xml beside one - this is how the parsing is tested
    /// without one.
    /// </summary>
    public static string? SummaryIn(string documentation, MemberInfo member)
    {
        try
        {
            XmlDocument document = new();
            document.LoadXml(documentation);
            return SummaryIn(document, member);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string? SummaryIn(XmlDocument document, MemberInfo member)
    {
        string? key = KeyFor(member);
        if (key == null) return null;

        try
        {
            XmlNode? summary = document.SelectSingleNode($"/doc/members/member[@name='{key}']/summary");
            return summary == null ? null : Flatten(summary);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Drops every cached document. For tests, which load the same assembly repeatedly.</summary>
    public static void ClearCache() => _documents.Clear();

    private static XmlDocument? DocumentFor(Assembly assembly) => _documents.GetOrAdd(assembly, Load);

    private static XmlDocument? Load(Assembly assembly)
    {
        // A dynamic assembly has no file, and neither does one loaded from bytes. Both are
        // normal here: test assemblies are compiled in memory by the game's own Roslyn.
        if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) return null;

        try
        {
            string path = Path.Combine(
                Path.GetDirectoryName(assembly.Location)!,
                Path.GetFileNameWithoutExtension(assembly.Location) + ".xml");

            if (!File.Exists(path)) return null;

            XmlDocument document = new();
            document.Load(path);
            return document;
        }
        catch (Exception)
        {
            // A malformed or unreadable doc file costs tooltips, nothing else.
            return null;
        }
    }

    /// <summary>
    /// The compiler's name for a member: "P:Namespace.Type.Member" for a property,
    /// "F:..." for a field. A nested type is spelled with a dot rather than the plus sign
    /// reflection uses, and a generic type carries its arity as `n, which FullName already
    /// gives us.
    /// </summary>
    private static string? KeyFor(MemberInfo member)
    {
        char kind = member switch
        {
            PropertyInfo => 'P',
            FieldInfo => 'F',
            _ => '\0'
        };

        if (kind == '\0') return null;

        string? declaring = member.DeclaringType?.FullName?.Replace('+', '.');
        if (declaring == null) return null;

        // Single quotes would break out of the XPath predicate this is interpolated into.
        // No legal C# identifier contains one, so a name that does is not a member name.
        string name = member.Name;
        if (declaring.Contains('\'') || name.Contains('\'')) return null;

        return $"{kind}:{declaring}.{name}";
    }

    /// <summary>
    /// The readable text of a summary element. Doc comments are indented XML, so the raw
    /// InnerText arrives full of newlines and leading spaces; inline elements carry the part
    /// of the sentence that names something, so they are kept as their text rather than
    /// dropped.
    /// </summary>
    private static string Flatten(XmlNode summary)
    {
        StringBuilder text = new();
        Append(summary, text);

        // Collapse the indentation of the source file into single spaces.
        string[] words = text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words);
    }

    private static void Append(XmlNode node, StringBuilder text)
    {
        foreach (XmlNode child in node.ChildNodes)
        {
            switch (child)
            {
                case XmlText or XmlCDataSection:
                    text.Append(child.Value);
                    break;

                // <see cref="T:Some.Type"/> and <paramref name="x"/> carry their subject in an
                // attribute rather than as text. The one-letter prefix and the namespace are
                // noise in a tooltip, so only the last segment is kept.
                case XmlElement element when element.ChildNodes.Count == 0:
                    string? reference = element.GetAttribute("cref") is { Length: > 0 } cref
                        ? cref
                        : element.GetAttribute("name") is { Length: > 0 } named ? named : null;

                    if (reference != null) text.Append(' ').Append(LastSegment(reference)).Append(' ');
                    break;

                // <para>, <c>, <b> and friends: keep the words, drop the markup.
                default:
                    text.Append(' ');
                    Append(child, text);
                    text.Append(' ');
                    break;
            }
        }
    }

    private static string LastSegment(string reference)
    {
        // "T:Namespace.Type.Member" -> "Member"
        int colon = reference.IndexOf(':');
        string body = colon >= 0 ? reference[(colon + 1)..] : reference;

        int dot = body.LastIndexOf('.');
        return dot >= 0 && dot < body.Length - 1 ? body[(dot + 1)..] : body;
    }
}
