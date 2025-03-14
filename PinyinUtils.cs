using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TGZH.Pinyin;

/// <summary>
/// 拼音实用工具类
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static partial class PinyinUtils
{
    /// <summary>
    /// 比较两个拼音字符串的相似度
    /// </summary>
    /// <param name="pinyin1">拼音1</param>
    /// <param name="pinyin2">拼音2</param>
    /// <returns>相似度（0-1）</returns>
    public static double CompareSimilarity(string pinyin1, string pinyin2)
    {
        if (string.IsNullOrEmpty(pinyin1) && string.IsNullOrEmpty(pinyin2))
            return 1.0;

        if (string.IsNullOrEmpty(pinyin1) || string.IsNullOrEmpty(pinyin2))
            return 0.0;

        // 移除声调并转为小写以便比较
        var s1 = EnhancedPinyinConverter.RemoveToneMarks(pinyin1).ToLower();
        var s2 = EnhancedPinyinConverter.RemoveToneMarks(pinyin2).ToLower();

        // 计算编辑距离
        var editDistance = ComputeLevenshteinDistance(s1, s2);
        var maxLength = Math.Max(s1.Length, s2.Length);

        return 1.0 - (double)editDistance / maxLength;
    }

    /// <summary>
    /// 计算莱温斯坦编辑距离
    /// </summary>
    private static int ComputeLevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s))
            return string.IsNullOrEmpty(t) ? 0 : t.Length;

        if (string.IsNullOrEmpty(t))
            return s.Length;

        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];

        // 初始化
        for (var i = 0; i <= n; i++)
            d[i, 0] = i;

        for (var j = 0; j <= m; j++)
            d[0, j] = j;

        // 计算
        for (var j = 1; j <= m; j++)
        {
            for (var i = 1; i <= n; i++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    /// <summary>
    /// 将非标准拼音修正为标准拼音
    /// </summary>
    public static string NormalizeToStandardPinyin(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin))
            return string.Empty;

        // 将可能的拼音变体转换为标准拼音
        pinyin = pinyin.Replace("v", "ü").Replace("V", "Ü");

        // 自动添加必要的分隔符
        var parts = SpacesRegex().Split(pinyin).Where(p => !string.IsNullOrEmpty(p)).ToArray();

        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = CorrectPinyinSyllable(parts[i]);
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// 修正单个拼音音节
    /// </summary>
    private static string CorrectPinyinSyllable(string syllable)
    {
        // 常见的拼写错误修正
        var corrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"shi", "shī"}, {"si", "sī"}, {"zi", "zī"}, {"ci", "cī"}, {"ji", "jī"},
            {"zhi", "zhī"}, {"chi", "chī"}, {"xi", "xī"}, {"ri", "rī"}
            // 可以添加更多常见错误修正...
        };

        return corrections.GetValueOrDefault(syllable, syllable);
    }

    /// <summary>
    /// 判断是否符合拼音规则
    /// </summary>
    public static bool IsValidPinyin(string pinyin)
    {
        if (string.IsNullOrEmpty(pinyin))
            return false;

        // 移除所有空白字符
        pinyin = SpaceRegex().Replace(pinyin, "");

        // 检查是否只包含合法拼音字符
        return PinyinValidRegex().IsMatch(pinyin);
    }

    /// <summary>
    /// 获取所有汉字的拼音首字母
    /// </summary>
    public static async Task<string> GetInitialsAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = new StringBuilder();

        foreach (var c in text)
        {
            if (ChineseCharacterUtils.IsChineseChar(c))
            {
                // 获取拼音并提取首字母
                var pinyins = await UnifiedPinyinApi.GetCharPinyinAsync(c, PinyinFormat.WithoutTone);
                if (pinyins is { Length: > 0 } && !string.IsNullOrEmpty(pinyins[0]))
                {
                    result.Append(pinyins[0][0]);
                }
            }
            else
            {
                // 非中文字符保持不变
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// 将带有拼音首字母的文本进行模糊匹配
    /// </summary>
    public static async Task<bool> MatchesPinyinPattern(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern))
            return false;

        // 获取文本的拼音首字母
        var initials = await GetInitialsAsync(text);

        // 将pattern视为正则表达式进行匹配
        try
        {
            return Regex.IsMatch(initials, pattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            // 如果pattern不是有效的正则表达式，则进行简单的包含匹配
            return initials.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 检查一个文本是否包含另一个文本的拼音
    /// </summary>
    public static async Task<bool> ContainsPinyinAsync(string text, string search)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search))
            return false;

        // 将文本转换为拼音（不带声调）
        var textPinyin = await UnifiedPinyinApi.GetTextPinyinAsync(text, PinyinFormat.WithoutTone, "");
        var searchPinyin = await UnifiedPinyinApi.GetTextPinyinAsync(search, PinyinFormat.WithoutTone, "");

        return textPinyin.Contains(searchPinyin, StringComparison.OrdinalIgnoreCase);
    }

    // 正则表达式：多个空白字符
    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();

    // 正则表达式：空白字符
    [GeneratedRegex(@"\s")]
    private static partial Regex SpaceRegex();

    // 正则表达式：合法拼音字符串
    [GeneratedRegex("^(([bpmfdthknljqxzcsryw]|[zcs]h)?[iu]?([aāáǎà][on]?|[eēéěè][inr]?|[aāáǎàeēéěè]ng|[au]?[iīíǐì]|[iīíǐì]ng?|((?:jqxy)[uūúǔù]|[üǖǘǚǜv])e?|i?[uūúǔù]|[oōóǒò]u?)[0-9]?)+$", RegexOptions.IgnoreCase)]
    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    private static partial Regex PinyinValidRegex();
}