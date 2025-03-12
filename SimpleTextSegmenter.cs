using System.Collections.Generic;
using System.Text;

namespace TGZH.Pinyin;

/// <summary>
/// 简单文本分词器
/// </summary>
internal class SimpleTextSegmenter
{
    /// <summary>
    /// 对文本进行简单分词（优化支持所有Unicode平面）
    /// </summary>
    public static string[] Segment(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var segments = new List<string>();
        var currentSegment = new StringBuilder();
        var isCurrentChinese = false;

        for (var i = 0; i < text.Length; i++)
        {
            var isChinese = ChineseCharacterUtils.IsChineseCodePoint(text, i);

            // 字符类型变化时分段
            if (i > 0 && isChinese != isCurrentChinese)
            {
                segments.Add(currentSegment.ToString());
                currentSegment.Clear();
            }

            currentSegment.Append(text[i]);

            // 如果是代理对，添加第二个字符并前进索引
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                currentSegment.Append(text[i + 1]);
                i++;
            }

            isCurrentChinese = isChinese;
        }

        // 添加最后一段
        if (currentSegment.Length > 0)
        {
            segments.Add(currentSegment.ToString());
        }

        return [.. segments];
    }

    // 将字符串分割为单个字符，支持所有Unicode平面
    public static string[] SplitToChars(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        var chars = new string[text.Length];
        var index = 0;
        for (var i = 0; i < text.Length; i++)
        {
            chars[index++] = text[i].ToString();
            // 如果是代理对，添加第二个字符并前进索引
            if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) continue;
            chars[index++] = text[i + 1].ToString();
            i++;
        }
        return chars;
    }

}