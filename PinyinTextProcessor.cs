using System;
using System.Collections.Generic;
using System.Linq;
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
            .Where(c => ChineseCharacterUtils.IsChineseCodePoint(c, 0))
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
            var context = ExtractContext(characters, i, 10);
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

                if (!isFirstAppend) result.Append(separator);
                result.Append(pinyin);
                isFirstAppend = false;
            }
            // 处理非中文字符
            else if (_options.PreserveNonChinese)
            {
                if (!isFirstAppend) result.Append(separator);
                result.Append(c);
                isFirstAppend = false;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// 提取字符周围的上下文
    /// </summary>
    private static string ExtractContext(string[] characters, int position, int range)
    {
        var result = new StringBuilder();

        // 向前提取
        var startPos = Math.Max(0, position - range);
        for (var i = position - 1; i >= startPos; i--)
        {
            if (ChineseCharacterUtils.ContainsChinese(characters[i]))
                result.Insert(0, characters[i]);
            else
                break; // 遇到非中文字符停止
        }

        // 添加当前字符
        result.Append(characters[position]);

        // 向后提取
        var endPos = Math.Min(characters.Length - 1, position + range);
        for (var i = position + 1; i <= endPos; i++)
        {
            if (ChineseCharacterUtils.ContainsChinese(characters[i]))
                result.Append(characters[i]);
            else
                break; // 遇到非中文字符停止
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

        foreach (var contexts in items.Select(item => ExtractPossibleWordContexts(item.Context, item.Character)))
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
            var contexts = ExtractPossibleWordContexts(item.Context, item.Character)
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
    /// 从上下文中提取所有可能包含目标字符的词语组合
    /// </summary>
    private List<string> ExtractPossibleWordContexts(string context, string targetChar)
    {
        var results = new List<string>();
        var maxLength = _options.MaxWordLength > 0 ? _options.MaxWordLength : 10;

        var targetPos = context.IndexOf(targetChar, StringComparison.Ordinal);
        if (targetPos < 0) return results;

        // 提取所有可能的包含目标字符的子字符串，长度从2到maxLength
        for (var len = 2; len <= Math.Min(maxLength, context.Length); len++)
        {
            // 以目标字符为中心，向两侧扩展
            for (var start = Math.Max(0, targetPos - len + 1); start <= targetPos; start++)
            {
                if (start + len > context.Length) continue;

                var word = context.Substring(start, len);
                if (word.Contains(targetChar))
                {
                    results.Add(word);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 处理大型文本
    /// </summary>
    public async Task<string> ProcessLargeTextAsync(string text, PinyinFormat format, string separator)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        const int blockSize = 1000;
        if (text.Length <= blockSize)
            return await GetTextPinyinAsync(text, format, separator);

        var result = new StringBuilder(text.Length * 2);

        for (var i = 0; i < text.Length; i += blockSize)
        {
            // 计算基本块长度
            var length = Math.Min(blockSize, text.Length - i);

            // 检查块结束位置是否正好是代理对的高代理项
            if (i + length < text.Length && char.IsHighSurrogate(text[i + length - 1]) &&
                char.IsLowSurrogate(text[i + length]))
            {
                // 如果是，则包含下一个字符，保持代理对完整
                length++;
            }

            var block = text.Substring(i, length);
            var blockResult = await GetTextPinyinAsync(block, format, separator);

            if (i > 0 && result.Length > 0 && !result.ToString().EndsWith(separator))
            {
                result.Append(separator);
            }
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
        foreach (var c in text.Where(IsChinese))
        {
            uniqueChars.Add(c);
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

    /// <summary>
    /// 多音字项
    /// </summary>
    private class PolyphoneItem
    {
        public int Index { get; set; }
        public string Character { get; set; }
        public string Context { get; set; }
        public string[] PossiblePinyins { get; set; }
        public string ResolvedPinyin { get; set; }
    }
}