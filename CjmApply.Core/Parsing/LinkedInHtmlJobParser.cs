using System.Net;
using System.Text.RegularExpressions;
using CjmApply.Core.Models;

namespace CjmApply.Core.Parsing;

/// <summary>
/// Parses LinkedIn job-search HTML (Save Page / outerHTML paste).
/// Expects cards marked with componentkey="job-card-component-ref-{jobId}".
/// </summary>
public static partial class LinkedInHtmlJobParser
{
    public static bool LooksLikeHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("job-card-component-ref-", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || (text.Contains("<html", StringComparison.OrdinalIgnoreCase)
                && text.Contains("linkedin", StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<JobRecord> Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];

        var results = new List<JobRecord>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in JobCardRef().Matches(html))
        {
            var jobId = match.Groups["id"].Value;
            if (!seenIds.Add(jobId))
                continue;

            var start = match.Index;
            var next = match.NextMatch();
            var end = next.Success ? next.Index : Math.Min(html.Length, start + 12_000);
            var slice = html[start..end];

            var job = ParseCard(jobId, slice);
            if (job is null)
                continue;

            results.Add(job);
        }

        return results;
    }

    private static JobRecord? ParseCard(string jobId, string cardHtml)
    {
        var title = ExtractTitle(cardHtml);
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var lines = ExtractTextLines(cardHtml);
        var company = ExtractCompany(lines, title);
        var location = ExtractLocation(lines, title, company);
        var compensation = lines.FirstOrDefault(LooksLikeCompensation);
        var postedText = ExtractPostedText(cardHtml, lines);
        var viewed = lines.Any(l => string.Equals(l, "Viewed", StringComparison.OrdinalIgnoreCase))
            || ViewedWord().IsMatch(cardHtml);
        var topApplicant = lines.Any(l => TopApplicant().IsMatch(l))
            || TopApplicant().IsMatch(cardHtml);

        return new JobRecord
        {
            JobTitle = title,
            Company = company ?? "",
            Location = location,
            Viewed = viewed,
            IsTopApplicant = topApplicant,
            CompensationText = compensation,
            PostedDateText = postedText,
            PostedDate = LinkedInPasteParser.TryResolveRelativeDate(postedText, DateTime.Today),
            ApplyUrl = $"https://www.linkedin.com/jobs/view/{jobId}/",
            RawPaste = cardHtml.Length > 4000 ? cardHtml[..4000] : cardHtml,
            JobKey = JobKeyBuilder.Build(title, company, location),
        };
    }

    private static string? ExtractTitle(string cardHtml)
    {
        var dismiss = DismissAriaLabel().Match(cardHtml);
        if (dismiss.Success)
            return CleanTitle(WebUtility.HtmlDecode(dismiss.Groups["title"].Value));

        // Typical title node: <span aria-hidden="true">Title...</span>
        var hidden = TitleAriaHidden().Match(cardHtml);
        if (hidden.Success)
            return CleanTitle(StripTags(WebUtility.HtmlDecode(hidden.Groups["title"].Value)));

        return null;
    }

    private static string? ExtractCompany(IReadOnlyList<string> lines, string title)
    {
        foreach (var line in lines)
        {
            if (string.Equals(line, title, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsNoiseOrFlag(line) || LooksLikeLocation(line) || LooksLikeCompensation(line)
                || LooksLikePosted(line) || LooksLikeBenefits(line))
                continue;

            return line;
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

    private static string? ExtractPostedText(string cardHtml, IReadOnlyList<string> lines)
    {
        var posted = PostedLabel().Match(cardHtml);
        if (posted.Success)
        {
            var text = WebUtility.HtmlDecode(posted.Groups["text"].Value).Trim();
            if (text.Length > 0)
                return text;
        }

        foreach (var line in lines)
        {
            if (LooksLikePosted(line))
                return line.StartsWith("Posted ", StringComparison.OrdinalIgnoreCase)
                    ? line
                    : $"Posted {line}";
        }

        return null;
    }

    private static List<string> ExtractTextLines(string cardHtml)
    {
        var decoded = WebUtility.HtmlDecode(cardHtml);
        decoded = Comment().Replace(decoded, " ");
        decoded = ScriptOrStyle().Replace(decoded, " ");
        decoded = BlockBreak().Replace(decoded, "\n");
        decoded = StripTags(decoded);

        return decoded
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => MultiSpace().Replace(l, " ").Trim())
            .Where(l => l.Length > 0)
            .Where(l => l is not ("·" or "•" or "-" or "–" or "—"))
            .Where(l => !l.StartsWith("componentkey=", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string CleanTitle(string title)
    {
        title = title.Trim();
        title = StripVerified().Replace(title, string.Empty).Trim();
        title = TrailingViewed().Replace(title, string.Empty).Trim();
        title = TrailingComma().Replace(title, string.Empty).Trim();
        return title;
    }

    private static string StripTags(string value) =>
        Tag().Replace(value, string.Empty);

    private static bool IsNoiseOrFlag(string line)
    {
        var t = line.ToLowerInvariant();
        return t is "viewed" or "easy apply" or "promoted" or "verified job"
            || t.Contains("you'd be a top applicant", StringComparison.Ordinal)
            || t.Contains("you’d be a top applicant", StringComparison.Ordinal)
            || t.Contains("be an early applicant", StringComparison.Ordinal)
            || t.Contains("actively reviewing", StringComparison.Ordinal)
            || t.Contains("dismiss ", StringComparison.Ordinal);
    }

    private static bool LooksLikeLocation(string line)
    {
        if (LooksLikeCompensation(line) || LooksLikePosted(line) || IsNoiseOrFlag(line))
            return false;

        var t = line.ToLowerInvariant();
        if (t.Contains("remote") || t.Contains("hybrid") || t.Contains("on-site") || t.Contains("onsite"))
            return true;
        if (t.Contains("united states") || t.Contains("united kingdom") || t.EndsWith(", usa"))
            return true;
        if (CityState().IsMatch(line))
            return true;
        if (t.Contains(" area") && line.Length < 80)
            return true;

        return false;
    }

    private static bool LooksLikeCompensation(string line) =>
        SalaryRange().IsMatch(line) || CompWithoutDollar().IsMatch(line);

    private static bool LooksLikePosted(string line)
    {
        var t = line.Trim().ToLowerInvariant();
        if (t.StartsWith("posted ", StringComparison.Ordinal) || t.StartsWith("reposted ", StringComparison.Ordinal))
            return true;
        return RelativePosted().IsMatch(t) || t is "today" or "yesterday" or "just now";
    }

    private static bool LooksLikeBenefits(string line)
    {
        var t = line.ToLowerInvariant();
        return t.Contains("benefit")
            || t.Contains("401(k)")
            || t.Contains("401k")
            || t.Contains("medical")
            || t.Contains("dental")
            || t.Contains("vision");
    }

    [GeneratedRegex(@"componentkey\s*=\s*""job-card-component-ref-(?<id>\d+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JobCardRef();

    [GeneratedRegex(@"aria-label\s*=\s*""Dismiss\s+(?<title>.+?)\s+job""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DismissAriaLabel();

    [GeneratedRegex(@"aria-hidden\s*=\s*""true""\s*>(?<title>[^<]{3,200})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleAriaHidden();

    [GeneratedRegex(@">Posted\s+(?<text>[^<]+)<", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PostedLabel();

    [GeneratedRegex(@"<script[\s\S]*?</script>|<style[\s\S]*?</style>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Comment();

    [GeneratedRegex(@"</(p|div|li|h\d|br|tr|button)\s*>|<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockBreak();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"\s*\(Verified job\)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StripVerified();

    [GeneratedRegex(@",?\s*Viewed\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingViewed();

    [GeneratedRegex(@",\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingComma();

    [GeneratedRegex(@"\bViewed\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ViewedWord();

    [GeneratedRegex(@"You['’]d be a top applicant", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TopApplicant();

    [GeneratedRegex(@"\$\s*\d", RegexOptions.CultureInvariant)]
    private static partial Regex SalaryRange();

    [GeneratedRegex(@"\b\d{2,3}(?:\.\d)?\s*K\s*/\s*yr\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompWithoutDollar();

    [GeneratedRegex(@"^(?:reposted\s+)?(?<n>\d+)\s+(?<unit>minute|minutes|hour|hours|day|days|week|weeks|month|months|year|years)\s+ago$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RelativePosted();

    [GeneratedRegex(@"^[A-Za-z .'-]+,\s*[A-Z]{2}(?:\s*\([^)]+\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex CityState();
}
