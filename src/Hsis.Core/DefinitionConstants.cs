namespace Hsis.Core;

public static class DefinitionConstants
{
    public static Dictionary<string, string> Parse(IEnumerable<string> values)
    {
        Dictionary<string, string> constants = new(StringComparer.Ordinal);
        foreach (string value in values)
        {
            string line = value.Trim();
            if (line.Length == 0) continue;
            int separator = line.IndexOf('=');
            string name = separator >= 0 ? line[..separator].Trim() : line;
            string content = separator >= 0 ? line[(separator + 1)..] : "";
            if (name.Length == 0 || name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')) || !constants.TryAdd(name, content))
                throw new InvalidDataException($"定数名が不正か重複しています: {name}");
        }
        return constants;
    }
}
