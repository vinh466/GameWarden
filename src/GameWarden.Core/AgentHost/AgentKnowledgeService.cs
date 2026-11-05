using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace GameWarden.Core.AgentHost;

public sealed record AgentKnowledgeHit(string Source, string Title, string Snippet, int Score);

/// <summary>
/// Small, local knowledge search over the operator docs, adapter guides and runbooks shipped with Core
/// (docs/21 §2.2). It is deliberately lexical: no embedding service, no network, and every answer can cite a source.
/// </summary>
public sealed partial class AgentKnowledgeService(
    IOptions<GameWardenOptions> options,
    ILogger<AgentKnowledgeService> logger)
{
    private IReadOnlyList<KnowledgeSection>? _sections;

    public IReadOnlyList<AgentKnowledgeHit> Search(string query, int limit)
    {
        var terms = Terms(query);
        if (terms.Count == 0)
        {
            return [];
        }

        var sections = Sections();
        return sections
            .Select(section => new { section, score = Score(section, terms) })
            .Where(row => row.score > 0)
            .OrderByDescending(row => row.score)
            .ThenBy(row => row.section.Source, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 10))
            .Select(row => new AgentKnowledgeHit(
                row.section.Source,
                row.section.Title,
                Snippet(row.section.Text, terms),
                row.score))
            .ToList();
    }

    public IReadOnlyList<AgentKnowledgeHit> Search(string query) => Search(query, 5);

    private static List<string> Terms(string query) =>
        [.. query.ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '?', '!', '(', ')', '[', ']', '/', '\\', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .Take(12)];

    private static int Score(KnowledgeSection section, IReadOnlyList<string> terms)
    {
        var score = 0;
        foreach (var term in terms)
        {
            score += Count(section.Title, term) * 5;
            score += Count(section.Text, term);
        }

        return score;
    }

    private static int Count(string text, string term)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += term.Length;
        }

        return count;
    }

    private static string Snippet(string text, IReadOnlyList<string> terms)
    {
        var index = terms
            .Select(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase))
            .Where(found => found >= 0)
            .DefaultIfEmpty(0)
            .Min();
        var start = Math.Max(0, index - 140);
        var length = Math.Min(600, text.Length - start);
        var snippet = text.Substring(start, length).Trim();
        return start > 0 ? "…" + snippet : snippet;
    }

    private IReadOnlyList<KnowledgeSection> Sections()
    {
        if (_sections is not null)
        {
            return _sections;
        }

        var sections = new List<KnowledgeSection>();
        foreach (var folder in new[] { Path.Combine(AppContext.BaseDirectory, "knowledge"), Path.Combine(options.Value.DataRoot, "knowledge") })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.md"))
            {
                ReadMarkdown(file, $"docs/{Path.GetFileName(file)}", sections);
            }
        }

        foreach (var folder in new[] { Path.Combine(AppContext.BaseDirectory, "runbooks"), Path.Combine(options.Value.DataRoot, "runbooks") })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.y*ml"))
            {
                try
                {
                    sections.Add(new KnowledgeSection($"runbook/{Path.GetFileName(file)}", Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Could not read knowledge runbook {File}.", file);
                }
            }
        }

        foreach (var adapter in new[] { Path.Combine(AppContext.BaseDirectory, "adapters-builtin"), Path.Combine(options.Value.DataRoot, "adapters") })
        {
            if (!Directory.Exists(adapter))
            {
                continue;
            }

            foreach (var guide in Directory.EnumerateFiles(adapter, "guide.md", SearchOption.AllDirectories))
            {
                ReadMarkdown(guide, $"adapter/{Path.GetFileName(Path.GetDirectoryName(guide))}", sections);
            }

            foreach (var help in Directory.EnumerateFiles(adapter, "help.json", SearchOption.AllDirectories))
            {
                try
                {
                    sections.Add(new KnowledgeSection($"adapter/{Path.GetFileName(Path.GetDirectoryName(help))}", "Adapter help", File.ReadAllText(help)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Could not read adapter help {File}.", help);
                }
            }
        }

        _sections = sections;
        logger.LogInformation("Loaded {Count} Agent knowledge section(s).", sections.Count);
        return _sections;
    }

    private static void ReadMarkdown(string file, string source, List<KnowledgeSection> sections)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var matches = Heading().Matches(text);
        if (matches.Count == 0)
        {
            sections.Add(new KnowledgeSection(source, Path.GetFileNameWithoutExtension(file), text));
            return;
        }

        for (var index = 0; index < matches.Count; index++)
        {
            var heading = matches[index];
            var start = heading.Index + heading.Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : text.Length;
            var title = heading.Groups[1].Value.Trim();
            var body = text[start..end].Trim();
            if (body.Length > 0)
            {
                sections.Add(new KnowledgeSection(source, title, body));
            }
        }
    }

    private sealed record KnowledgeSection(string Source, string Title, string Text);

    [GeneratedRegex(@"^#{1,3}\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex Heading();
}
