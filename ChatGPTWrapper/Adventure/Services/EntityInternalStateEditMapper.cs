using System.Reflection;
using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class EntityInternalStatePathAccessor
{
    public static bool TryGetDisplayValue(
        object root,
        string path,
        EntityInternalStateFieldKind kind,
        out string display)
    {
        display = "";
        if (!TryResolve(root, path, out var target, out var leaf))
            return false;

        display = kind switch
        {
            EntityInternalStateFieldKind.Bool => target is bool b ? (b ? "true" : "false") : "false",
            EntityInternalStateFieldKind.StringList => FormatList(target as IList<string>),
            EntityInternalStateFieldKind.StringDictionary => FormatDictionary(target),
            _ => target?.ToString() ?? "",
        };
        return true;
    }

    public static bool TrySetDisplayValue(
        object root,
        string path,
        EntityInternalStateFieldKind kind,
        string display)
    {
        if (!TryResolveParent(root, path, out var parent, out var leaf))
            return false;

        var prop = GetProperty(parent!.GetType(), leaf!);
        if (prop is null)
            return false;

        switch (kind)
        {
            case EntityInternalStateFieldKind.Bool:
                prop.SetValue(parent, string.Equals(display, "true", StringComparison.OrdinalIgnoreCase));
                return true;
            case EntityInternalStateFieldKind.StringList:
                prop.SetValue(parent, ParseList(display));
                return true;
            case EntityInternalStateFieldKind.StringDictionary:
                prop.SetValue(parent, ParseDictionary(display, prop.PropertyType));
                return true;
            default:
                prop.SetValue(parent, display ?? "");
                return true;
        }
    }

    private static string FormatList(IList<string>? items) =>
        items is null || items.Count == 0 ? "" : string.Join(Environment.NewLine, items);

    private static string FormatDictionary(object? value)
    {
        switch (value)
        {
            case Dictionary<string, string> sDict:
                return string.Join(Environment.NewLine, sDict.Select(kv => $"{kv.Key}: {kv.Value}"));
            case Dictionary<string, bool> bDict:
                return string.Join(Environment.NewLine, bDict.Select(kv => $"{kv.Key}: {kv.Value}"));
            case Dictionary<string, int> iDict:
                return string.Join(Environment.NewLine, iDict.Select(kv => $"{kv.Key}: {kv.Value}"));
            default:
                return "";
        }
    }

    private static List<string> ParseList(string display) =>
        display.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    private static object ParseDictionary(string display, Type targetType)
    {
        if (targetType == typeof(Dictionary<string, bool>))
        {
            var dict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in ParseDictionaryLines(display))
            {
                if (bool.TryParse(value, out var b))
                    dict[key] = b;
            }

            return dict;
        }

        if (targetType == typeof(Dictionary<string, int>))
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in ParseDictionaryLines(display))
            {
                if (int.TryParse(value, out var n))
                    dict[key] = n;
            }

            return dict;
        }

        var sDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ParseDictionaryLines(display))
            sDict[key] = value;
        return sDict;
    }

    private static IEnumerable<(string Key, string Value)> ParseDictionaryLines(string display)
    {
        foreach (var line in display.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            var idx = trimmed.IndexOf(':');
            if (idx <= 0)
                continue;

            yield return (trimmed[..idx].Trim(), trimmed[(idx + 1)..].Trim());
        }
    }

    private static bool TryResolve(object root, string path, out object? target, out string? leaf)
    {
        target = root;
        leaf = null;
        var parts = path.Split('.');
        for (var i = 0; i < parts.Length; i++)
        {
            if (target is null)
                return false;

            if (i == parts.Length - 1)
            {
                leaf = parts[i];
                var prop = GetProperty(target.GetType(), parts[i]);
                target = prop?.GetValue(target);
                return prop is not null;
            }

            var next = GetProperty(target.GetType(), parts[i]);
            if (next is null)
                return false;
            target = next.GetValue(target);
        }

        return false;
    }

    private static bool TryResolveParent(object root, string path, out object? parent, out string? leaf)
    {
        parent = root;
        leaf = null;
        var parts = path.Split('.');
        if (parts.Length == 1)
        {
            leaf = parts[0];
            return GetProperty(root.GetType(), parts[0]) is not null;
        }

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parent is null)
                return false;
            var prop = GetProperty(parent.GetType(), parts[i]);
            if (prop is null)
                return false;
            parent = prop.GetValue(parent);
        }

        leaf = parts[^1];
        return parent is not null && GetProperty(parent.GetType(), leaf) is not null;
    }

    private static PropertyInfo? GetProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
}

public static class EntityInternalStateEditMapper
{
    public static EntityInternalStateEditModel Load(
        AdventureBundle bundle,
        Guid entityId,
        string kindId,
        string entityName) =>
        EntityInternalStateSchema.LoadModelInner(bundle, entityId, kindId, entityName);

    public static void Apply(AdventureBundle bundle, EntityInternalStateEditModel model)
    {
        var record = EntityInternalStateService.GetOrCreate(bundle, model.KindId, model.EntityId, seedFromCanon: false);
        EntityInternalStateSchema.ApplyModel(model, record, model.KindId);
        EntityInternalStateService.Upsert(bundle, record);
    }

    public static bool HasChanges(EntityInternalStateEditModel model) =>
        EntityInternalStateSchema.HasChanges(model);
}
