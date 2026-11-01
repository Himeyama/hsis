namespace Hsis.Core;

public static class DefinitionConstants
{
    public static string AddMissing(string text, IEnumerable<string> names)
    {
        Dictionary<string, string> existing = Parse(text.Split('\n'));
        List<string> missing = names.Where(name => !existing.ContainsKey(name)).Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count == 0) return text;
        string separator = text.Length > 0 && !text.EndsWith('\n') && !text.EndsWith('\r') ? Environment.NewLine : "";
        return text + separator + string.Join(Environment.NewLine, missing.Select(name => name + "="));
    }

    public static Dictionary<string, string> ParseValues(string text) => Parse(text.Split('\n'))
        .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    public static Dictionary<string, string> Parse(IEnumerable<string> values)
    {
        Dictionary<string, string> constants = new(StringComparer.Ordinal);
        foreach (string value in values.SelectMany(value => value.Split(['\r', '\n'], StringSplitOptions.None)))
        {
            string line = value.Trim();
            if (line.Length == 0) continue;
            int separator = line.IndexOf('=');
            string name = separator >= 0 ? line[..separator].Trim() : line;
            string content = separator >= 0 ? line[(separator + 1)..] : "";
            content = content.Trim();
            if (content.Length >= 2 && (content[0] == '"' && content[^1] == '"' || content[0] == '\'' && content[^1] == '\'')) content = content[1..^1];
            if (name.Length == 0 || name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')) || !constants.TryAdd(name, content))
                throw new InvalidDataException($"定数名が不正か重複しています: {name}");
        }
        return constants;
    }
}
