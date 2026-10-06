using System.Globalization;
using System.Text;

namespace SteamPosters.Matching;

/// <summary>
/// Fuzzy similarity between a search term and a provider's game name, 0 (unrelated) to 1 (same).
/// Normalizes case, accents, punctuation, "&amp;", roman numerals and a leading "The", expands
/// acronyms found in the candidate ("TotK" -> "Tears of the Kingdom"), then takes the better of
/// a word overlap score and a character bigram score, scaled down by term words the candidate lacks.
/// </summary>
public static class NameSimilarity
{
    private static readonly Dictionary<string, string> RomanNumerals = new()
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10",
    };

    public static double Score(string term, string candidate)
    {
        var a = Tokens(term);
        var b = Tokens(candidate);
        if (a.Count == 0 || b.Count == 0) return 0;
        if (a.SequenceEqual(b)) return 1;

        a = ExpandAcronyms(a, b);
        if (a.SequenceEqual(b)) return 0.99;

        // Words of the term missing from the candidate count against it, so "The Legend of Zelda"
        // can't look like a strong match for "The Legend of Zelda TotK".
        var coverage = (double)a.Count(b.Contains) / a.Count;
        var overlap = Math.Max(WordDice(a, b), BigramDice(string.Concat(a), string.Concat(b)));
        return overlap * (0.5 + 0.5 * coverage);
    }

    /// <summary>Lowercase words without accents or punctuation; numerals as digits; no leading "the".</summary>
    public static List<string> Tokens(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is '\'' or '’') continue;                     // Assassin's -> assassins
            if (c == '&') { sb.Append(" and "); continue; }
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        var words = NameCleaner.SplitCamelCase(sb.ToString())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => RomanNumerals.GetValueOrDefault(w, w))
            .ToList();
        if (words.Count > 1 && words[0] == "the") words.RemoveAt(0);
        return words;
    }

    /// <summary>Replaces a term word like "totk" with the candidate's words whose initials spell it.</summary>
    private static List<string> ExpandAcronyms(List<string> term, List<string> candidate)
    {
        var result = new List<string>();
        foreach (var word in term)
        {
            if (word.Length is >= 2 and <= 6 && word.All(char.IsLetter) && !candidate.Contains(word)
                && FindInitials(word, candidate) is { } run)
                result.AddRange(run);
            else
                result.Add(word);
        }
        return result;
    }

    private static List<string>? FindInitials(string acronym, List<string> words)
    {
        for (var start = 0; start + acronym.Length <= words.Count; start++)
        {
            var run = words.GetRange(start, acronym.Length);
            if (run.Select(w => w[0]).SequenceEqual(acronym)) return run;
        }
        return null;
    }

    private static double WordDice(List<string> a, List<string> b)
    {
        var remaining = new List<string>(b);
        var common = 0;
        foreach (var word in a)
        {
            if (remaining.Remove(word)) common++;
        }
        return 2.0 * common / (a.Count + b.Count);
    }

    private static double BigramDice(string a, string b)
    {
        if (a.Length < 2 || b.Length < 2) return a == b ? 1 : 0;
        var bigrams = new List<string>();
        for (var i = 0; i < b.Length - 1; i++) bigrams.Add(b.Substring(i, 2));
        var total = a.Length - 1 + bigrams.Count;
        var common = 0;
        for (var i = 0; i < a.Length - 1; i++)
        {
            if (bigrams.Remove(a.Substring(i, 2))) common++;
        }
        return 2.0 * common / total;
    }
}
