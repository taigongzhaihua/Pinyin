using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace TGZH.Pinyin;

/// <summary>
/// 简单文本分词器（C#13高性能版本）
/// </summary>
internal static class SimpleTextSegmenter
{
    /// <summary>
    /// 对文本进行简单分词（unsafe优化版本）
    /// </summary>
    public static unsafe string[] Segment(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        // 预分配合理大小
        var estimatedCapacity = Math.Max(4, text.Length / 5);
        var segments = new List<string>(estimatedCapacity);

        var textLength = text.Length;
        if (textLength == 0)
            return [];

        fixed (char* pText = text)
        {
            var start = 0;
            var isCurrentChinese = ChineseCharacterUtils.IsChineseCodePoint(text, 0);

            for (var i = 0; i < textLength;)
            {
                // 使用指针直接检查代理对
                var charSize = 1;
                if (i < textLength - 1 && char.IsHighSurrogate(pText[i]) && char.IsLowSurrogate(pText[i + 1]))
                    charSize = 2;

                var nextIndex = i + charSize;

                var isEndOfText = nextIndex >= textLength;
                var isTypeChanged = false;

                if (!isEndOfText)
                    isTypeChanged = ChineseCharacterUtils.IsChineseCodePoint(text, nextIndex) != isCurrentChinese;

                if (isEndOfText || isTypeChanged)
                {
                    // 计算片段长度
                    var length = nextIndex - start;

                    // 使用指针高效创建字符串
                    var segment = CreateStringFromPointer(pText + start, length);
                    segments.Add(segment);

                    if (!isEndOfText)
                    {
                        start = nextIndex;
                        isCurrentChinese = !isCurrentChinese; // 类型已经改变
                    }
                }

                i = nextIndex;
            }
        }

        return [.. segments];
    }

    /// <summary>
    /// 将字符串分割为字符数组，中文单独成元素，连续非中文合并为一个元素（unsafe优化版本）
    /// </summary>
    public static unsafe string[] SplitToChars(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        // 预分配容量
        var estimatedCapacity = Math.Max(8, text.Length / 3);
        var result = new List<string>(estimatedCapacity);

        // 使用栈分配小缓冲区，避免堆分配
        var initialBufferSize = Math.Min(512, text.Length);
        var stackBuffer = stackalloc char[initialBufferSize];
        var bufferPos = 0;

        fixed (char* pText = text)
        {
            var textLength = text.Length;

            for (var i = 0; i < textLength;)
            {
                // 使用指针直接检测代理对
                var isSurrogatePair = i + 1 < textLength &&
                                      char.IsHighSurrogate(pText[i]) &&
                                      char.IsLowSurrogate(pText[i + 1]);

                var charLength = isSurrogatePair ? 2 : 1;
                var isCurrentChinese = ChineseCharacterUtils.IsChineseCodePoint(text, i);

                if (isCurrentChinese)
                {
                    // 先处理缓冲区中的非中文字符
                    if (bufferPos > 0)
                    {
                        result.Add(new string(stackBuffer, 0, bufferPos));
                        bufferPos = 0;
                    }

                    // 中文字符直接添加 - 使用指针创建
                    // 单个字符转字符串
                    result.Add(isSurrogatePair ? new string(pText + i, 0, 2) : pText[i].ToString());
                }
                else
                {
                    // 检查栈缓冲区是否足够
                    if (bufferPos + charLength > initialBufferSize)
                    {
                        // 缓冲区已满，添加到结果并清空
                        result.Add(new string(stackBuffer, 0, bufferPos));
                        bufferPos = 0;
                    }

                    // 使用指针直接复制到栈缓冲区
                    stackBuffer[bufferPos++] = pText[i];
                    if (isSurrogatePair)
                    {
                        stackBuffer[bufferPos++] = pText[i + 1];
                    }
                }

                i += charLength;
            }

            // 处理剩余的非中文字符
            if (bufferPos > 0)
            {
                result.Add(new string(stackBuffer, 0, bufferPos));
            }
        }

        return [.. result];
    }
    /// <summary>
    /// 从字符指针高效创建字符串
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe string CreateStringFromPointer(char* ptr, int length)
    {
        // 小字符串使用new string(char*, int)构造函数
        // .NET内部会针对这种方法进行特别优化
        return new string(ptr, 0, length);
    }

}