using System.Text.RegularExpressions;
using CjmApplyAssistant.Models;

namespace CjmApplyAssistant.Parsing;

/// <summary>
/// Best-effort parser for LinkedIn job-card paste text.
/// LI card layouts vary; fields are filled when recognizable and left blank otherwise.
/// </summary>
public static partial class LinkedInPasteParser
{
    private static readonly string[] NoiseExact =
    [
        "•",
        "·",
        "Easy Apply",
        "Be an early applicant",
        "Actively reviewing applicants",
        "Promoted",
        "Verified job",
    ];

    public static IReadOnlyList<JobRecord> Parse(string paste)
    {
        if (string.IsNullOrWhiteSpace(paste))
            return [];

        var normalized = paste.Replace("\r\n", "\n").Replace('\r', '\n');
        var blocks = SplitIntoBlocks(normalized);
        var results = new List<JobRecord>();

        foreach (var block in blocks)
        {
            var job = ParseBlock(block);
            if (job is null)
                continue;

            job.JobKey = JobKeyBuilder.Build(job.JobTitle, job.Company, job.Location);
            if (string.IsNullOrWhiteSpace(job.JobKey))
                continue;

            results.Add(job);
        }

        return results;
    }

    private static List<string> SplitIntoBlocks(string text)
    {
        // LinkedIn cards themselves contain blank lines between fields, so blank-line
        // splitting is wrong. Use an explicit delimiter, or start a new block when a
        // later line looks like another verified/title card after a prior company line.
        var explicitParts = ExplicitJobSplit()
            .Split(text)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (explicitParts.Count > 1)
            return explicitParts;

        return SplitByCardStarts(text.Trim());
    }

    private static List<string> SplitByCardStarts(string text)
    {
        var lines = text.Split('\n');
        var starts = new List<int> { 0 };

        for (var i = 1; i < lines.Length; i++)
        {
            var cleaned = CleanLine(lines[i]);
            if (cleaned.Length == 0 || IsNoiseLine(cleaned) || IsFlagLine(cleaned))
                continue;

            // A new card often begins with "...(Verified job)..." after earlier content.
            if (!VerifiedJobConcat().IsMatch(cleaned) && !TitleWithViewed().IsMatch(cleaned))
                continue;

            // Only treat as a new card if we've already seen a plausible company/location above.
            var prior = string.Join('\n', lines.Take(i));
            if (!LooksLikeCompletedCardPrefix(prior))
                continue;

            starts.Add(i);
        }

        if (starts.Count == 1)
            return [text];

        var blocks = new List<string>();
        for (var s = 0; s < starts.Count; s++)
        {
            var from = starts[s];
            var to = s + 1 < starts.Count ? starts[s + 1] : lines.Length;
            var block = string.Join('\n', lines.Skip(from).Take(to - from)).Trim();
            if (block.Length > 0)
                blocks.Add(block);
        }

        return blocks;
    }

    private static bool LooksLikeCompletedCardPrefix(string prior)
    {
        var lines = prior
            .Split('\n')
            .Select(CleanLine)
            .Where(l => l.Length > 0)
            .Where(l => !IsNoiseLine(l))
            .ToList();

        // Need at least title + one more substantive line (company or location).
        return lines.Count >= 2 &&
               lines.Skip(1).Any(l => !IsFlagLine(l) && !LooksLikeCompensation(l) && !LooksLikeBenefits(l));
    }

    private static JobRecord? ParseBlock(string block)
    {
        var lines = block
            .Split('\n')
            .Select(l => CleanLine(l))
            .Where(l => l.Length > 0)
            .Where(l => !IsNoiseLine(l))
            .ToList();

        if (lines.Count == 0)
            return null;

        var fullText = string.Join("\n", lines);
        var viewed = ContainsViewed(fullText);
        var topApplicant = ContainsTopApplicant(fullText);

        var title = ExtractTitle(lines);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var company = ExtractCompany(lines, title);
        var location = ExtractLocation(lines, title, company);
        var compensation = ExtractCompensation(lines);
        var (postedText, postedDate) = ExtractPosted(lines);

        return new JobRecord
        {
            JobTitle = title,
            Company = company ?? "",
            Location = location,
            Viewed = viewed,
            IsTopApplicant = topApplicant,
            CompensationText = compensation,
            PostedDateText = postedText,
            PostedDate = postedDate,
            RawPaste = block.Trim(),
        };
    }

    private static string ExtractTitle(IReadOnlyList<string> lines)
    {
        var first = lines[0];

        // "Title (Verified job)Title, Viewed" — common LI card quirk
        var verifiedSplit = VerifiedJobConcat().Match(first);
        if (verifiedSplit.Success)
            return CleanTitle(verifiedSplit.Groups["title"].Value);

        // "Title, Viewed" on one line
        var withViewed = TitleWithViewed().Match(first);
        if (withViewed.Success)
            return CleanTitle(withViewed.Groups["title"].Value);

        return CleanTitle(first);
    }

    private static string? ExtractCompany(IReadOnlyList<string> lines, string title)
    {
        foreach (var line in lines.Skip(1))
        {
            if (LooksLikeTitleEcho(line, title))
                continue;
            if (LooksLikeLocation(line))
                continue;
            if (LooksLikeCompensation(line))
                continue;
            if (LooksLikePosted(line))
                continue;
            if (LooksLikeBenefits(line))
                continue;
            if (IsFlagLine(line))
                continue;

            // Company names are usually short-ish single lines without $ or remote tags.
            return line.Trim();
        }

        return null;
    }

    private static string? ExtractLocation(IReadOnlyList<string> lines, string title, string? company)
    {
        foreach (var line in lines)
        {
            if (string.Equals(line, title, StringComparison.OrdinalIgnoreCase))
                continue;
            if (company is not null && string.Equals(line, company, StringComparison.OrdinalIgnoreCase))
                continue;
            if (LooksLikeLocation(line))
                return line;
        }

        return null;
    }

    private static string? ExtractCompensation(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (LooksLikeCompensation(line))
                return line;
        }

        return null;
    }

    private static (string? Text, DateOnly? Date) ExtractPosted(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
        {
            if (!LooksLikePosted(line))
                continue;

            var text = line.Trim();
            return (text, TryResolveRelativeDate(text, DateTime.Today));
        }

        return (null, null);
    }

    public static DateOnly? TryResolveRelativeDate(string? text, DateTime today)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var t = text.Trim().ToLowerInvariant();

        if (t is "today" or "just now")
            return DateOnly.FromDateTime(today);

        if (t == "yesterday")
            return DateOnly.FromDateTime(today.AddDays(-1));

        var match = RelativePosted().Match(t);
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups["n"].Value, out var n) || n < 0)
            return null;

        var unit = match.Groups["unit"].Value;
        var days = unit switch
        {
            "minute" or "minutes" or "hour" or "hours" => 0,
            "day" or "days" => n,
            "week" or "weeks" => n * 7,
            "month" or "months" => n * 30,
            "year" or "years" => n * 365,
            _ => -1,
        };

        if (days < 0)
            return null;

        return DateOnly.FromDateTime(today.AddDays(-days));
    }

    private static string CleanTitle(string title)
    {
        title = title.Trim();
        title = StripVerified().Replace(title, string.Empty).Trim();
        title = TrailingViewed().Replace(title, string.Empty).Trim();
        title = TrailingComma().Replace(title, string.Empty).Trim();
        return title;
    }

    private static string CleanLine(string line)
    {
        var t = line.Trim();
        t = t.Replace('\u00a0', ' ');
        t = MultiSpace().Replace(t, " ");
        return t.Trim();
    }

    private static bool IsNoiseLine(string line)
    {
        if (NoiseExact.Any(n => string.Equals(n, line, StringComparison.OrdinalIgnoreCase)))
            return true;

        return line is "•" or "·" or "-" or "–" or "—";
    }

    private static bool IsFlagLine(string line)
    {
        var t = line.ToLowerInvariant();
        return t is "viewed"
            || t.Contains("you'd be a top applicant", StringComparison.Ordinal)
            || t.Contains("you’d be a top applicant", StringComparison.Ordinal)
            || t.Contains("be an early applicant", StringComparison.Ordinal)
            || t.Contains("actively reviewing", StringComparison.Ordinal)
            || t.Contains("easy apply", StringComparison.Ordinal);
    }

    private static bool ContainsViewed(string text) =>
        ViewedWord().IsMatch(text);

    private static bool ContainsTopApplicant(string text) =>
        TopApplicant().IsMatch(text);

    private static bool LooksLikeTitleEcho(string line, string title)
    {
        var cleaned = CleanTitle(line);
        return string.Equals(cleaned, title, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeLocation(string line)
    {
        if (LooksLikeCompensation(line) || LooksLikePosted(line) || IsFlagLine(line))
            return false;

        var t = line.ToLowerInvariant();
        if (t.Contains("remote") || t.Contains("hybrid") || t.Contains("on-site") || t.Contains("onsite"))
            return true;

        if (t.Contains("united states") || t.Contains("united kingdom") || t.EndsWith(", usa"))
            return true;

        // City, ST
        if (CityState().IsMatch(line))
            return true;

        // "Area" style: "New York City Metropolitan Area"
        if (t.Contains(" area") && line.Length < 80)
            return true;

        return false;
    }

    private static bool LooksLikeCompensation(string line)
    {
        if (SalaryRange().IsMatch(line))
            return true;

        // Sometimes appears without $: "120K/yr - 150K/yr"
        return CompWithoutDollar().IsMatch(line);
    }

    private static bool LooksLikePosted(string line)
    {
        var t = line.ToLowerInvariant();
        if (t.StartsWith("reposted ", StringComparison.Ordinal))
            return true;

        return RelativePosted().IsMatch(t) || t is "today" or "yesterday" or "just now";
    }

    private static bool LooksLikeBenefits(string line)
    {
        var t = line.ToLowerInvariant();
        return t.Contains("medical")
            || t.Contains("dental")
            || t.Contains("vision")
            || t.Contains("401(k)")
            || t.Contains("401k")
            || t.Contains("benefit");
    }

    // Optional multi-job paste separator the user can insert between cards.
    [GeneratedRegex(@"^\s*-{3,}\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitJobSplit();

    [GeneratedRegex(@"^(?<title>.+?)\s*\(Verified job\)\s*(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VerifiedJobConcat();

    [GeneratedRegex(@"^(?<title>.+?),\s*Viewed\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleWithViewed();

    [GeneratedRegex(@"\s*\(Verified job\)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StripVerified();

    [GeneratedRegex(@",?\s*Viewed\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingViewed();

    [GeneratedRegex(@",\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingComma();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"\bViewed\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ViewedWord();

    [GeneratedRegex(@"You['’]d be a top applicant", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TopApplicant();

    [GeneratedRegex(@"\$\s*\d", RegexOptions.CultureInvariant)]
    private static partial Regex SalaryRange();

    [GeneratedRegex(@"\b\d{2,3}\s*K\s*/\s*yr\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompWithoutDollar();

    [GeneratedRegex(@"^(?:reposted\s+)?(?<n>\d+)\s+(?<unit>minute|minutes|hour|hours|day|days|week|weeks|month|months|year|years)\s+ago$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RelativePosted();

    [GeneratedRegex(@"^[A-Za-z .'-]+,\s*[A-Z]{2}(?:\s*\([^)]+\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex CityState();
}
