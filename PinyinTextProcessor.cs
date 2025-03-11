using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace TGZH.Pinyin;

/// <summary>
/// 拼音文本处理器 - 提供高级文本处理功能
/// </summary>
/// <remarks>
/// 创建拼音文本处理器
/// </remarks>
internal class PinyinTextProcessor(OptimizedPinyinDatabase database)
{
    private readonly OptimizedPinyinDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private PinyinServiceOptions _options;
    private bool _isInitialized;

    // 常见的多音字上下文映射
    private readonly Dictionary<string, Dictionary<char, string>> _contextMapping =
        [];

    // 分词器（简单实现）
    private readonly SimpleTextSegmenter _segmenter = new();

    /// <summary>
    /// 初始化文本处理器
    /// </summary>
    public Task InitializeAsync(PinyinServiceOptions options)
    {
        _options = options ?? new PinyinServiceOptions();
        InitializePolyphoneContext();
        _isInitialized = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 初始化常见多音字上下文映射
    /// </summary>
    private void InitializePolyphoneContext()
    {
        // 根据上下文确定多音字读音的规则
        // 格式: 上下文词组 -> 多音字 -> 对应读音

        // 处理"长"字
        _contextMapping["长江"] = new Dictionary<char, string> { { '长', "cháng" } };
        _contextMapping["长城"] = new Dictionary<char, string> { { '长', "cháng" } };
        _contextMapping["长度"] = new Dictionary<char, string> { { '长', "cháng" } };
        _contextMapping["长短"] = new Dictionary<char, string> { { '长', "cháng" } };
        _contextMapping["成长"] = new Dictionary<char, string> { { '长', "zhǎng" } };
        _contextMapping["生长"] = new Dictionary<char, string> { { '长', "zhǎng" } };
        _contextMapping["长大"] = new Dictionary<char, string> { { '长', "zhǎng" } };

        // 处理"乐"字
        _contextMapping["音乐"] = new Dictionary<char, string> { { '乐', "yuè" } };
        _contextMapping["乐器"] = new Dictionary<char, string> { { '乐', "yuè" } };
        _contextMapping["乐谱"] = new Dictionary<char, string> { { '乐', "yuè" } };
        _contextMapping["快乐"] = new Dictionary<char, string> { { '乐', "lè" } };
        _contextMapping["娱乐"] = new Dictionary<char, string> { { '乐', "lè" } };

        // 处理"行"字
        _contextMapping["行走"] = new Dictionary<char, string> { { '行', "xíng" } };
        _contextMapping["行为"] = new Dictionary<char, string> { { '行', "xíng" } };
        _contextMapping["行列"] = new Dictionary<char, string> { { '行', "háng" } };
        _contextMapping["银行"] = new Dictionary<char, string> { { '行', "háng" } };
        _contextMapping["行业"] = new Dictionary<char, string> { { '行', "háng" } };

        // 添加更多常见多音字的处理规则...
    }

    /// <summary>
    /// 获取文本的拼音
    /// </summary>
    public async Task<string> GetTextPinyinAsync(string text, PinyinFormat format, string separator)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (!_isInitialized)
            throw new InvalidOperationException("文本处理器未初始化");

        var result = new StringBuilder();

        if (_options.UseWordFirstConversion)
        {
            // 基于词语的转换（考虑多音字）
            await ProcessWithWordFirst(text, format, separator, result);
        }
        else
        {
            // 逐字转换
            await ProcessCharByChar(text, format, separator, result);
        }

        return result.ToString();
    }

    /// <summary>
    /// 基于词语的文本处理（优化后支持所有Unicode平面）
    /// </summary>
    private async Task ProcessWithWordFirst(string text, PinyinFormat format, string separator, StringBuilder result)
    {
        // 分词
        var segments = SimpleTextSegmenter.Segment(text);
        var nonChineseBuffer = new StringBuilder();
        var isFirstAppend = true;

        foreach (var segment in segments)
        {
            if (segment.Length > 1 && IsChineseAt(segment, 0))
            {
                // 处理之前缓冲的非中文字符
                if (nonChineseBuffer.Length > 0)
                {
                    if (!isFirstAppend) result.Append(separator);
                    result.Append(nonChineseBuffer);
                    nonChineseBuffer.Clear();
                    isFirstAppend = false;
                }

                // 先尝试作为词语处理
                var wordPinyin = await _database.GetWordPinyinAsync(segment, format);

                if (!string.IsNullOrEmpty(wordPinyin))
                {
                    // 使用词语的拼音
                    if (!isFirstAppend) result.Append(separator);
                    switch (format)
                    {
                        case PinyinFormat.FirstLetter:
                            var wordResult = "";
                            foreach (var c in wordPinyin)
                            {
                                if (wordResult.Length > 0) wordResult += separator;
                                wordResult += c;
                            }
                            result.Append(wordResult);
                            break;
                        default:
                            result.Append(wordPinyin.Replace(" ", separator));
                            break;
                    }
                    isFirstAppend = false;
                    continue;
                }

                // 逐字符处理
                var segmentNonChineseBuffer = new StringBuilder();
                var isFirstInSegment = true;

                for (var i = 0; i < segment.Length; i++)
                {
                    if (IsChineseAt(segment, i))
                    {
                        // 处理之前缓冲的非中文字符
                        if (segmentNonChineseBuffer.Length > 0)
                        {
                            if (!isFirstAppend || !isFirstInSegment) result.Append(separator);
                            result.Append(segmentNonChineseBuffer);
                            segmentNonChineseBuffer.Clear();
                            isFirstAppend = false;
                            isFirstInSegment = false;
                        }

                        var c = segment[i];
                        string[] pinyins;

                        // 使用上下文智能判断多音字
                        if (_options.UseSmartPolyphoneHandling)
                        {
                            pinyins = await GetContextAwarePinyinAsync(segment, i, format);
                        }
                        else
                        {
                            pinyins = await _database.GetCharPinyinAsync(c, format);
                        }

                        if (pinyins is { Length: > 0 })
                        {
                            if (!isFirstAppend || !isFirstInSegment) result.Append(separator);
                            result.Append(pinyins[0]); // 使用第一个拼音
                            isFirstAppend = false;
                            isFirstInSegment = false;
                        }

                        // 如果是代理对，跳过第二个char
                        if (char.IsHighSurrogate(c) && i + 1 < segment.Length && char.IsLowSurrogate(segment[i + 1]))
                        {
                            i++;
                        }
                    }
                    // 处理非中文字符
                    else if (_options.PreserveNonChinese)
                    {
                        segmentNonChineseBuffer.Append(segment[i]);
                    }
                }

                // 处理段落内剩余的非中文字符
                if (segmentNonChineseBuffer.Length > 0)
                {
                    if (!isFirstAppend || !isFirstInSegment) result.Append(separator);
                    result.Append(segmentNonChineseBuffer);
                    isFirstAppend = false;
                }
            }
            // 处理完全非中文的段落
            else if (_options.PreserveNonChinese)
            {
                nonChineseBuffer.Append(segment);
            }
        }

        // 处理最后剩余的非中文字符
        if (nonChineseBuffer.Length > 0)
        {
            if (!isFirstAppend) result.Append(separator);
            result.Append(nonChineseBuffer);
        }
    }

    /// <summary>
    /// 逐字处理文本（优化后支持所有Unicode平面）
    /// </summary>
    private async Task ProcessCharByChar(string text, PinyinFormat format, string separator, StringBuilder result)
    {
        var nonChineseBuffer = new StringBuilder();
        var isFirstAppend = true;

        for (var i = 0; i < text.Length; i++)
        {
            if (IsChineseAt(text, i))
            {
                // 如果有缓冲的非中文字符，先处理它们
                if (nonChineseBuffer.Length > 0)
                {
                    if (!isFirstAppend) result.Append(separator);
                    result.Append(nonChineseBuffer);
                    nonChineseBuffer.Clear();
                    isFirstAppend = false;
                }

                var c = text[i];
                var pinyins = await _database.GetCharPinyinAsync(c, format);

                if (pinyins is { Length: > 0 })
                {
                    if (!isFirstAppend) result.Append(separator);
                    result.Append(pinyins[0]); // 使用第一个拼音
                    isFirstAppend = false;
                }

                // 如果是代理对，跳过第二个char
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                }
            }
            else if (_options.PreserveNonChinese)
            {
                // 缓冲非中文字符，等待处理完连续的非中文字符后再一次性添加
                nonChineseBuffer.Append(text[i]);
            }
        }

        // 处理最后剩余的非中文字符
        if (nonChineseBuffer.Length > 0 && _options.PreserveNonChinese)
        {
            if (!isFirstAppend) result.Append(separator);
            result.Append(nonChineseBuffer);
        }
    }

    /// <summary>
    /// 基于上下文获取多音字的拼音
    /// </summary>
    private async Task<string[]> GetContextAwarePinyinAsync(string context, int position, PinyinFormat format)
    {
        var c = context[position];

        // 获取所有可能的读音
        var allPinyins = await _database.GetCharPinyinAsync(c, PinyinFormat.WithToneMark);

        // 如果不是多音字，直接返回
        if (allPinyins.Length <= 1)
        {
            // 如果需要其他格式，转换格式
            return format != PinyinFormat.WithToneMark ? [ConvertPinyinFormat(allPinyins[0], format)] : allPinyins;
        }

        // 尝试在上下文映射中查找
        string bestPinyin = null;

        // 查找包含当前字的词语
        foreach (var (key, value) in _contextMapping)
        {
            // 判断当前上下文是否包含这个词语
            if (!context.Contains(key)) continue;
            // 检查当前字在词语中的位置
            var keyPosition = context.IndexOf(key, StringComparison.Ordinal);
            var relativePos = position - keyPosition;

            // 如果字符在词语范围内
            if (relativePos < 0 || relativePos >= key.Length || key[relativePos] != c ||
                !value.TryGetValue(c, out var contextPinyin)) continue;
            bestPinyin = contextPinyin;
            break;
        }

        // 如果找到上下文特定读音
        if (!string.IsNullOrEmpty(bestPinyin))
        {
            if (format != PinyinFormat.WithToneMark)
            {
                return [ConvertPinyinFormat(bestPinyin, format)];
            }
            return [bestPinyin];
        }

        // 如果没找到上下文，但要转换格式
        if (format == PinyinFormat.WithToneMark) return allPinyins;
        var result = new string[allPinyins.Length];
        for (var i = 0; i < allPinyins.Length; i++)
        {
            result[i] = ConvertPinyinFormat(allPinyins[i], format);
        }
        return result;

    }

    /// <summary>
    /// 转换拼音格式
    /// </summary>
    private static string ConvertPinyinFormat(string pinyin, PinyinFormat targetFormat)
    {
        if (string.IsNullOrEmpty(pinyin))
            return pinyin;

        return targetFormat switch
        {
            PinyinFormat.WithoutTone => EnhancedPinyinConverter.RemoveToneMarks(pinyin),
            PinyinFormat.WithToneNumber => EnhancedPinyinConverter.ToToneNumber(pinyin),
            PinyinFormat.FirstLetter => EnhancedPinyinConverter.RemoveToneMarks(pinyin)[0].ToString(),
            _ => pinyin
        };
    }

    /// <summary>
    /// 处理大型文本
    /// </summary>
    public async Task<string> ProcessLargeTextAsync(string text, PinyinFormat format, string separator)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // 对于大文本，采用分块处理策略
        const int blockSize = 1000;

        if (text.Length <= blockSize)
        {
            return await GetTextPinyinAsync(text, format, separator);
        }

        var result = new StringBuilder(text.Length * 2); // 预估结果长度

        // 分块处理
        for (var i = 0; i < text.Length; i += blockSize)
        {
            var length = Math.Min(blockSize, text.Length - i);
            var block = text.Substring(i, length);

            // 处理一个块
            var blockResult = await GetTextPinyinAsync(block, format, separator);
            result.Append(blockResult);

            // 如果不是最后一块，且结果不为空，添加分隔符
            if (i + blockSize < text.Length && result.Length > 0 && !result.ToString().EndsWith(separator))
            {
                result.Append(separator);
            }
        }

        return result.ToString();
    }
    /// <summary>
    /// 获取文本中每个字符的拼音
    /// </summary>
    public async Task<string[]> GetTextCharactersPinyinAsync(string text, PinyinFormat format)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        if (!_isInitialized)
            throw new InvalidOperationException("文本处理器未初始化");

        // 创建结果数组，每个字符对应一个拼音
        var result = new string[text.Length];

        // 提取所有唯一汉字
        var uniqueChars = new HashSet<char>();
        foreach (var c in text)
        {
            if (IsChinese(c))
            {
                uniqueChars.Add(c);
            }
        }

        // 批量查询所有汉字的拼音
        var charPinyins = await _database.GetCharsPinyinBatchAsync([.. uniqueChars], format);

        // 填充结果数组
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsChinese(c) && charPinyins.TryGetValue(c, out var pinyins) && pinyins.Length > 0)
            {
                result[i] = pinyins[0]; // 使用第一个拼音
            }
            else
            {
                // 非汉字或未找到拼音，使用原字符
                result[i] = c.ToString();
            }
        }

        return result;
    }

    /// <summary>
    /// 分块处理大文本
    /// </summary>
    public async Task<string> ProcessLargeTextBatchAsync(string text, PinyinFormat format, string separator)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // 对于大文本，采用分块策略
        const int blockSize = 2000; // 每次处理的字符数

        if (text.Length <= blockSize)
        {
            // 小文本直接处理
            return await GetTextPinyinAsync(text, format, separator);
        }

        // 为大文本分配缓冲区
        var result = new StringBuilder(text.Length * 3); // 预估最终长度

        // 分块处理
        for (var i = 0; i < text.Length; i += blockSize)
        {
            // 计算当前块的长度
            var length = Math.Min(blockSize, text.Length - i);

            // 截取当前块
            var block = text.Substring(i, length);

            // 处理当前块
            var blockResult = await GetTextPinyinAsync(block, format, separator);

            // 添加到结果，确保分隔符处理正确
            if (i > 0 && result.Length > 0 && !result.ToString().EndsWith(separator))
            {
                result.Append(separator);
            }

            result.Append(blockResult);
        }

        return result.ToString();
    }
    /// <summary>
    /// 判断字符是否是中文
    /// </summary>
    private static bool IsChinese(char c)
    {
        // 对于单个字符，只能判断基本汉字和扩展A区
        return (c >= 0x4E00 && c <= 0x9FFF) ||    // 基本汉字
               (c >= 0x3400 && c <= 0x4DBF);      // 扩展A区
    }

    /// <summary>
    /// 判断指定位置的字符是否是中文（支持扩展B区及更高区域）
    /// </summary>
    private static bool IsChineseAt(string text, int index)
    {
        return ChineseCharacterUtils.IsChineseCodePoint(text, index);
    }

}

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
}