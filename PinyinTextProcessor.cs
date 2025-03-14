using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TGZH.Pinyin;

/// <summary>
/// 拼音文本处理器 - 提供高级文本处理功能
/// </summary>
/// <remarks>
/// 创建拼音文本处理器
/// </remarks>
internal partial class PinyinTextProcessor(OptimizedPinyinDatabase database)
{
    private readonly OptimizedPinyinDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private PinyinServiceOptions _options;
    private bool _isInitialized;


    /// <summary>
    /// 初始化文本处理器
    /// </summary>
    public Task InitializeAsync(PinyinServiceOptions options)
    {
        _options = options ?? new PinyinServiceOptions();
        _isInitialized = true;
        return Task.CompletedTask;
    }


    /// <summary>
    /// 获取文本的拼音（优化批量处理版本）
    /// </summary>
    public async Task<string> GetTextPinyinAsync(string text, PinyinFormat format, string separator)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (!_isInitialized)
            throw new InvalidOperationException("文本处理器未初始化");

        // 1. 使用分词器拆分成单字符数组
        var characters = SimpleTextSegmenter.SplitToChars(text);

        // 2. 识别中文字符并去重
        var uniqueChineseChars = characters
            .Where(c => ChineseCharacterUtils.IsChineseCodePoint(c, index: 0))
            .Distinct()
            .ToArray();

        // 3. 批量查询所有中文字符的拼音
        var pinyinDict = await _database.GetCharsPinyinBatchAsync(uniqueChineseChars, format);

        // 4. 识别多音字并记录位置
        var polyphoneItems = new List<PolyphoneItem>();
        for (var i = 0; i < characters.Length; i++)
        {
            var c = characters[i];
            if (!ChineseCharacterUtils.ContainsChinese(c)) continue;

            if (!pinyinDict.TryGetValue(c, out var pinyins) || pinyins.Length <= 1) continue;
            // 是多音字，提取前后文
            var context = TextUtils.ExtractContext(characters, i, 10);
            polyphoneItems.Add(new PolyphoneItem
            {
                Index = i,
                Character = c,
                Context = context,
                PossiblePinyins = pinyins
            });
        }

        // 5. 批量处理多音字
        if (polyphoneItems.Count > 0)
        {
            await ResolvePolyphoneCharactersAsync(polyphoneItems, format);
        }

        // 6. 构建最终拼音结果
        var result = new StringBuilder(text.Length * 3); // 预估容量
        var isFirstAppend = true;

        for (var i = 0; i < characters.Length; i++)
        {
            var c = characters[i];

            // 处理中文字符
            if (ChineseCharacterUtils.ContainsChinese(c))
            {
                // 查找是否为已处理的多音字
                var polyphoneItem = polyphoneItems.FirstOrDefault(p => p.Index == i);
                string pinyin;

                if (polyphoneItem != null && !string.IsNullOrEmpty(polyphoneItem.ResolvedPinyin))
                {
                    pinyin = polyphoneItem.ResolvedPinyin;
                }
                else if (pinyinDict.TryGetValue(c, out var pinyins) && pinyins.Length > 0)
                {
                    pinyin = pinyins[0];
                }
                else
                {
                    pinyin = "?"; // 未找到拼音
                }

                if (i > 0)
                {
                    var x = characters[i - 1].ToString();
                    if (!isFirstAppend && x != null &&
                        ChineseCharacterUtils.ContainsChinese(x))
                        result.Append(separator);
                }

                result.Append(pinyin);
                isFirstAppend = false;
            }
            // 处理非中文字符
            else if (_options.PreserveNonChinese)
            {
                if (!isFirstAppend && ChineseCharacterUtils.ContainsChinese(characters[i - 1])) result.Append(separator);
                result.Append(c);
                isFirstAppend = false;
            }
        }

        return result.ToString();
    }


    /// <summary>
    /// 解析多音字
    /// </summary>
    private async Task ResolvePolyphoneCharactersAsync(List<PolyphoneItem> items, PinyinFormat format)
    {
        // 创建所有可能的词语组合查询
        var wordQueries = new List<string>();

        foreach (var contexts in items.Select(item => TextUtils.ExtractPossibleWordContexts(item.Context,
                     item.Character, _options.MaxWordLength > 0 ? _options.MaxWordLength : 10)))
        {
            wordQueries.AddRange(contexts);
        }

        // 去重
        wordQueries = [.. wordQueries.Distinct()];

        // 批量查询词语拼音
        var wordPinyinDict = await _database.GetWordsPinyinBatchAsync([.. wordQueries], format, true);

        // 为每个多音字找到最佳匹配
        foreach (var item in items)
        {
            // 提取上下文中的所有可能词语
            var contexts = TextUtils.ExtractPossibleWordContexts(item.Context, item.Character,
                    _options.MaxWordLength > 0 ? _options.MaxWordLength : 10)
                .OrderByDescending(ChineseCharacterUtils.CountChineseCharacters) // 按长度排序，优先使用长词
                .ToList();

            // 查找匹配的词语
            foreach (var context in contexts)
            {
                if (!wordPinyinDict.TryGetValue(context, out var wordPinyin)) continue;
                // 从词语拼音中提取当前字的拼音
                var charIndex = context.IndexOf(item.Character, StringComparison.Ordinal);
                if (charIndex < 0) continue;
                var parts = wordPinyin.Split(' ');
                if (charIndex >= parts.Length) continue;
                item.ResolvedPinyin = parts[charIndex];
                break; // 找到匹配，停止查找
            }

            // 如果没找到匹配的词语拼音，使用第一个可能的拼音
            if (string.IsNullOrEmpty(item.ResolvedPinyin) && item.PossiblePinyins.Length > 0)
            {
                item.ResolvedPinyin = item.PossiblePinyins[0];
            }
        }
    }


    /// <summary>
    /// 处理大型文本（优化版）
    /// </summary>
    public async Task<string> ProcessLargeTextAsync(string text, PinyinFormat format, string separator)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        const int blockSize = 1000;
        if (text.Length <= blockSize)
            return await GetTextPinyinAsync(text, format, separator);

        var result = new StringBuilder(text.Length * 2);
        var segments = SimpleTextSegmenter.Segment(text);

        var currentBlock = new StringBuilder(blockSize + 500); // 添加缓冲区，避免频繁扩容
        var chineseCharCount = 0;

        foreach (var segment in segments)
        {
            var segmentChineseCount = ChineseCharacterUtils.CountChineseCharacters(segment);

            // 如果添加这个片段会超出块大小，先处理当前块
            if (chineseCharCount > 0 && chineseCharCount + segmentChineseCount > blockSize)
            {
                var blockResult = await GetTextPinyinAsync(currentBlock.ToString(), format, separator);

                if (result.Length > 0 && !result.ToString().EndsWith(separator))
                    result.Append(separator);

                result.Append(blockResult);

                // 重置为下一个块
                currentBlock.Clear();
                chineseCharCount = 0;
            }

            currentBlock.Append(segment);
            chineseCharCount += segmentChineseCount;
        }

        // 处理最后一个块，如果它包含任何文本
        if (currentBlock.Length == 0) return result.ToString();
        {
            var blockResult = await GetTextPinyinAsync(currentBlock.ToString(), format, separator);

            if (result.Length > 0 && !result.ToString().EndsWith(separator))
                result.Append(separator);

            result.Append(blockResult);
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
        foreach (var c in text.Where(ChineseCharacterUtils.IsChineseChar))
        {
            uniqueChars.Add(c);
        }

        // 批量查询所有汉字的拼音
        var charPinyins = await _database.GetCharsPinyinBatchAsync([.. uniqueChars], format);

        // 填充结果数组
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (ChineseCharacterUtils.IsChineseChar(c) && charPinyins.TryGetValue(c, out var pinyins) &&
                pinyins.Length > 0)
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
    /// 多音字项
    /// </summary>
    [SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Local")]
    private class PolyphoneItem
    {
        public int Index { get; set; }
        public string Character { get; set; }
        public string Context { get; set; }
        public string[] PossiblePinyins { get; set; }
        public string ResolvedPinyin { get; set; }
    }

    [GeneratedRegex("[a-zA-Z0-9]")]
    private static partial Regex IsEnglishRegex();
}