using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace TGZH.Pinyin;

/// <summary>
/// 提供处理中文字符的通用方法
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class ChineseCharacterUtils
{
    /// <summary>
    /// 判断字符是否是基本汉字或扩展A区汉字（单个char可以表示的范围）
    /// </summary>
    public static bool IsChineseChar(char c)
    {
        return (c >= 0x4E00 && c <= 0x9FFF) ||    // 基本汉字
               (c >= 0x3400 && c <= 0x4DBF);      // 扩展A区
    }

    /// <summary>
    /// 判断指定位置的Unicode代码点是否是中文字符（支持所有Unicode平面）
    /// </summary>
    public static bool IsChineseCodePoint(string text, int index)
    {
        if (string.IsNullOrEmpty(text) || index < 0 || index >= text.Length)
            return false;

        int codePoint;

        // 处理可能的代理对
        if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            codePoint = char.ConvertToUtf32(text, index);
        }
        else
        {
            codePoint = text[index];
        }

        return codePoint is >= 0x4E00 and <= 0x9FFF ||    // 基本汉字
               codePoint is >= 0x3400 and <= 0x4DBF ||    // 扩展A区
               codePoint is >= 0x20000 and <= 0x2A6DF ||  // 扩展B区
               codePoint is >= 0x2A700 and <= 0x2B73F ||  // 扩展C区
               codePoint is >= 0x2B740 and <= 0x2B81F ||  // 扩展D区
               codePoint is >= 0x2B820 and <= 0x2CEAF ||  // 扩展E区
               codePoint is >= 0x2CEB0 and <= 0x2EBEF ||  // 扩展F区
               codePoint is >= 0x30000 and <= 0x3134F;    // 扩展G区
    }

    /// <summary>
    /// 以Unicode代码点方式遍历字符串中的每个字符
    /// </summary>
    public static void ForEachCodePoint(string text, Action<int, int, int> action)
    {
        if (string.IsNullOrEmpty(text) || action == null)
            return;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        var elementIndex = 0;

        while (enumerator.MoveNext())
        {
            var index = enumerator.ElementIndex;
            var element = enumerator.GetTextElement();

            var codePoint = element.Length switch
            {
                1 => element[0],
                2 when char.IsHighSurrogate(element[0]) && char.IsLowSurrogate(element[1]) => char.ConvertToUtf32(
                    element, 0),
                _ => -1
            };

            action(elementIndex++, index, codePoint);
        }
    }

    /// <summary>
    /// 以代码点的方式计算文本中的中文字符数量
    /// </summary>
    public static int CountChineseCharacters(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (!IsChineseCodePoint(text, i)) continue;
            count++;

            // 跳过代理对的第二个字符
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
        }

        return count;
    }

    /// <summary>
    /// 判断文本是否包含中文字符
    /// </summary>
    public static bool ContainsChinese(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        for (var i = 0; i < text.Length; i++)
        {
            if (IsChineseCodePoint(text, i))
                return true;

            // 跳过代理对的第二个字符
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
        }

        return false;
    }
}