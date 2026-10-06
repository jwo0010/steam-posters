using System.Runtime.CompilerServices;
using SteamPosters.Artwork;
using SteamPosters.Core.Steam;

namespace SteamPosters.Matching;

public enum MatchConfidence
{
    /// <summary>No usable result; the user has to search by hand (or untick the entry).</summary>
    NotFound,
    /// <summary>A plausible result, or several close ones; the user should confirm.</summary>
    CheckThis,
    /// <summary>A clear, close match.</summary>
    Matched,
}

/// <summary>A provider game, its score, and the search term it was scored against.</summary>
public sealed record MatchCandidate(ProviderGame Game, double Score, SearchTerm Term);

public sealed record MatchResult(
    string ShortcutIndex,
    uint AppId,
    string AppName,
    MatchConfidence Confidence,
    MatchCandidate? Match,
    IReadOnlyList<MatchCandidate> Alternatives,
    IReadOnlyList<SearchTerm> Terms,
    bool IsEmulatorOrTool,
    string? Reason)
{
    public ProviderGame? Game => Match?.Game;

    public double Score => Match?.Score ?? 0;

    public SearchTerm? MatchedTerm => Match?.Term;

    /// <summary>The matched game's name, offered as the new Steam display name.</summary>
    public string? SuggestedName => Confidence == MatchConfidence.NotFound ? null : Match?.Game.Name;
}

public sealed class MatchOptions
{
    /// <summary>At or above this score (and clear of the runner-up) a match counts as <see cref="MatchConfidence.Matched"/>.</summary>
    public double MatchedScore { get; init; } = 0.85;

    /// <summary>The best must beat a differently named runner-up by this much to count as matched.</summary>
    public double MatchedMargin { get; init; } = 0.05;

    /// <summary>At or above this score a match is offered as <see cref="MatchConfidence.CheckThis"/>.</summary>
    public double CheckScore { get; init; } = 0.5;

    /// <summary>Stop searching further terms once a result scores this high.</summary>
    public double StopScore { get; init; } = 0.95;

    /// <summary>Most provider searches per game.</summary>
    public int MaxSearches { get; init; } = 4;

    public int MaxAlternatives { get; init; } = 5;

    /// <summary>Small boost for games the provider marks as verified.</summary>
    public double VerifiedBonus { get; init; } = 0.03;
}

/// <summary>Guesses which real game each non-Steam shortcut is, using an artwork provider's search.</summary>
public sealed class MatchService(IArtworkProvider provider, MatchOptions? options = null)
{
    public const string EmulatorReason = "emulator, no game in launch options";

    private readonly MatchOptions _options = options ?? new MatchOptions();

    /// <summary>Matches shortcuts one at a time, yielding each result as soon as it's ready (for progress UI).</summary>
    public async IAsyncEnumerable<MatchResult> MatchAllAsync(
        IEnumerable<Shortcut> shortcuts, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var shortcut in shortcuts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await MatchAsync(shortcut, cancellationToken);
        }
    }

    public Task<MatchResult> MatchAsync(Shortcut shortcut, CancellationToken cancellationToken = default) =>
        MatchAsync(shortcut.Index, shortcut.AppId, shortcut.AppName, CandidateExtractor.Extract(shortcut), cancellationToken);

    public Task<MatchResult> MatchAsync(string appName, string exe, string? launchOptions = null, CancellationToken cancellationToken = default) =>
        MatchAsync("", 0, appName, CandidateExtractor.Extract(appName, exe, launchOptions), cancellationToken);

    private async Task<MatchResult> MatchAsync(
        string index, uint appId, string appName, Candidates candidates, CancellationToken cancellationToken)
    {
        var terms = candidates.Terms;
        if (terms.Count == 0)
        {
            var reason = candidates.IsEmulatorOrTool ? EmulatorReason : "no usable name";
            return new MatchResult(index, appId, appName, MatchConfidence.NotFound, null, [], terms, candidates.IsEmulatorOrTool, reason);
        }

        // Search-only terms widen the search but never score: a shortened term would match the wrong game exactly.
        var scoringTerms = terms.Where(t => t.Source != TermSource.SearchOnly).ToList();
        var best = new Dictionary<string, MatchCandidate>();
        foreach (var term in terms.Take(_options.MaxSearches))
        {
            var games = await provider.SearchGamesAsync(term.Text, cancellationToken);
            foreach (var game in games)
            {
                // Score against every term: a folder search can find what the app name describes best.
                var scored = scoringTerms.Select(t => new MatchCandidate(game, Score(t.Text, game), t)).MaxBy(c => c.Score)!;
                if (!best.TryGetValue(game.Id, out var existing) || scored.Score > existing.Score)
                    best[game.Id] = scored;
            }
            if (best.Values.Any(c => c.Score >= _options.StopScore)) break;
        }

        var ranked = best.Values.OrderByDescending(c => c.Score).ThenByDescending(c => c.Game.Verified).ToList();
        var confidence = Confidence(ranked);
        var match = confidence == MatchConfidence.NotFound ? null : ranked.FirstOrDefault();
        var alternatives = ranked.Where(c => c != match).Take(_options.MaxAlternatives).ToList();
        var why = confidence == MatchConfidence.NotFound ? (ranked.Count == 0 ? "no search results" : "no close result") : null;
        return new MatchResult(index, appId, appName, confidence, match, alternatives, terms, candidates.IsEmulatorOrTool, why);
    }

    private double Score(string term, ProviderGame game)
    {
        var score = NameSimilarity.Score(term, game.Name);
        if (game.Verified) score += _options.VerifiedBonus;
        return Math.Min(score, 1);
    }

    private MatchConfidence Confidence(List<MatchCandidate> ranked)
    {
        if (ranked.Count == 0 || ranked[0].Score < _options.CheckScore) return MatchConfidence.NotFound;
        var top = ranked[0];
        var runnerUp = ranked.Skip(1).FirstOrDefault(c =>
            !string.Equals(c.Game.Name, top.Game.Name, StringComparison.OrdinalIgnoreCase));
        var clear = runnerUp is null || top.Score - runnerUp.Score >= _options.MatchedMargin;
        return top.Score >= _options.MatchedScore && clear ? MatchConfidence.Matched : MatchConfidence.CheckThis;
    }
}
