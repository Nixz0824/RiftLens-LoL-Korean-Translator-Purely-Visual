using System.Drawing.Imaging;
using System.Text;
using System.Text.RegularExpressions;
using RiftLens.Models;

namespace RiftLens.Services;

/// <summary>
/// Keeps DeepSeek's literal transcription as the primary source while borrowing
/// line geometry, colour and tightly-scoped header evidence from the bundled OCR.
/// Free-form player content is never replaced from the secondary engine.
/// </summary>
public sealed partial class VisionOcrPostProcessor
{
    private readonly HeroDictionary _heroes;
    private readonly ChatMessageClassifier _classifier = new();

    public VisionOcrPostProcessor(HeroDictionary heroes)
    {
        _heroes = heroes;
    }

    public VisionPostProcessResult Process(
        Bitmap? source,
        OcrDocument visionDocument,
        OcrDocument? localDocument = null)
    {
        var joinedContinuations = 0;
        var droppedOrphans = 0;
        var visionLines = PrepareLines(
            visionDocument,
            ref joinedContinuations,
            ref droppedOrphans);
        var ignoredJoined = 0;
        var ignoredDropped = 0;
        var localLines = localDocument is null
            ? new List<WorkingLine>()
            : PrepareLines(localDocument, ref ignoredJoined, ref ignoredDropped);

        var matches = Align(visionLines, localLines);
        var output = new List<OcrRecognizedLine>(visionLines.Count);
        var matchedLocalLines = 0;
        var repairedSeparators = 0;
        var repairedHeroTokens = 0;
        var repairedOfficialTermFields = 0;

        for (var index = 0; index < visionLines.Count; index++)
        {
            var primary = visionLines[index];
            WorkingLine? secondary = null;
            var matchScore = 0d;
            if (matches.TryGetValue(index, out var match))
            {
                secondary = localLines[match.LocalIndex];
                matchScore = match.Score;
                matchedLocalLines++;
            }

            var repaired = RepairHeader(
                primary.Text,
                secondary?.Text,
                secondary?.ColorEvidence,
                out var separatorRepaired,
                out var heroRepaired,
                out var officialTermFieldRepaired);
            if (separatorRepaired) repairedSeparators++;
            if (heroRepaired) repairedHeroTokens++;
            if (officialTermFieldRepaired) repairedOfficialTermFields++;

            var bounds = secondary?.Bounds ?? primary.Bounds;
            var colour = CreateColourEvidence(source, bounds, repaired)
                ?? secondary?.ColorEvidence
                ?? primary.ColorEvidence;
            var confidence = secondary is null
                ? primary.Confidence
                : Math.Clamp(
                    Math.Max(primary.Confidence, 0.58f + (float)(matchScore * 0.38)),
                    0,
                    0.98f);
            output.Add(new OcrRecognizedLine(repaired, bounds, confidence, colour));
        }

        return new VisionPostProcessResult(
            new OcrDocument(
                string.Join(Environment.NewLine, output.Select(line => line.Text)),
                output),
            new VisionPostProcessStats(
                visionDocument.Lines.Count,
                output.Count,
                joinedContinuations,
                droppedOrphans,
                matchedLocalLines,
                repairedSeparators,
                repairedHeroTokens,
                repairedOfficialTermFields));
    }

    private List<WorkingLine> PrepareLines(
        OcrDocument document,
        ref int joinedContinuations,
        ref int droppedOrphans)
    {
        var sourceLines = document.Lines.Count > 0
            ? document.Lines
            : OcrDocument.FromText(document.RawText).Lines;
        var normalized = sourceLines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .Select(line => new WorkingLine(
                NormalizeLine(line.Text),
                line.Bounds,
                line.Confidence,
                line.ColorEvidence,
                HasTimestampPrefix(line.Text)))
            .Where(line => line.Text.Length > 0)
            .ToList();
        var timestamped = normalized.Count(line => line.HadTimestamp);
        if (timestamped < 2)
        {
            return normalized;
        }

        var result = new List<WorkingLine>(normalized.Count);
        foreach (var line in normalized)
        {
            if (!line.HadTimestamp)
            {
                if (result.Count == 0)
                {
                    if (!LooksLikeIndependentLine(line.Text))
                    {
                        // With timestamps enabled, a headerless first row is the visible
                        // continuation of a message that has already scrolled off screen.
                        droppedOrphans++;
                        continue;
                    }
                }
                else if (result[^1].HadTimestamp
                    && (!LooksLikeIndependentLine(line.Text)
                        || PreviousLineRequiresContinuation(result[^1].Text)))
                {
                    result[^1] = Merge(result[^1], line);
                    joinedContinuations++;
                    continue;
                }
            }
            result.Add(line);
        }
        return result;
    }

    private string RepairHeader(
        string primaryText,
        string? secondaryText,
        OcrColorEvidence? secondaryColour,
        out bool separatorRepaired,
        out bool heroRepaired,
        out bool officialTermFieldRepaired)
    {
        separatorRepaired = false;
        heroRepaired = false;
        officialTermFieldRepaired = false;
        if (!TryReadHeader(primaryText, out var primary))
        {
            return primaryText;
        }

        HeaderParts? secondary = null;
        if (!string.IsNullOrWhiteSpace(secondaryText)
            && TryReadHeader(secondaryText, out var parsedSecondary)
            && SpeakersMatch(primary.Speaker, parsedSecondary.Speaker))
        {
            secondary = parsedSecondary;
        }

        var repairedBody = primary.Body;
        var primaryHeroId = _heroes.FindOfficialHeroId(primary.Hero);
        var secondaryHeroId = secondary is null
            ? null
            : _heroes.FindOfficialHeroId(secondary.Hero);
        if (primaryHeroId is null && secondaryHeroId is not null)
        {
            var official = _heroes.FindById(secondaryHeroId, preferPersonalName: false);
            if (official is not null)
            {
                var correctedHero = primary.Hero.Any(IsHan)
                    ? official.ChinesePersonal
                    : official.KoreanName;
                repairedBody = ReplaceGroup(repairedBody, primary.HeroGroup, correctedHero);
                heroRepaired = true;
                if (!TryReadHeader(WithTimestamp(primary, repairedBody), out primary))
                {
                    return WithTimestamp(primary, repairedBody);
                }
                repairedBody = primary.Body;
                primaryHeroId = secondaryHeroId;
            }
        }

        var secondaryKind = secondary is null
            ? GameMessageKind.Unknown
            : _classifier.Classify(secondaryText!, secondaryColour).Kind;
        var canRepairSeparator = secondaryKind is GameMessageKind.PlayerChat
            or GameMessageKind.TacticalSignal;
        var needsSeparatorRepair = string.IsNullOrEmpty(primary.Separator)
            && secondary is not null
            && !string.IsNullOrEmpty(secondary.Separator)
            && (primaryHeroId is null
                || secondaryHeroId is null
                || string.Equals(primaryHeroId, secondaryHeroId, StringComparison.OrdinalIgnoreCase));
        if (canRepairSeparator
            && needsSeparatorRepair
            && GeneratedSubjectPrefixRegex().Match(primary.ContentGroup.Value.Trim()) is { Success: true } generated)
        {
            var cleanedPrimaryContent = primary.ContentGroup.Value.Trim()[generated.Length..].TrimStart();
            var secondaryContent = secondary!.ContentGroup.Value.Trim();
            var contentSimilarity = Similarity(
                NormalizeForSimilarity(cleanedPrimaryContent),
                NormalizeForSimilarity(secondaryContent));
            var requiredSimilarity = secondaryKind == GameMessageKind.TacticalSignal ? 0.78 : 0.90;
            if (contentSimilarity < requiredSimilarity)
            {
                canRepairSeparator = false;
            }
            else
            {
                repairedBody = repairedBody[..primary.ContentGroup.Index] + cleanedPrimaryContent;
                if (!TryReadHeader(WithTimestamp(primary, repairedBody), out primary))
                {
                    return WithTimestamp(primary, repairedBody);
                }
                repairedBody = primary.Body;
            }
        }

        if (canRepairSeparator
            && needsSeparatorRepair
            && secondaryKind is GameMessageKind.PlayerChat or GameMessageKind.TacticalSignal)
        {
            var insertIndex = primary.ContentGroup.Index;
            while (insertIndex > 0 && char.IsWhiteSpace(repairedBody[insertIndex - 1]))
            {
                insertIndex--;
            }
            repairedBody = repairedBody[..insertIndex]
                + ": "
                + repairedBody[primary.ContentGroup.Index..];
            separatorRepaired = true;
        }

        if (secondary is not null
            && !string.IsNullOrEmpty(secondary.Separator)
            && TryReadHeader(WithTimestamp(primary, repairedBody), out var current)
            && TryRepairDeterministicFeedContent(current, secondary, out var correctedContent))
        {
            repairedBody = current.Body[..current.ContentGroup.Index] + correctedContent;
            primary = current;
            officialTermFieldRepaired = true;
        }

        return WithTimestamp(primary, repairedBody);
    }

    private bool TryRepairDeterministicFeedContent(
        HeaderParts primary,
        HeaderParts secondary,
        out string correctedContent)
    {
        correctedContent = primary.ContentGroup.Value.Trim();
        var secondaryContent = secondary.ContentGroup.Value.Trim();
        var sourceSimilarity = Similarity(
            NormalizeForSimilarity(correctedContent),
            NormalizeForSimilarity(secondaryContent));
        if (sourceSimilarity < 0.15)
        {
            return false;
        }

        var hasOfficialHeroEvidence = _heroes.TryCorrectKoreanHeroPrefix(
                secondaryContent,
                out var officialHero,
                out var secondaryHeroLength);
        if (hasOfficialHeroEvidence)
        {
            var primaryHeroLength = LeadingHangulLength(correctedContent);
            if (primaryHeroLength == officialHero.Length)
            {
                correctedContent = officialHero
                    + " "
                    + correctedContent[primaryHeroLength..].TrimStart();
            }

            secondaryContent = officialHero
                + " "
                + secondaryContent[secondaryHeroLength..].TrimStart();
            correctedContent = RepairTermsAfterLeadingHero(correctedContent, officialHero);
            secondaryContent = RepairTermsAfterLeadingHero(secondaryContent, officialHero);
        }
        else
        {
            var primaryChinese = _heroes.TryCorrectChineseHeroPrefix(
                correctedContent,
                out var primaryHeroId,
                out var primaryOfficialHero,
                out var primaryChineseLength);
            var secondaryChinese = _heroes.TryCorrectChineseHeroPrefix(
                secondaryContent,
                out var secondaryHeroId,
                out var secondaryOfficialHero,
                out var secondaryChineseLength);
            if (primaryChinese && secondaryChinese
                && !string.Equals(primaryHeroId, secondaryHeroId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (primaryChinese || secondaryChinese)
            {
                hasOfficialHeroEvidence = true;
                var heroId = primaryChinese ? primaryHeroId : secondaryHeroId;
                var simplifiedHero = primaryChinese ? primaryOfficialHero : secondaryOfficialHero;
                var primaryPrefixLength = primaryChinese
                    ? primaryChineseLength
                    : LeadingHanLength(correctedContent);
                if (primaryPrefixLength > 0)
                {
                    correctedContent = simplifiedHero
                        + " "
                        + correctedContent[primaryPrefixLength..].TrimStart();
                }
                if (secondaryChinese)
                {
                    secondaryContent = simplifiedHero
                        + " "
                        + secondaryContent[secondaryChineseLength..].TrimStart();
                }

                correctedContent = NormalizeChineseHudTerms(correctedContent, simplifiedHero);
                secondaryContent = NormalizeChineseHudTerms(secondaryContent, simplifiedHero);
            }
        }
        if (!GameSemanticEventParser.TryParsePlayerFeed(correctedContent, out _)
            && hasOfficialHeroEvidence
            && GameSemanticEventParser.TryParsePlayerFeed(secondaryContent, out _))
        {
            correctedContent = secondaryContent;
        }

        return GameSemanticEventParser.TryParsePlayerFeed(correctedContent, out _)
            && !string.Equals(
                NormalizeForSimilarity(correctedContent),
                NormalizeForSimilarity(primary.ContentGroup.Value),
            StringComparison.Ordinal);
    }

    private static string RepairTermsAfterLeadingHero(string content, string officialHero)
    {
        if (!content.StartsWith(officialHero, StringComparison.Ordinal))
        {
            return content;
        }

        var remainder = content[officialHero.Length..].TrimStart();
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return officialHero;
        }

        if (GameTermGlossary.TryCanonicalizeSummonerSpellToken(remainder, out var canonicalSpell))
        {
            remainder = canonicalSpell;
        }
        else
        {
            remainder = GameTermGlossary.RepairKoreanOcrTokens(remainder);
        }

        return officialHero + " " + remainder;
    }

    private static string NormalizeChineseHudTerms(string content, string officialHero)
    {
        if (!content.StartsWith(officialHero, StringComparison.Ordinal))
        {
            return content;
        }

        var remainder = content[officialHero.Length..].TrimStart();
        return string.IsNullOrWhiteSpace(remainder)
            ? officialHero
            : officialHero + " " + GameTermGlossary.ReplaceTerms(remainder, AppLanguage.Chinese);
    }

    private static int LeadingHangulLength(string value)
    {
        var length = 0;
        while (length < value.Length && value[length] is >= '\uAC00' and <= '\uD7A3')
        {
            length++;
        }
        return length;
    }

    private static int LeadingHanLength(string value)
    {
        var length = 0;
        while (length < value.Length && IsHan(value[length]))
        {
            length++;
        }
        return length;
    }

    private static bool IsHan(char character) =>
        character is >= '\u3400' and <= '\u9FFF';

    private static Dictionary<int, LineMatch> Align(
        IReadOnlyList<WorkingLine> primary,
        IReadOnlyList<WorkingLine> secondary)
    {
        var matches = new Dictionary<int, LineMatch>();
        var used = new HashSet<int>();
        for (var primaryIndex = 0; primaryIndex < primary.Count; primaryIndex++)
        {
            var bestIndex = -1;
            var bestScore = double.MinValue;
            for (var secondaryIndex = 0; secondaryIndex < secondary.Count; secondaryIndex++)
            {
                if (used.Contains(secondaryIndex))
                {
                    continue;
                }

                var score = MatchScore(
                    primary[primaryIndex],
                    primaryIndex,
                    primary.Count,
                    secondary[secondaryIndex],
                    secondaryIndex,
                    secondary.Count);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = secondaryIndex;
                }
            }

            if (bestIndex >= 0 && bestScore >= 0.42)
            {
                used.Add(bestIndex);
                matches[primaryIndex] = new LineMatch(bestIndex, bestScore);
            }
        }
        return matches;
    }

    private static double MatchScore(
        WorkingLine primary,
        int primaryIndex,
        int primaryCount,
        WorkingLine secondary,
        int secondaryIndex,
        int secondaryCount)
    {
        var primaryTimestamp = GameTimestampPrefixParser.Parse(primary.Text);
        var secondaryTimestamp = GameTimestampPrefixParser.Parse(secondary.Text);
        var timestampScore = 0d;
        if (primaryTimestamp.Matched && secondaryTimestamp.Matched)
        {
            timestampScore = primaryTimestamp.Timestamp == secondaryTimestamp.Timestamp
                ? 0.34
                : -0.42;
        }

        var left = NormalizeForSimilarity(primaryTimestamp.TextWithoutTimestamp);
        var right = NormalizeForSimilarity(secondaryTimestamp.TextWithoutTimestamp);
        var similarity = Similarity(left, right);
        var primaryPosition = primaryIndex / (double)Math.Max(1, primaryCount - 1);
        var secondaryPosition = secondaryIndex / (double)Math.Max(1, secondaryCount - 1);
        var orderScore = 0.08 * (1 - Math.Abs(primaryPosition - secondaryPosition));
        return similarity * 0.72 + timestampScore + orderScore;
    }

    private static bool TryReadHeader(string text, out HeaderParts parts)
    {
        var timestamp = GameTimestampPrefixParser.Parse(text);
        var body = timestamp.TextWithoutTimestamp;
        var match = HeaderRegex().Match(body);
        if (!match.Success)
        {
            parts = HeaderParts.Empty(text, body, timestamp);
            return false;
        }

        parts = new HeaderParts(
            text,
            body,
            timestamp,
            match.Groups["speaker"].Value.Trim(),
            match.Groups["hero"].Value.Trim(),
            match.Groups["separator"].Value,
            match.Groups["hero"],
            match.Groups["content"]);
        return true;
    }

    private static string WithTimestamp(HeaderParts parts, string body) =>
        parts.Timestamp.Matched
            ? $"{parts.Timestamp.OriginalPrefix} {body}".Trim()
            : body;

    private static string ReplaceGroup(string source, Group group, string replacement) =>
        source[..group.Index] + replacement + source[(group.Index + group.Length)..];

    private static bool SpeakersMatch(string left, string right)
    {
        var normalizedLeft = NormalizeSpeaker(left);
        var normalizedRight = NormalizeSpeaker(right);
        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return true;
        }
        return Math.Min(normalizedLeft.Length, normalizedRight.Length) >= 6
            && Math.Abs(normalizedLeft.Length - normalizedRight.Length) <= 1
            && EditDistance(normalizedLeft, normalizedRight) <= 1;
    }

    private static string NormalizeSpeaker(string value) => new(
        value.Normalize(NormalizationForm.FormKC)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static bool HasTimestampPrefix(string text)
    {
        var parsed = GameTimestampPrefixParser.Parse(text);
        return parsed.Matched
            || GameTimestampPrefixParser.StartsWithTimestampLikePrefix(text);
    }

    private static bool LooksLikeIndependentLine(string text)
    {
        var body = GameTimestampPrefixParser.Strip(text);
        if (HeaderRegex().IsMatch(body)
            || ExplicitChannelRegex().IsMatch(body)
            || ReservedSystemRegex().IsMatch(body)
            || SystemMessageCatalog.TryClassify(body, out _, out _, out _)
            || GameSemanticEventParser.TryParseSystemFeed(body, out _))
        {
            return true;
        }
        return false;
    }

    private static bool PreviousLineRequiresContinuation(string text) =>
        IncompleteFixedFeedSuffixRegex().IsMatch(
            GameTimestampPrefixParser.Strip(text).TrimEnd());

    private static WorkingLine Merge(WorkingLine first, WorkingLine second)
    {
        var bounds = first.Bounds.IsEmpty
            ? second.Bounds
            : second.Bounds.IsEmpty
                ? first.Bounds
                : Rectangle.Union(first.Bounds, second.Bounds);
        return new WorkingLine(
            $"{first.Text.TrimEnd()} {second.Text.TrimStart()}",
            bounds,
            Math.Min(first.Confidence, second.Confidence),
            null,
            first.HadTimestamp);
    }

    private static OcrColorEvidence? CreateColourEvidence(
        Bitmap? source,
        Rectangle bounds,
        string text)
    {
        if (source is null || bounds.IsEmpty)
        {
            return null;
        }

        var clipped = Rectangle.Intersect(
            new Rectangle(Point.Empty, source.Size),
            bounds);
        if (clipped.Width < 4 || clipped.Height < 4)
        {
            return null;
        }

        try
        {
            using var crop = source.Clone(clipped, PixelFormat.Format24bppRgb);
            return OcrColorAnalyzer.Analyze(crop, text);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeLine(string value) => Regex.Replace(
            value.Normalize(NormalizationForm.FormKC).Trim(),
            @"\s+",
            " ",
            RegexOptions.CultureInvariant)
        .Trim();

    private static string NormalizeForSimilarity(string value) => new(
        value.Normalize(NormalizationForm.FormKC)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static double Similarity(string left, string right)
    {
        var maximum = Math.Max(left.Length, right.Length);
        return maximum == 0 ? 1 : 1 - EditDistance(left, right) / (double)maximum;
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var row = 1; row <= left.Length; row++)
        {
            var current = new int[right.Length + 1];
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                current[column] = Math.Min(
                    Math.Min(previous[column] + 1, current[column - 1] + 1),
                    previous[column - 1] + (left[row - 1] == right[column - 1] ? 0 : 1));
            }
            previous = current;
        }
        return previous[right.Length];
    }

    [GeneratedRegex(
        @"^\s*(?:\[(?:팀|전체|파티|所有人|队伍|团队|组队|All|Team|Party)\]\s*)?"
        + @"(?<speaker>[^:：\r\n\(\[（]{1,64}?)\s*[\(\[（]\s*"
        + @"(?<hero>[^:：\r\n\)\]）]{1,36})\s*[\)\]）]\s*"
        + @"(?<separator>[:：·•]?)\s*(?<content>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(
        @"^\s*\[(?:팀|전체|파티|所有人|队伍|团队|组队|All|Team|Party)\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitChannelRegex();

    [GeneratedRegex(
        @"^\s*(?:시스템|알림|안내|공지|신호|系统|通知|提示|信号|SYSTEM|NOTICE|SIGNAL)\s*[:：]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedSystemRegex();

    [GeneratedRegex(
        @"^(?:님\s*(?:이|가)?|님이|님가)\s*",
        RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedSubjectPrefixRegex();

    [GeneratedRegex(
        @"(?:님이|님께서|확인하고|아이템을|주문을|협곡의|연속|추가\s*골드|(?:이|가|을|를|의|하고))$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IncompleteFixedFeedSuffixRegex();

    private sealed record WorkingLine(
        string Text,
        Rectangle Bounds,
        float Confidence,
        OcrColorEvidence? ColorEvidence,
        bool HadTimestamp);

    private sealed record LineMatch(int LocalIndex, double Score);

    private sealed record HeaderParts(
        string Original,
        string Body,
        GameTimestampParseResult Timestamp,
        string Speaker,
        string Hero,
        string Separator,
        Group HeroGroup,
        Group ContentGroup)
    {
        public static HeaderParts Empty(
            string original,
            string body,
            GameTimestampParseResult timestamp) => new(
                original,
                body,
                timestamp,
                string.Empty,
                string.Empty,
                string.Empty,
                Match.Empty.Groups[0],
                Match.Empty.Groups[0]);
    }
}

public sealed record VisionPostProcessResult(
    OcrDocument Document,
    VisionPostProcessStats Stats);

public sealed record VisionPostProcessStats(
    int InputLines,
    int OutputLines,
    int JoinedContinuations,
    int DroppedOrphans,
    int MatchedLocalLines,
    int RepairedPlayerSeparators,
    int RepairedHeroTokens,
    int RepairedOfficialTermFields);
