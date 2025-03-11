using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TGZH.Pinyin;

/// <summary>
/// 增强版拼音转换器，提供更高效的拼音转换功能
/// </summary>
internal static partial class EnhancedPinyinConverter
{
    private static readonly Dictionary<char, string> ToneMarks = new()
    {
        // 一声
        {'ā', "a1"}, {'ē', "e1"}, {'ī', "i1"}, {'ō', "o1"}, {'ū', "u1"}, {'ǖ', "v1"}, {'ń', "n1"}, {'ḿ', "m1"},
        // 二声
        {'á', "a2"}, {'é', "e2"}, {'í', "i2"}, {'ó', "o2"}, {'ú', "u2"}, {'ǘ', "v2"},
        // 三声
        {'ǎ', "a3"}, {'ě', "e3"}, {'ǐ', "i3"}, {'ǒ', "o3"}, {'ǔ', "u3"}, {'ǚ', "v3"}, {'ň', "n3"},
        // 四声
        {'à', "a4"}, {'è', "e4"}, {'ì', "i4"}, {'ò', "o4"}, {'ù', "u4"}, {'ǜ', "v4"}, {'ǹ', "n4"}
    };

    private static readonly Dictionary<string, char> ToneNumberToMark = new()
    {
        {"a1", 'ā'}, {"e1", 'ē'}, {"i1", 'ī'}, {"o1", 'ō'}, {"u1", 'ū'}, {"v1", 'ǖ'}, {"n1", 'ń'}, {"m1", 'ḿ'},
        {"a2", 'á'}, {"e2", 'é'}, {"i2", 'í'}, {"o2", 'ó'}, {"u2", 'ú'}, {"v2", 'ǘ'},
        {"a3", 'ǎ'}, {"e3", 'ě'}, {"i3", 'ǐ'}, {"o3", 'ǒ'}, {"u3", 'ǔ'}, {"v3", 'ǚ'}, {"n3", 'ň'},
        {"a4", 'à'}, {"e4", 'è'}, {"i4", 'ì'}, {"o4", 'ò'}, {"u4", 'ù'}, {"v4", 'ǜ'}, {"n4", 'ǹ'}
    };

    /// <summary>
    /// 快速移除拼音中的声调标记
    /// </summary>
    public static string RemoveToneMarks(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin))
            return string.Empty;

        var sb = new StringBuilder(pinyin.Length);
        foreach (var c in pinyin)
        {
            if (ToneMarks.TryGetValue(c, out var replacement))
            {
                sb.Append(replacement[0]); // 添加不带声调的字母
            }
            else if (c is 'ü' or 'Ü')
            {
                sb.Append('v'); // 特殊处理 ü
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 将带声调拼音转换为数字声调
    /// </summary>
    public static string ToToneNumber(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin))
            return string.Empty;

        // 处理多音节
        if (pinyin.Contains(' '))
        {
            return string.Join(" ", pinyin.Split(' ').Select(ToToneNumberSingle));
        }

        return ToToneNumberSingle(pinyin);
    }

    /// <summary>
    /// 转换单个拼音为数字声调
    /// </summary>
    private static string ToToneNumberSingle(string syllable)
    {
        if (string.IsNullOrEmpty(syllable))
            return string.Empty;

        var toneNumber = 0;
        var result = new StringBuilder(syllable.Length);

        foreach (var c in syllable)
        {
            if (ToneMarks.TryGetValue(c, out var replacement))
            {
                result.Append(replacement[0]); // 添加不带声调的字母
                toneNumber = int.Parse(replacement[1].ToString());
            }
            else
            {
                result.Append(c);
            }
        }

        // 如果找到声调，添加数字
        if (toneNumber > 0)
        {
            result.Append(toneNumber);
        }
        else if (IsNeutralTone(syllable))
        {
            result.Append('0'); // 轻声
        }

        return result.ToString();
    }

    /// <summary>
    /// 将数字声调拼音转换为带声调拼音
    /// </summary>
    public static string ToToneMark(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin))
            return string.Empty;

        // 处理多音节
        return pinyin.Contains(' ') ? string.Join(" ", pinyin.Split(' ').Select(ToToneMarkSingle)) : ToToneMarkSingle(pinyin);
    }

    /// <summary>
    /// 将单个数字声调拼音转换为带声调拼音
    /// </summary>
    private static string ToToneMarkSingle(string syllable)
    {
        if (string.IsNullOrEmpty(syllable))
            return string.Empty;

        var match = ToneMaskSingleRegex().Match(syllable);
        if (!match.Success)
            return syllable;

        var word = match.Groups[1].Value;
        var tone = int.Parse(match.Groups[2].Value);

        // 轻声无需处理
        return tone == 0 ? word : AddToneMarkToSyllable(word, tone);
    }

    /// <summary>
    /// 将拼音首字母大写
    /// </summary>
    public static string Capitalize(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin))
            return string.Empty;

        // 处理多音节
        return pinyin.Contains(' ') ? string.Join(" ", pinyin.Split(' ').Select(CapitalizeSingle)) : CapitalizeSingle(pinyin);
    }

    /// <summary>
    /// 将单个拼音首字母大写
    /// </summary>
    private static string CapitalizeSingle(string syllable)
    {
        if (string.IsNullOrEmpty(syllable))
            return string.Empty;

        if (syllable.Length == 1)
            return syllable.ToUpper();

        return char.ToUpper(syllable[0]) + syllable[1..];
    }

    /// <summary>
    /// 获取拼音首字母
    /// </summary>
    public static string GetFirstLetters(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin))
            return string.Empty;

        var withoutTone = RemoveToneMarks(pinyin);
        var parts = withoutTone.Split(' ');
        var sb = new StringBuilder(parts.Length);

        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part))
            {
                sb.Append(part[0]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 在拼音上添加声调标记
    /// </summary>
    private static string AddToneMarkToSyllable(string syllable, int toneNumber)
    {
        if (string.IsNullOrEmpty(syllable) || toneNumber < 1 || toneNumber > 4)
            return syllable;

        // 元音优先级: a, o, e, i, u, v
        const string vowels = "aoeiuv";
        foreach (var vowel in vowels)
        {
            var index = syllable.IndexOf(vowel);
            if (index < 0) continue;

            // 处理ü的特殊情况
            if (vowel == 'v')
            {
                var key = $"v{toneNumber}";
                if (ToneNumberToMark.TryGetValue(key, out var tonedV))
                {
                    return syllable[..index] + tonedV + syllable[(index + 1)..];
                }
            }
            else
            {
                var key = $"{vowel}{toneNumber}";
                if (ToneNumberToMark.TryGetValue(key, out var tonedVowel))
                {
                    return syllable[..index] + tonedVowel + syllable[(index + 1)..];
                }
            }
            break;
        }

        return syllable;
    }

    /// <summary>
    /// 判断是否为轻声拼音
    /// </summary>
    private static bool IsNeutralTone(string syllable)
    {
        // 常见轻声词
        var neutralWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "de", "le", "me", "ne", "ge", "zi", "zhe", "ma"
        };

        return neutralWords.Contains(RemoveToneMarks(syllable).ToLowerInvariant());
    }

    [GeneratedRegex("([a-zA-Z]+)([0-4])")]
    private static partial Regex ToneMaskSingleRegex();
}