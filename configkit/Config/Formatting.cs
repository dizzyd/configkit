// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.
//
// Derived from ConfigLib by Maltiez (https://github.com/maltiez2/vsmod_configlib),
// released under CC0 1.0 Universal. Adapted to drop the Dear ImGui dependency.

using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ConfigKit.Formatting;

public interface IConfigBlock
{
}

internal interface IFormattingBlock : IConfigBlock
{
    float SortingWeight { get; }
    public bool Collapsible { get; }
    public bool StopCollapsible { get; }
    string Yaml { get; }

    /// <summary>Heading for this block, or null for a plain rule.</summary>
    string Title { get; }
    /// <summary>Explanatory paragraph shown under the heading, or null.</summary>
    string Text { get; }
    /// <summary>URL offered as a button, or null.</summary>
    string Link { get; }
    /// <summary>Label for <see cref="Link"/>; falls back to the URL itself.</summary>
    string LinkText { get; }
}

internal sealed class Blank : IFormattingBlock
{
    public float SortingWeight => 0;
    public bool Collapsible => false;
    public bool StopCollapsible => false;
    public string Yaml => "";

    public string Title => null;
    public string Text => null;
    public string Link => null;
    public string LinkText => null;
}

internal sealed class Separator : IFormattingBlock
{
    public Separator(JsonObject definition, string domain, ICoreAPI api)
    {
        _weight = definition["weight"].AsFloat(0);
        _collapsible = definition["collapsible"].AsBool(false);
        _stopCollapsible = _collapsible;
        _weight = _weight < 0 ? 0 : _weight;
        StringBuilder yaml = new();
        yaml.Append("\n\n");

        if (definition.KeyExists("title"))
        {
            _stopCollapsible = true;
            string title = Localize(definition["title"].AsString(), domain);
            _title = title;
            int width = title.Length + 6;
            string line = new('#', width);
            yaml.Append($"{line}\n## {title} ##\n{line}\n");
        }

        if (definition.KeyExists("text"))
        {
            string text = Localize(definition["text"].AsString(), domain);
            _text = text;
            string[] lines = text.Split('\n');
            string composed = lines.Select(line => $"# {line}").Aggregate((first, second) => $"{first}\n{second}");
            yaml.Append($"{composed}\n");
        }

        if (definition.KeyExists("link"))
        {
            _link = definition["link"].AsString();
            _linkText = definition["linkText"].AsString(null);
            if (_linkText != null) _linkText = Localize(_linkText, domain);
            yaml.Append($"# {_link}\n");
        }

        _yaml = yaml.ToString();
    }

    public string Yaml => _yaml;
    public float SortingWeight => _weight;
    public bool Collapsible => _collapsible;
    public bool StopCollapsible => _stopCollapsible;

    public string Title => _title;
    public string Text => _text;
    public string Link => _link;
    public string LinkText => _linkText ?? _link;


    private readonly string _yaml;
    private readonly float _weight;
    private readonly bool _collapsible;
    private readonly bool _stopCollapsible;
    private readonly string? _title;
    private readonly string? _text;
    private readonly string? _link;
    private readonly string? _linkText;

    private static string Localize(string value, string domain)
    {
        bool hasDomain = value.Contains(':');
        string langCode = hasDomain ? value : $"{domain}:{value}";
        return Lang.HasTranslation(langCode) ? Lang.Get(langCode) : value;
    }
}