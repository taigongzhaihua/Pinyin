using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace TGZH.Pinyin;

internal static class TextUtils
{
    /// <summary>
    /// 提取字符周围的上下文（高性能优化版本）
    /// </summary>
    internal static string ExtractContext(string[] characters, int position, int range)
    {
        // 快速路径处理边界条件
        if (characters == null || characters.Length == 0 || position < 0 || position >= characters.Length)
            return string.Empty;

        // 如果范围为0，直接返回当前字符
        if (range <= 0)
            return characters[position] ?? string.Empty;

        // 预估结果大小以减少StringBuilder的重新分配
        var estimatedSize = Math.Min(range * 2 + 1, characters.Length) * 2;
        var result = new StringBuilder(estimatedSize);

        // 1. 添加当前位置的字符
        var currentChar = characters[position];
        if (!string.IsNullOrEmpty(currentChar))
        {
            result.Append(currentChar);
        }

        // 2. 向前提取（反向）- 利用优化版ContainsChinese
        var startPos = Math.Max(0, position - range);
        for (var i = position - 1; i >= startPos; i--)
        {
            var current = characters[i];
            if (string.IsNullOrEmpty(current))
                continue;

            // 使用优化版ContainsChinese进行检测
            if (!ChineseCharacterUtils.ContainsChinese(current))
                break; // 遇到非中文字符停止

            result.Insert(0, current);
        }

        // 3. 向后提取 - 同样利用优化版ContainsChinese
        var endPos = Math.Min(characters.Length - 1, position + range);
        for (var i = position + 1; i <= endPos; i++)
        {
            var current = characters[i];
            if (string.IsNullOrEmpty(current))
                continue;

            // 使用优化版ContainsChinese进行检测
            if (!ChineseCharacterUtils.ContainsChinese(current))
                break; // 遇到非中文字符停止

            result.Append(current);
        }

        return result.ToString();
    }

    /// <summary>
    /// 从上下文中提取所有可能包含目标字符的词语组合（统一接口）
    /// 支持所有Unicode平面上的汉字，包括扩展B-G区
    /// </summary>
    internal static List<string> ExtractPossibleWordContexts(string context, string targetChar, int maxLength)
    {
        if (string.IsNullOrEmpty(context) || string.IsNullOrEmpty(targetChar))
            return [];

        switch (targetChar.Length)
        {
            // 处理单个Unicode代码点的情况
            case 1:
                // 基本平面字符 - 使用单字符优化方法
                return ExtractPossibleWordContextsForSingleChar(context, targetChar[0], maxLength);
            case 2 when
                char.IsHighSurrogate(targetChar[0]) &&
                char.IsLowSurrogate(targetChar[1]):
                {
                    // 扩展平面字符 - 计算代码点并使用代码点方法
                    var codePoint = char.ConvertToUtf32(targetChar, 0);
                    return ExtractPossibleWordContextsForCodePoint(context, codePoint, maxLength);
                }
            default:
                // 处理多字符情况
                return ExtractPossibleWordContextsForMultiChar(context, targetChar, maxLength);
        }
    }

    /// <summary>
    /// 处理单字符情况的私有优化方法
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe List<string> ExtractPossibleWordContextsForSingleChar(string context, char targetChar, int maxLength)
    {
        if (string.IsNullOrEmpty(context))
            return [];

        // 预分配结果容量
        var results = new List<string>(maxLength * 2);

        // 首先定位目标字符
        var targetPos = -1;

        fixed (char* pContext = context)
        {
            var contextLength = context.Length;

            // 手动查找目标字符位置 - 对于单字符，这通常比IndexOf更快
            for (var i = 0; i < contextLength; i++)
            {
                if (pContext[i] != targetChar) continue;
                targetPos = i;
                break;
            }
        }

        if (targetPos < 0)
            return results;

        // 使用HashSet避免重复
        var uniqueWords = new HashSet<string>();

        fixed (char* pContext = context)
        {
            var contextLength = context.Length;

            for (var len = 2; len <= Math.Min(maxLength, contextLength); len++)
            {
                // 优化循环范围 - 只考虑包含目标字符的子串
                for (var start = Math.Max(0, targetPos - len + 1);
                     start <= targetPos && start + len <= contextLength;
                     start++)
                {
                    // 从指针直接创建字符串，避免Substring调用
                    var word = new string(pContext + start, 0, len);

                    if (uniqueWords.Add(word))
                    {
                        results.Add(word);
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 处理扩展Unicode平面上的单个代码点（私有方法）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe List<string> ExtractPossibleWordContextsForCodePoint(string context, int targetCodePoint, int maxLength)
    {
        if (string.IsNullOrEmpty(context))
            return [];

        // 预分配结果容量
        var results = new List<string>(maxLength * 2);

        // 查找目标代码点的位置
        var targetPos = -1;

        fixed (char* pContext = context)
        {
            var contextLength = context.Length;

            for (var i = 0; i < contextLength;)
            {
                int codePoint;
                var charCount = 1;

                // 检查是否是代理对
                if (i < contextLength - 1 && char.IsHighSurrogate(pContext[i]) && char.IsLowSurrogate(pContext[i + 1]))
                {
                    codePoint = ((pContext[i] - 0xD800) << 10) + (pContext[i + 1] - 0xDC00) + 0x10000;
                    charCount = 2;
                }
                else
                {
                    codePoint = pContext[i];
                }

                if (codePoint == targetCodePoint)
                {
                    targetPos = i;
                    break;
                }

                i += charCount;
            }
        }

        if (targetPos < 0)
            return results;

        // 使用HashSet避免重复
        var uniqueWords = new HashSet<string>();

        fixed (char* pContext = context)
        {
            var contextLength = context.Length;

            // 计算目标字符的宽度（普通字符为1，代理对为2）
            var targetCharWidth = 1;
            if (targetPos < contextLength - 1 &&
                char.IsHighSurrogate(pContext[targetPos]) &&
                char.IsLowSurrogate(pContext[targetPos + 1]))
            {
                targetCharWidth = 2;
            }

            for (var len = 2; len <= Math.Min(maxLength, contextLength); len++)
            {
                // 优化循环范围 - 只考虑包含目标字符的子串
                for (var start = Math.Max(0, targetPos - len + 1);
                     start <= targetPos && start + len <= contextLength;
                     start++)
                {
                    // 确保子串完全包含目标字符（包括可能的代理对）
                    if (targetPos + targetCharWidth > start + len) continue;
                    // 直接从指针创建字符串
                    var word = new string(pContext + start, 0, len);

                    if (uniqueWords.Add(word))
                    {
                        results.Add(word);
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 处理多字符词语的私有方法
    /// </summary>
    private static unsafe List<string> ExtractPossibleWordContextsForMultiChar(string context, string targetChar, int maxLength)
    {
        // 预分配结果容量
        var estimatedCapacity = Math.Min(maxLength * 2, context.Length);
        var results = new List<string>(estimatedCapacity);

        // 查找目标字符串位置
        var targetPos = context.IndexOf(targetChar, StringComparison.Ordinal);
        if (targetPos < 0)
            return results;

        // 使用HashSet避免重复
        var uniqueWords = new HashSet<string>();

        var targetLength = targetChar.Length;
        var contextLength = context.Length;
        var actualMaxLength = Math.Min(maxLength, contextLength);

        fixed (char* pContext = context)
        {
            for (var len = Math.Max(2, targetLength); len <= actualMaxLength; len++)
            {
                // 优化起始位置计算 - 以目标字符串为中心
                var minStart = Math.Max(0, targetPos - len + targetLength);
                var maxStart = Math.Min(targetPos, contextLength - len);

                for (var start = minStart; start <= maxStart; start++)
                {
                    // 确保子串完全位于字符串范围内
                    if (start + len > contextLength)
                        continue;

                    // 检查子串是否包含目标位置
                    if (targetPos < start || targetPos + targetLength > start + len) continue;
                    var word = new string(pContext + start, 0, len);
                    if (uniqueWords.Add(word))
                    {
                        results.Add(word);
                    }
                }
            }
        }

        return results;
    }
}