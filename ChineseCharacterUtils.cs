using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace TGZH.Pinyin;

/// <summary>
/// 提供处理中文字符的通用方法（unsafe 高性能版本）
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class ChineseCharacterUtils
{
    /// <summary>
    /// 判断字符是否是基本汉字或扩展A区汉字（单个char可以表示的范围）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsChineseChar(char c)
    {
        return (c >= 0x4E00 && c <= 0x9FFF) ||    // 基本汉字
               (c >= 0x3400 && c <= 0x4DBF);      // 扩展A区
    }

    /// <summary>
    /// 判断指定位置的Unicode代码点是否是中文字符（支持所有Unicode平面）
    /// </summary>
    public static unsafe bool IsChineseCodePoint(string text, int index)
    {
        if (string.IsNullOrEmpty(text) || (uint)index >= (uint)text.Length)
            return false;

        int codePoint;

        fixed (char* pText = text)
        {
            // 处理可能的代理对
            if (index < text.Length - 1 && char.IsHighSurrogate(pText[index]) && char.IsLowSurrogate(pText[index + 1]))
            {
                codePoint = ((pText[index] - 0xD800) << 10) + (pText[index + 1] - 0xDC00) + 0x10000;
            }
            else
            {
                codePoint = pText[index];
            }
        }

        // 使用范围模式匹配检查是否是汉字
        return IsChineseCodePointInternal(codePoint);
    }

    /// <summary>
    /// 内部方法：根据代码点检查是否是汉字
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsChineseCodePointInternal(int codePoint)
    {
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
    /// 以Unicode代码点方式遍历字符串中的每个字符（unsafe高性能版本）
    /// </summary>
    public static unsafe void ForEachCodePoint(string text, Action<int, int, int> action)
    {
        if (string.IsNullOrEmpty(text) || action == null)
            return;

        fixed (char* pText = text)
        {
            var textLength = text.Length;
            var elementIndex = 0;

            for (var i = 0; i < textLength;)
            {
                int codePoint;
                var charCount = 1;

                // 检查是否是代理对
                if (i < textLength - 1 && char.IsHighSurrogate(pText[i]) && char.IsLowSurrogate(pText[i + 1]))
                {
                    codePoint = ((pText[i] - 0xD800) << 10) + (pText[i + 1] - 0xDC00) + 0x10000;
                    charCount = 2;
                }
                else
                {
                    codePoint = pText[i];
                }

                // 调用用户提供的委托
                action(elementIndex++, i, codePoint);

                // 前进到下一个字符/代码点
                i += charCount;
            }
        }
    }

    /// <summary>
    /// 以代码点的方式计算文本中的中文字符数量（unsafe高性能版本）
    /// </summary>
    public static unsafe int CountChineseCharacters(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;

        fixed (char* pText = text)
        {
            var textLength = text.Length;

            for (var i = 0; i < textLength;)
            {
                int codePoint;

                // 检查是否是代理对
                if (i < textLength - 1 && char.IsHighSurrogate(pText[i]) && char.IsLowSurrogate(pText[i + 1]))
                {
                    codePoint = ((pText[i] - 0xD800) << 10) + (pText[i + 1] - 0xDC00) + 0x10000;

                    // 检查是否是中文
                    if (IsChineseCodePointInternal(codePoint))
                        count++;

                    // 跳过代理对的第二个字符
                    i += 2;
                }
                else
                {
                    codePoint = pText[i];

                    // 检查是否是中文
                    if (IsChineseCodePointInternal(codePoint))
                        count++;

                    i++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// 判断文本是否包含中文字符（unsafe高性能版本）
    /// </summary>
    public static unsafe bool ContainsChinese(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        fixed (char* pText = text)
        {
            var textLength = text.Length;

            for (var i = 0; i < textLength;)
            {
                int codePoint;

                // 检查是否是代理对
                if (i < textLength - 1 && char.IsHighSurrogate(pText[i]) && char.IsLowSurrogate(pText[i + 1]))
                {
                    codePoint = ((pText[i] - 0xD800) << 10) + (pText[i + 1] - 0xDC00) + 0x10000;

                    // 发现中文则立即返回true
                    if (IsChineseCodePointInternal(codePoint))
                        return true;

                    // 跳过代理对的第二个字符
                    i += 2;
                }
                else
                {
                    codePoint = pText[i];

                    // 发现中文则立即返回true
                    if (IsChineseCodePointInternal(codePoint))
                        return true;

                    i++;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 手动计算UTF-32代码点（比char.ConvertToUtf32更快的实现）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ConvertToUtf32Fast(char highSurrogate, char lowSurrogate)
    {
        return ((highSurrogate - 0xD800) << 10) + (lowSurrogate - 0xDC00) + 0x10000;
    }
}