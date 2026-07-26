using System.Text;

namespace RiftLens.Services;

public enum OcrRecognitionModel
{
    KoreanEnglish,
    ChineseEnglish
}

public sealed record OcrTextCandidate(
    string Text,
    float Confidence,
    OcrRecognitionModel Model);

public static class TrilingualOcrFusion
{
    public static OcrTextCandidate Select(
        OcrTextCandidate koreanEnglish,
        OcrTextCandidate chineseEnglish)
    {
        var korean = Normalize(koreanEnglish);
        var chinese = Normalize(chineseEnglish);
        if (korean.Text.Length == 0)
        {
            return chinese;
        }
        if (chinese.Text.Length == 0)
        {
            return korean;
        }

        var koreanProfile = Analyze(korean.Text);
        var chineseProfile = Analyze(chinese.Text);

        // English, numbers, player names and punctuation are present in both dictionaries.
        // Agreement is stronger evidence than a small confidence difference between models.
        if (koreanProfile.HasOnlySharedCharacters && chineseProfile.HasOnlySharedCharacters)
        {
            var agreement = Similarity(korean.Text, chinese.Text);
            if (agreement >= 0.82f)
            {
                if (korean.Text.Length != chinese.Text.Length
                    && Math.Abs(korean.Confidence - chinese.Confidence) <= 0.08f)
                {
                    return korean.Text.Length > chinese.Text.Length ? korean : chinese;
                }
                return korean.Confidence >= chinese.Confidence ? korean : chinese;
            }
        }

        var koreanScore = Score(korean, koreanProfile);
        var chineseScore = Score(chinese, chineseProfile);
        return koreanScore >= chineseScore ? korean : chinese;
    }

    public static string JoinSegments(IEnumerable<OcrTextCandidate> segments)
    {
        var builder = new StringBuilder();
        foreach (var candidate in segments)
        {
            var text = Normalize(candidate).Text;
            if (text.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0 && !StartsWithClosingPunctuation(text))
            {
                builder.Append(' ');
            }
            builder.Append(text);
        }
        return builder.ToString().Trim();
    }

    public static bool ContainsHangul(string text) => Analyze(text).Hangul > 0;

    public static bool ContainsHan(string text) => Analyze(text).Han > 0;

    private static OcrTextCandidate Normalize(OcrTextCandidate candidate)
    {
        var text = string.Join(
            " ",
            (candidate.Text ?? string.Empty)
                .Normalize(NormalizationForm.FormC)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        text = AddScriptTransitionSpaces(text);
        return candidate with
        {
            Text = text.Trim(),
            Confidence = Math.Clamp(candidate.Confidence, 0f, 1f)
        };
    }

    private static float Score(OcrTextCandidate candidate, ScriptProfile profile)
    {
        var score = candidate.Confidence;
        var readable = Math.Max(1, profile.Readable);
        if (candidate.Model == OcrRecognitionModel.KoreanEnglish)
        {
            score += 0.20f * profile.Hangul / readable;
            score -= 0.34f * profile.Han / readable;
            if (profile.HangulSyllables == 0 && profile.HangulJamo > 0)
            {
                // A lone compatibility Jamo is a common Korean-model hallucination on Han text.
                score -= 0.16f;
            }
        }
        else
        {
            score += 0.20f * profile.Han / readable;
            score -= 0.34f * profile.Hangul / readable;
        }

        if (profile.Readable == 0)
        {
            score -= 0.25f;
        }
        if (profile.OtherLetters > 0)
        {
            score -= Math.Min(0.18f, profile.OtherLetters * 0.04f);
        }
        return score;
    }

    private static ScriptProfile Analyze(string text)
    {
        var hangul = 0;
        var hangulSyllables = 0;
        var hangulJamo = 0;
        var han = 0;
        var shared = 0;
        var otherLetters = 0;

        foreach (var character in text)
        {
            if (IsHangulSyllable(character))
            {
                hangul++;
                hangulSyllables++;
            }
            else if (IsHangulJamo(character))
            {
                hangul++;
                hangulJamo++;
            }
            else if (IsHan(character))
            {
                han++;
            }
            else if (IsShared(character))
            {
                shared++;
            }
            else if (char.IsLetterOrDigit(character))
            {
                otherLetters++;
            }
        }

        return new ScriptProfile(
            hangul,
            hangulSyllables,
            hangulJamo,
            han,
            shared,
            otherLetters);
    }

    private static float Similarity(string left, string right)
    {
        left = SharedComparisonText(left);
        right = SharedComparisonText(right);
        if (left == right)
        {
            return 1f;
        }
        if (left.Length == 0 || right.Length == 0)
        {
            return 0f;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitution = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitution);
            }
            (previous, current) = (current, previous);
        }

        var distance = previous[right.Length];
        return 1f - distance / (float)Math.Max(left.Length, right.Length);
    }

    private static string SharedComparisonText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }
        return builder.ToString();
    }

    private static string AddScriptTransitionSpaces(string text)
    {
        var builder = new StringBuilder(text.Length + 4);
        char? previous = null;
        foreach (var character in text)
        {
            if (previous is not null
                && !char.IsWhiteSpace(previous.Value)
                && !char.IsWhiteSpace(character)
                && IsLatinCjkTransition(previous.Value, character))
            {
                builder.Append(' ');
            }
            builder.Append(character);
            previous = character;
        }
        return builder.ToString();
    }

    private static bool IsLatinCjkTransition(char left, char right)
    {
        var leftLatin = left is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        var rightLatin = right is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        var leftCjk = IsHangulSyllable(left) || IsHangulJamo(left) || IsHan(left);
        var rightCjk = IsHangulSyllable(right) || IsHangulJamo(right) || IsHan(right);
        return leftLatin && rightCjk || leftCjk && rightLatin;
    }

    private static bool StartsWithClosingPunctuation(string text) =>
        text.Length > 0 && text[0] is ',' or '.' or '!' or '?' or ':' or ';'
            or '，' or '。' or '！' or '？' or '：' or '；' or ')' or ']' or '}';

    private static bool IsShared(char character) =>
        character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9';

    private static bool IsHangulSyllable(char character) =>
        character is >= '\uAC00' and <= '\uD7AF';

    private static bool IsHangulJamo(char character) =>
        character is >= '\u1100' and <= '\u11FF'
            or >= '\u3130' and <= '\u318F'
            or >= '\uA960' and <= '\uA97F'
            or >= '\uD7B0' and <= '\uD7FF';

    private static bool IsHan(char character) =>
        character is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF';

    private sealed record ScriptProfile(
        int Hangul,
        int HangulSyllables,
        int HangulJamo,
        int Han,
        int Shared,
        int OtherLetters)
    {
        public int Readable => Hangul + Han + Shared + OtherLetters;
        public bool HasOnlySharedCharacters =>
            Hangul == 0 && Han == 0 && OtherLetters == 0 && Shared > 0;
    }
}
