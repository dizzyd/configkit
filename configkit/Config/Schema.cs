// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace ConfigKit;

internal enum SchemaKind
{
    /// <summary>A leaf the settings model already has a control for.</summary>
    Scalar,
    /// <summary>A mod-declared class or struct. Grouping only - it holds no setting of its own.</summary>
    Object,
    /// <summary>A dictionary. One setting whose value is the whole subtree.</summary>
    Dictionary,
    /// <summary>A list, set or array. One setting whose value is the whole subtree.</summary>
    List,
    /// <summary>Something we have no editor for but Newtonsoft can still round-trip.</summary>
    Opaque,
    /// <summary>Something that cannot be edited or persisted at all. Rendered disabled, never silent.</summary>
    Dead
}

/// <summary>
/// One member of a config class, classified.
///
/// A single node type with a <see cref="Kind"/> discriminator rather than a class hierarchy:
/// there is no polymorphic behaviour to dispatch, and every consumer - the definition
/// emitter, the assigner, the GUI - wants to walk the whole tree and switch, which a
/// hierarchy only makes more indirect.
/// </summary>
internal sealed class SchemaNode
{
    public SchemaKind Kind;
    public MemberInfo Member = null!;
    public Type MemberType = null!;
    /// <summary>The object node this member is declared inside, or null at the config root.</summary>
    public SchemaNode? Parent;

    /// <summary>The member's own name, or its [JsonProperty] name where it has one.</summary>
    public string Code = "";
    /// <summary>JSON path from the config root: "Thirst/HungerRate".</summary>
    public string Path = "";
    /// <summary>
    /// Paths tried on read and never written. A rename must never orphan a value that is
    /// already sitting in a player's file - see docs/, and the [JsonProperty] case.
    /// </summary>
    public List<string> LegacyPaths = [];

    /// <summary>Explicit label from [DisplayName] or [Display(Name)]. Null means "derive one".</summary>
    public string? Label;
    public string? Comment;
    /// <summary>
    /// A .NET format string from [DisplayFormat(DataFormatString = "...")], applied when a
    /// number is written out - "P" for a ratio a player thinks of as a percentage, "N2" for
    /// one that should not sprawl. Display only: the value stored is untouched.
    /// </summary>
    public string? Format;
    /// <summary>
    /// Which section this member belongs to, as an identity rather than a caption:
    /// "cat:Doors" for a name the author chose, or the owning object's path for one derived
    /// from a class. The two namespaces cannot collide, which a shared display name could -
    /// a [Category("Rain collector")] and a nested RainCollector used to become one section.
    /// </summary>
    public string? SectionId;
    /// <summary>What that section is called on screen. Never compared, only drawn.</summary>
    public string? SectionLabel;
    /// <summary>True when [Category] or [Display(GroupName)] named the section outright.</summary>
    public bool SectionExplicit;
    /// <summary>Domain hint for a key or string, from [DataType]. Unused until autocomplete lands.</summary>
    public string? KeySource;

    public bool ClientSide;
    public bool Logarithmic;
    /// <summary>Hidden from the settings screen, but still persisted. [Browsable(false)].</summary>
    public bool Hidden;
    public bool ReadOnly;
    /// <summary>Where the row sits: declaration order unless [Display(Order)] says otherwise.</summary>
    public float Weight;

    // Kind-specific.
    public ConfigSettingType ScalarType;        // Scalar
    public List<SchemaNode> Children = [];      // Object
    public SchemaNode? KeyNode;                 // Dictionary
    public SchemaNode? ValueNode;               // Dictionary
    public SchemaNode? ElementNode;             // List
    /// <summary>Which member of a list element labels its row, from [Key] or the first string.</summary>
    public string? LabelMember;                 // List
    public string? DeadReason;                  // Dead

    /// <summary>
    /// True when this node becomes a setting of its own. An object contributes a heading and a
    /// path prefix but no setting; a dead node cannot be persisted at all.
    /// </summary>
    public bool IsSetting => Kind is SchemaKind.Scalar or SchemaKind.Dictionary
                                  or SchemaKind.List or SchemaKind.Opaque;

    public override string ToString() => $"{Path} : {Kind}";
}

/// <summary>
/// The shape of one config class. Derived from the <see cref="Type"/> alone and cached by it -
/// deliberately holds no values, because defaults come from the instance the mod hands us and
/// two mods may register different instances of the same class.
/// </summary>
internal sealed class ConfigSchema
{
    public required Type Root;
    public required List<SchemaNode> Nodes;
    /// <summary>Anything worth telling the author about at registration. Never thrown away silently.</summary>
    public required List<string> Notices;

    /// <summary>Depth-first over every node, parents before children.</summary>
    public IEnumerable<SchemaNode> Walk()
    {
        foreach (SchemaNode node in Nodes)
        {
            foreach (SchemaNode descendant in Walk(node)) yield return descendant;
        }
    }

    private static IEnumerable<SchemaNode> Walk(SchemaNode node)
    {
        yield return node;
        foreach (SchemaNode child in node.Children)
        {
            foreach (SchemaNode descendant in Walk(child)) yield return descendant;
        }
    }

    /// <summary>
    /// One line for the log at registration. The rule this exists to serve: every public
    /// member is either rendered, deliberately excluded, or reported. Nothing vanishes -
    /// which is exactly what the old reflection walk did to anything it could not classify.
    /// </summary>
    public string Summary()
    {
        int scalars = 0, objects = 0, containers = 0, opaque = 0, dead = 0, hidden = 0;

        foreach (SchemaNode node in Walk())
        {
            if (node.Hidden) hidden++;
            switch (node.Kind)
            {
                case SchemaKind.Scalar: scalars++; break;
                case SchemaKind.Object: objects++; break;
                case SchemaKind.Dictionary:
                case SchemaKind.List: containers++; break;
                case SchemaKind.Opaque: opaque++; break;
                case SchemaKind.Dead: dead++; break;
            }
        }

        List<string> parts = [$"{scalars} settings"];
        if (objects > 0) parts.Add($"{objects} sections");
        if (containers > 0) parts.Add($"{containers} containers");
        if (opaque > 0) parts.Add($"{opaque} as raw JSON");
        if (hidden > 0) parts.Add($"{hidden} hidden");
        if (dead > 0) parts.Add($"{dead} not editable");

        return string.Join(", ", parts);
    }
}

internal static class SchemaBuilder
{
    /// <summary>
    /// A member whose type is already on the stack is a cycle, but a long chain of distinct
    /// types is not - and would still produce hundreds of rows nobody asked for. Both need a
    /// stop.
    /// </summary>
    private const int MaxDepth = 5;

    /// <summary>Where members that declared no [Display(Order)] start. DataAnnotations' own default.</summary>
    private const int UnorderedBase = 10000;

    private static readonly Dictionary<Type, ConfigSchema> _cache = [];
    private static readonly object _cacheLock = new();

    public static ConfigSchema For(Type type)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(type, out ConfigSchema? cached)) return cached;
        }

        List<string> notices = [];
        Stack<Type> visiting = new();
        visiting.Push(type);

        // One counter for the whole walk, not one per object. Restarting it inside a nested
        // class gave that class's members the same positions as the config's own first
        // members, so a section declared near the bottom sorted to the top.
        int[] position = [0];

        List<SchemaNode> nodes = [];

        foreach (MemberInfo member in Members(type))
        {
            SchemaNode? node = Build(member, prefix: "", sectionId: null, sectionLabel: null, visiting, depth: 1, notices, position);
            if (node != null) nodes.Add(node);
        }

        ConfigSchema schema = new() { Root = type, Nodes = nodes, Notices = notices };

        lock (_cacheLock)
        {
            _cache[type] = schema;
        }

        return schema;
    }

    /// <summary>
    /// Public instance fields and properties: fields in the order they were written, then
    /// properties in the order they were written.
    ///
    /// Reflection cannot give true source order across the two kinds - they live in separate
    /// metadata tables, so their tokens are not comparable - and GetMembers' own order puts
    /// every property first, which sent a whole section to the top of the screen because one
    /// member of it happened to be a get-only collection. Fields lead because a config class
    /// is nearly always fields; a class that needs them interleaved says so with
    /// [Display(Order)].
    /// </summary>
    private static IEnumerable<MemberInfo> Members(Type type)
    {
        foreach (MemberInfo member in type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(member => member is FieldInfo ? 0 : 1)
            .ThenBy(member => member.MetadataToken))
        {
            switch (member)
            {
                case FieldInfo:
                    yield return member;
                    break;

                // An indexer reaches GetValue with no arguments and throws
                // TargetParameterCountException, which used to take the mod's whole
                // registration down with it. It has never produced a key, so skipping it
                // cannot orphan one.
                case PropertyInfo property when property.GetIndexParameters().Length == 0:
                    yield return member;
                    break;
            }
        }
    }

    private static SchemaNode? Build(MemberInfo member, string prefix, string? sectionId, string? sectionLabel, Stack<Type> visiting, int depth, List<string> notices, int[] position)
    {
        Type memberType = (member as PropertyInfo)?.PropertyType ?? ((FieldInfo)member).FieldType;

        // [JsonIgnore] says "do not persist this". That is a statement about serialisation,
        // so it excludes the member outright. [Browsable(false)] says "do not show this",
        // which is a statement about display only - it hides the row and keeps the key, so
        // adding it can never delete a value from a file that already holds one.
        if (HasJsonIgnore(member)) return null;

        SchemaNode node = new()
        {
            Member = member,
            MemberType = memberType,
            Code = JsonPropertyName(member) ?? member.Name,
            Hidden = member.GetCustomAttribute<BrowsableAttribute>()?.Browsable == false,
            ReadOnly = IsReadOnly(member),
            // [Description] first, then the member's own /// doc comment. The attribute was
            // written to be shown; the doc comment was written for a reader of the source and
            // is merely the best thing available when nobody wrote the attribute.
            Comment = member.GetCustomAttribute<DescriptionAttribute>()?.Description
                      ?? XmlDocs.Summary(member),
            Label = ExplicitLabel(member),
            KeySource = member.GetCustomAttribute<DataTypeAttribute>()?.CustomDataType,
            Format = FormatString(member),

            // A member that said nothing sorts after every member that did, which is the
            // convention DataAnnotations itself uses - DisplayAttribute.Order documents 10000
            // as its unset value. Without the offset an explicit Order of 0 merely ties with
            // whatever happened to be declared first.
            //
            // "Declaration order" is GetMembers order, which for a class mixing fields and
            // properties is not quite source order. Config classes are almost always all
            // fields, and this is the same order the walk this replaced used.
            Weight = member.GetCustomAttribute<DisplayAttribute>()?.GetOrder() ?? (UnorderedBase + position[0]++),
        };

        node.Path = prefix.Length == 0 ? node.Code : $"{prefix}/{node.Code}";

        // A [JsonProperty] rename is registered as an alias rather than a move: the member
        // name is what is sitting in every existing file, and re-keying without reading the
        // old name back would silently reset the value to its default.
        if (node.Code != member.Name)
        {
            node.LegacyPaths.Add(prefix.Length == 0 ? member.Name : $"{prefix}/{member.Name}");
        }

        ApplyCategory(member, node, inheritedId: sectionId, inheritedLabel: sectionLabel);

        Classify(node, visiting, depth, notices, position);

        if (CannotBeAssigned(node) && node.Kind is not (SchemaKind.Dictionary or SchemaKind.List))
        {
            node.ReadOnly = true;
        }

        return node;
    }

    /// <summary>
    /// [Category] carries two unrelated things, for reasons of history: a comma-separated
    /// list of flag words that configlib understood, and - as docs/MIGRATING.md has always
    /// claimed - the name of a section to group under. Recognised flag words are consumed as
    /// flags; anything else becomes the section name.
    ///
    /// The section name is taken from the raw text, before the normalisation used to match
    /// flags strips spaces and case. Matching on the normalised form and then *displaying*
    /// it gives you a heading that reads "doors".
    /// </summary>
    private static void ApplyCategory(MemberInfo member, SchemaNode node, string? inheritedId, string? inheritedLabel)
    {
        node.SectionId = inheritedId;
        node.SectionLabel = inheritedLabel;

        string? category = member.GetCustomAttribute<CategoryAttribute>()?.Category;

        // [Display(GroupName)] means the same thing and carries no flag-word baggage.
        DisplayAttribute? display = member.GetCustomAttribute<DisplayAttribute>();
        string? groupName = display?.GetGroupName();
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            node.SectionId = CategoryId(groupName!);
            node.SectionLabel = groupName;
            node.SectionExplicit = true;
        }

        if (category == null) return;

        // The flag words are lifted out and everything else is put back together, commas and
        // all. Taking the last unrecognised piece as the name turned
        // [Category("8 Yours, not the server's, clientside")] into a section called
        // "not the server's" - a name with a comma in it is a name, not a list.
        List<string> rest = [];

        foreach (string raw in category.Split(','))
        {
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;

            switch (trimmed.Replace(" ", "").Replace("_", "").ToLowerInvariant())
            {
                case "clientside": node.ClientSide = true; break;
                case "logarithmic": node.Logarithmic = true; break;
                default: rest.Add(trimmed); break;
            }
        }

        if (rest.Count > 0)
        {
            string named = string.Join(", ", rest);
            node.SectionId = CategoryId(named);
            node.SectionLabel = named;
            node.SectionExplicit = true;
        }
    }

    /// <summary>
    /// Order matters here and is not obvious. An array satisfies IList&lt;T&gt;, and every
    /// dictionary satisfies ICollection&lt;KeyValuePair&lt;,&gt;&gt; - so testing collections
    /// before dictionaries renders every dictionary as a list of pairs. Scalars come first of
    /// all, because string is enumerable.
    /// </summary>
    private static void Classify(SchemaNode node, Stack<Type> visiting, int depth, List<string> notices, int[] position)
    {
        Type type = Nullable.GetUnderlyingType(node.MemberType) ?? node.MemberType;

        ConfigSettingType scalar = ScalarTypeOf(type);
        if (scalar != ConfigSettingType.None)
        {
            node.Kind = SchemaKind.Scalar;
            node.ScalarType = scalar;
            return;
        }

        if (type.IsArray)
        {
            Type? element = type.GetElementType();
            if (element != null)
            {
                BuildContainer(node, SchemaKind.List, element, null, visiting, depth, notices);
                return;
            }
        }

        Type? dictionary = ClosedInterface(type, typeof(IDictionary<,>));
        if (dictionary != null)
        {
            Type[] args = dictionary.GetGenericArguments();
            BuildContainer(node, SchemaKind.Dictionary, args[1], args[0], visiting, depth, notices);
            return;
        }

        Type? collection = ClosedInterface(type, typeof(IList<>))
            ?? ClosedInterface(type, typeof(ICollection<>))
            ?? ClosedInterface(type, typeof(IReadOnlyCollection<>));
        if (collection != null)
        {
            BuildContainer(node, SchemaKind.List, collection.GetGenericArguments()[0], null, visiting, depth, notices);
            return;
        }

        // A type with a real TypeConverter both ways is written by Newtonsoft as a plain
        // string, not as an object with fields - AssetLocation serialises as "game:door-oak".
        // Classifying it as a nested object would flatten it into Domain and Path and break
        // the round trip. Verified against the shipped assembly, not assumed.
        if (ConvertsToAndFromString(type))
        {
            node.Kind = SchemaKind.Scalar;
            node.ScalarType = ConfigSettingType.String;
            return;
        }

        if (type.IsClass || (type.IsValueType && !type.IsPrimitive))
        {
            BuildObject(node, type, visiting, depth, notices, position);
            return;
        }

        node.Kind = SchemaKind.Opaque;
        notices.Add($"'{node.Path}' ({Describe(type)}) has no editor; it is stored and shown as raw JSON.");
    }

    private static void BuildContainer(SchemaNode node, SchemaKind kind, Type valueType, Type? keyType, Stack<Type> visiting, int depth, List<string> notices)
    {
        node.Kind = kind;

        if (keyType != null)
        {
            node.KeyNode = Describe(keyType, $"{node.Path}<key>", visiting, depth, notices);
        }

        SchemaNode value = Describe(valueType, $"{node.Path}<value>", visiting, depth, notices);

        if (kind == SchemaKind.Dictionary) node.ValueNode = value;
        else node.ElementNode = value;

        node.LabelMember = LabelMemberOf(value);

        // A container whose contents we cannot describe is worse than useless as a structured
        // editor - it would offer an Add button that produces something unreadable. Fall back
        // to raw JSON, which at least round-trips.
        if (value.Kind == SchemaKind.Dead)
        {
            node.Kind = SchemaKind.Opaque;
            notices.Add($"'{node.Path}' holds {Describe(valueType)}, which has no editor; it is stored and shown as raw JSON.");
        }
    }

    /// <summary>
    /// Which member of a list element supplies the text on its row. [Key] says "this member
    /// identifies the entity", which is exactly the question - and the fallbacks mean most
    /// authors never have to answer it: the first string member usually is the name.
    /// </summary>
    private static string? LabelMemberOf(SchemaNode element)
    {
        if (element.Kind != SchemaKind.Object) return null;

        SchemaNode? keyed = element.Children.FirstOrDefault(
            child => child.Member?.GetCustomAttribute<KeyAttribute>() != null);
        if (keyed != null) return keyed.Code;

        return element.Children
            .FirstOrDefault(child => child.Kind == SchemaKind.Scalar
                                  && child.ScalarType == ConfigSettingType.String)?.Code;
    }

    private static SchemaNode Describe(Type type, string path, Stack<Type> visiting, int depth, List<string> notices)
    {
        // A container's key and value are shapes, not members, so they take no position.
        SchemaNode node = new() { MemberType = type, Path = path, Code = "" };
        Classify(node, visiting, depth, notices, [0]);

        if (CannotBeAssigned(node) && node.Kind is not (SchemaKind.Dictionary or SchemaKind.List))
        {
            node.ReadOnly = true;
        }

        return node;
    }

    private static void BuildObject(SchemaNode node, Type type, Stack<Type> visiting, int depth, List<string> notices, int[] position)
    {
        // Two members in the published corpus hold their own declaring type - a handle back
        // to the config rather than config data. Walking either is an infinite recursion at
        // registration, so a type already on the stack stops here.
        if (visiting.Contains(type))
        {
            node.Kind = SchemaKind.Dead;
            node.DeadReason = $"{Describe(type)} refers back to itself";
            notices.Add($"'{node.Path}' is a {Describe(type)} inside another {Describe(type)}; skipped as a cycle.");
            return;
        }

        if (depth >= MaxDepth)
        {
            node.Kind = SchemaKind.Dead;
            node.DeadReason = $"nested deeper than {MaxDepth} levels";
            notices.Add($"'{node.Path}' is nested deeper than {MaxDepth} levels; skipped.");
            return;
        }

        // No public constructor means nothing to create when a player adds an entry, and
        // usually means this is not config data at all.
        if (type.IsAbstract || type.IsInterface)
        {
            node.Kind = SchemaKind.Dead;
            node.DeadReason = $"{Describe(type)} is abstract";
            notices.Add($"'{node.Path}' is {Describe(type)}, which cannot be constructed; not editable.");
            return;
        }

        node.Kind = SchemaKind.Object;

        visiting.Push(type);
        try
        {
            foreach (MemberInfo member in Members(type))
            {
                SchemaNode? child = Build(member, node.Path, ChildSectionId(node), ChildSectionLabel(node), visiting, depth + 1, notices, position);
                if (child == null) continue;

                child.Parent = node;
                node.Children.Add(child);
            }
        }
        finally
        {
            visiting.Pop();
        }

        if (node.Children.Count == 0)
        {
            node.Kind = SchemaKind.Opaque;
            notices.Add($"'{node.Path}' ({Describe(type)}) has no public settings; it is stored and shown as raw JSON.");
        }
    }

    /// <summary>
    /// The section a nested object's members belong to. An explicit [Category] on the object
    /// wins outright - the author named the group. Otherwise the object is its own group,
    /// named for the member and prefixed by whatever group it is itself sitting in, so a
    /// leaf three classes down still says where it came from.
    /// </summary>
    internal static string ChildSectionId(SchemaNode node)
        => node.SectionExplicit && node.SectionId != null ? node.SectionId : node.Path;

    internal static string ChildSectionLabel(SchemaNode node)
    {
        if (node.SectionExplicit && node.SectionLabel != null) return node.SectionLabel;

        // An author's [Category] is used as written; a name derived from a member is tidied
        // up the same way a label is, so a heading reads "Rain collector" and not
        // "RainCollector".
        string own = node.Label ?? Humanize(node.Code);
        return node.SectionLabel == null ? own : $"{node.SectionLabel} > {own}";
    }

    /// <summary>
    /// A section an author named. The name is the identity - two classes both saying
    /// [Category("Doors")] are meant to be one section - so it is the id, kept in its own
    /// namespace so it cannot collide with one derived from a member path.
    /// </summary>
    private static string CategoryId(string name) => "cat:" + name;

    // ------------------------------------------------------------------ attributes

    private static string? ExplicitLabel(MemberInfo member)
    {
        string? displayName = member.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName;

        string? name = member.GetCustomAttribute<DisplayAttribute>()?.GetName();
        if (!string.IsNullOrWhiteSpace(name)) return name;

        return null;
    }

    /// <summary>
    /// The display format an author declared, if any.
    ///
    /// [DisplayFormat] also carries ApplyFormatInEditMode, which is false by default and
    /// deliberately not honoured: formatting the text a player types into means parsing it
    /// back out, and "95%" has to survive a round trip through a control that also has to
    /// accept "95" mid-keystroke. The readout beside a slider has no such problem, and that
    /// is where a format earns its keep.
    /// </summary>
    private static string? FormatString(MemberInfo member)
    {
        string? format = member.GetCustomAttribute<DisplayFormatAttribute>()?.DataFormatString;

        return string.IsNullOrWhiteSpace(format) ? null : format;
    }

    private static bool IsReadOnly(MemberInfo member)
        => member.GetCustomAttribute<ReadOnlyAttribute>()?.IsReadOnly == true;

    /// <summary>
    /// A member with no way to assign it. Offering a control for one is theatre: the player
    /// edits it, the value is written to the file, and it never reaches the object.
    ///
    /// A collection is the exception, because it is filled in place rather than replaced -
    /// which is exactly why <c>public List&lt;string&gt; X { get; } = new();</c> works.
    /// </summary>
    private static bool CannotBeAssigned(SchemaNode node) => node.Member switch
    {
        FieldInfo field => field.IsInitOnly,
        PropertyInfo property => !property.CanWrite,
        _ => false
    };

    /// <summary>
    /// Matched by name so both Newtonsoft's and System.Text.Json's attributes are honoured.
    /// These are library types with settled names, not a contract of our own invention.
    /// </summary>
    private static bool HasJsonIgnore(MemberInfo member)
        => member.GetCustomAttributes(inherit: true)
                 .Any(attribute => attribute.GetType().Name == "JsonIgnoreAttribute");

    private static string? JsonPropertyName(MemberInfo member)
    {
        foreach (object attribute in member.GetCustomAttributes(inherit: true))
        {
            Type type = attribute.GetType();
            if (type.Name is not ("JsonPropertyAttribute" or "JsonPropertyNameAttribute")) continue;

            string? name = type.GetProperty("PropertyName")?.GetValue(attribute) as string
                        ?? type.GetProperty("Name")?.GetValue(attribute) as string;

            if (!string.IsNullOrWhiteSpace(name)) return name;
        }

        return null;
    }

    // ------------------------------------------------------------------ types

    /// <summary>
    /// The settings model has one integer type and one floating point type. Every CLR type
    /// that fits in one of them maps to it; ConfigSetting.CoerceTo converts back to whatever
    /// the member actually declared.
    /// </summary>
    internal static ConfigSettingType ScalarTypeOf(Type type)
    {
        // A [Flags] value is a combination - "North, South" - which is not any one member,
        // so the name-to-member mapping a plain enum uses cannot express it and silently
        // stored the first name instead. Its own string form round-trips exactly.
        if (type.IsEnum)
        {
            return type.GetCustomAttribute<FlagsAttribute>() != null
                ? ConfigSettingType.String
                : ConfigSettingType.Integer;
        }
        if (type == typeof(string)) return ConfigSettingType.String;
        if (type == typeof(bool)) return ConfigSettingType.Boolean;

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return ConfigSettingType.Float;
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short) ||
            type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) ||
            type == typeof(byte) || type == typeof(sbyte))
        {
            return ConfigSettingType.Integer;
        }

        return ConfigSettingType.None;
    }

    /// <summary>
    /// True when the type carries a TypeConverter that goes both ways to string. The base
    /// TypeConverter answers true to CanConvertTo(string) for everything, so it is the
    /// CanConvertFrom half that makes this mean anything - and it is the same test Newtonsoft
    /// uses to decide to write the value as a string.
    /// </summary>
    /// <summary>"MaxClientViewDistance" -> "Max client view distance".</summary>
    internal static string Humanize(string code)
    {
        string spaced = System.Text.RegularExpressions.Regex
            .Replace(code.Replace('_', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ")
            .Trim();

        if (spaced.Length == 0) return code;

        return char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
    }

    private static bool ConvertsToAndFromString(Type type)
    {
        if (type == typeof(object)) return false;

        try
        {
            TypeConverter converter = TypeDescriptor.GetConverter(type);
            return converter.CanConvertTo(typeof(string)) && converter.CanConvertFrom(typeof(string));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Type? ClosedInterface(Type type, Type openInterface)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == openInterface) return type;

        return type.GetInterfaces()
                   .FirstOrDefault(candidate => candidate.IsGenericType
                                             && candidate.GetGenericTypeDefinition() == openInterface);
    }

    /// <summary>A type name a mod author will recognise: "Dictionary&lt;string, float&gt;", not the CLR spelling.</summary>
    internal static string Describe(Type type)
    {
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null) return $"{Describe(nullable)}?";

        if (type.IsArray) return $"{Describe(type.GetElementType()!)}[]";

        if (!type.IsGenericType) return Alias(type);

        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick > 0) name = name[..tick];

        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>";
    }

    private static string Alias(Type type)
    {
        if (type == typeof(int)) return "int";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(string)) return "string";
        if (type == typeof(long)) return "long";
        return type.Name;
    }
}
