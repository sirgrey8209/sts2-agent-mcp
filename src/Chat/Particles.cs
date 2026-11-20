using System;
using System.Linq;

namespace Sts2AgentMcp.Chat;

/// <summary>
/// Korean particles fitted to the word before them, for display: an agent that writes a card id (`PINPOINT`로) cannot
/// know the shown name (정밀 사격), so the particle right after a name is chosen again from the name's last sound
/// (정밀 사격으로). "+" reads 플러스 (no final consonant); digits read as Sino-Korean numbers. A particle is only
/// changed when the name's ending is known.
/// </summary>
internal static class Particles
{
    // Form after a final consonant, form after a vowel. Longer forms are tried first.
    private static readonly (string Consonant, string Vowel)[] Pairs =
    {
        ("이에요", "예요"), ("으로", "로"), ("이랑", "랑"), ("이나", "나"), ("이며", "며"),
        ("이", "가"), ("을", "를"), ("은", "는"), ("과", "와"),
    };

    private static readonly string[] Forms = Pairs
        .SelectMany(p => new[] { p.Consonant, p.Vowel })
        .Distinct()
        .OrderByDescending(f => f.Length)
        .ToArray();

    /// <summary><paramref name="after"/> with its leading particle fitted to <paramref name="word"/>.</summary>
    public static string Fit(string word, string after)
    {
        if (after.Length == 0 || Ending(word) is not { } ending) return after;
        foreach (var form in Forms)
        {
            if (!after.StartsWith(form, StringComparison.Ordinal)) continue;
            var pair = Pairs.First(p => p.Consonant == form || p.Vowel == form);
            string fitted = pair.Consonant == "으로"
                ? (ending.Final && !ending.Rieul ? "으로" : "로")
                : (ending.Final ? pair.Consonant : pair.Vowel);
            return fitted + after[form.Length..];
        }
        return after;
    }

    /// <summary>Whether the word ends in a final consonant (and whether it is ㄹ); null when unknown.</summary>
    private static (bool Final, bool Rieul)? Ending(string word)
    {
        string w = word.TrimEnd();
        if (w.Length == 0) return null;
        char c = w[^1];
        if (c == '+') return (false, false); // 플러스
        if (c >= '가' && c <= '힣')
        {
            int jong = (c - 0xAC00) % 28;
            return (jong != 0, jong == 8);
        }
        return c switch
        {
            '0' => (true, false),  // 영
            '1' => (true, true),   // 일
            '2' => (false, false), // 이
            '3' => (true, false),  // 삼
            '4' => (false, false), // 사
            '5' => (false, false), // 오
            '6' => (true, false),  // 육
            '7' => (true, true),   // 칠
            '8' => (true, true),   // 팔
            '9' => (false, false), // 구
            _ => null,
        };
    }
}
