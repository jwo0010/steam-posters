using System.Text;

namespace SteamPosters.Core.Vdf;

/// <summary>A node of a text VDF file: string values and child objects, keys case-insensitive.</summary>
public sealed class TextVdfObject
{
    private readonly List<KeyValuePair<string, object>> _items = new();

    public IEnumerable<KeyValuePair<string, object>> Items => _items;

    public IEnumerable<KeyValuePair<string, TextVdfObject>> Children =>
        _items.Where(i => i.Value is TextVdfObject)
              .Select(i => new KeyValuePair<string, TextVdfObject>(i.Key, (TextVdfObject)i.Value));

    public string? GetString(string key) =>
        _items.FirstOrDefault(i => i.Value is string && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)).Value as string;

    public TextVdfObject? GetObject(string key) =>
        _items.FirstOrDefault(i => i.Value is TextVdfObject && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)).Value as TextVdfObject;

    internal void Add(string key, object value) => _items.Add(new(key, value));
}

/// <summary>Reader for Valve's text KeyValues format (e.g. loginusers.vdf). Read-only.</summary>
public static class TextVdf
{
    public static TextVdfObject Parse(string text)
    {
        var tokens = Tokenize(text).GetEnumerator();
        var root = new TextVdfObject();
        ParseInto(root, tokens, isRoot: true);
        return root;
    }

    public static TextVdfObject Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));

    private static void ParseInto(TextVdfObject target, IEnumerator<Token> tokens, bool isRoot)
    {
        while (tokens.MoveNext())
        {
            var token = tokens.Current;
            if (token.Kind == TokenKind.Close)
            {
                if (isRoot) throw new FormatException("Unexpected '}'.");
                return;
            }
            if (token.Kind == TokenKind.Open) throw new FormatException("Unexpected '{' where a key was expected.");

            var key = token.Text;
            if (!tokens.MoveNext()) throw new FormatException($"Missing value for key '{key}'.");
            var value = tokens.Current;
            if (value.Kind == TokenKind.Open)
            {
                var child = new TextVdfObject();
                ParseInto(child, tokens, isRoot: false);
                target.Add(key, child);
            }
            else if (value.Kind == TokenKind.String)
            {
                target.Add(key, value.Text);
            }
            else
            {
                throw new FormatException($"Unexpected '}}' after key '{key}'.");
            }
        }
        if (!isRoot) throw new FormatException("Missing '}' at end of file.");
    }

    private enum TokenKind { String, Open, Close }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static IEnumerable<Token> Tokenize(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '[')
            {
                // Conditional such as [$WIN32]; ignored.
                while (i < text.Length && text[i] != ']') i++;
                i++;
                continue;
            }
            if (c == '{') { i++; yield return new Token(TokenKind.Open, "{"); continue; }
            if (c == '}') { i++; yield return new Token(TokenKind.Close, "}"); continue; }

            var sb = new StringBuilder();
            if (c == '"')
            {
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        sb.Append(text[i] switch { 'n' => '\n', 't' => '\t', _ => text[i] });
                    }
                    else
                    {
                        sb.Append(text[i]);
                    }
                    i++;
                }
                if (i >= text.Length) throw new FormatException("Unterminated quoted string.");
                i++;
            }
            else
            {
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"'))
                    sb.Append(text[i++]);
            }
            yield return new Token(TokenKind.String, sb.ToString());
        }
    }
}
