using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TGZH.Pinyin;

/// <summary>
/// 拼音服务 - 提供统一的拼音处理接口
/// </summary>
internal partial class PinyinService : IDisposable
{
    private readonly OptimizedPinyinDatabase _database;
    private readonly PinyinTextProcessor _textProcessor;
    private bool _isInitialized;
    private readonly PinyinServiceOptions _options;

    /// <summary>
    /// 创建拼音服务
    /// </summary>
    public PinyinService(PinyinServiceOptions options = null)
    {
        _options = options ?? new PinyinServiceOptions();
        _database = new OptimizedPinyinDatabase(_options.DatabasePath);
        _textProcessor = new PinyinTextProcessor(_database);
    }

    /// <summary>
    /// 初始化服务
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        await _database.InitializeAsync();
        await _textProcessor.InitializeAsync(_options);

        _isInitialized = true;
    }

    /// <summary>
    /// 检查是否初始化
    /// </summary>
    private void EnsureInitialized()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("拼音服务未初始化，请先调用 InitializeAsync 方法");
    }

    /// <summary>
    /// 获取单个汉字的拼音
    /// </summary>
    public async Task<string[]> GetCharPinyinAsync(char c, PinyinFormat format = PinyinFormat.WithToneMark)
    {
        EnsureInitialized();
        return await _database.GetCharPinyinAsync(c, format);
    }

    public async Task<string[]> GetCharPinyinAsync(string c, PinyinFormat format = PinyinFormat.WithToneMark)
    {
        EnsureInitialized();
        if (c.Length > 1)
        {
            if (ChineseCharacterUtils.IsChineseCodePoint(c, 0) && c.Length == 2)
            {
                return await _database.GetCharPinyinAsync(c, format);
            }
            throw new InvalidOperationException("只能输入一个汉字");
        }
        return await _database.GetCharPinyinAsync(c, format);
    }
    /// <summary>
    /// 获取文本的拼音
    /// </summary>
    public async Task<string> GetTextPinyinAsync(
        string text,
        PinyinFormat format = PinyinFormat.WithToneMark,
        string separator = " ")
    {
        EnsureInitialized();
        return await _textProcessor.GetTextPinyinAsync(text, format, separator);
    }

    /// <summary>
    /// 获取文本的首字母拼音
    /// </summary>
    public async Task<string> GetFirstLettersAsync(string text, string separator = "")
    {
        EnsureInitialized();
        return await _textProcessor.GetTextPinyinAsync(text, PinyinFormat.FirstLetter, separator);
    }

    /// <summary>
    /// 高效处理大文本
    /// </summary>
    public async Task<string> ProcessLargeTextAsync(
        string text,
        PinyinFormat format = PinyinFormat.WithToneMark,
        string separator = " ")
    {
        EnsureInitialized();
        return await _textProcessor.ProcessLargeTextAsync(text, format, separator);
    }

    /// <summary>
    /// 转换格式
    /// </summary>
    public static string ConvertFormat(string pinyin, PinyinFormat sourceFormat, PinyinFormat targetFormat)
    {
        if (sourceFormat == targetFormat)
            return pinyin;

        // 使用增强的拼音转换器处理
        return sourceFormat switch
        {
            PinyinFormat.WithToneMark when targetFormat == PinyinFormat.WithoutTone => EnhancedPinyinConverter
                .RemoveToneMarks(pinyin),
            PinyinFormat.WithToneMark when targetFormat == PinyinFormat.WithToneNumber => EnhancedPinyinConverter
                .ToToneNumber(pinyin),
            PinyinFormat.WithToneMark when targetFormat == PinyinFormat.FirstLetter => EnhancedPinyinConverter
                .GetFirstLetters(pinyin),
            PinyinFormat.WithToneNumber when targetFormat == PinyinFormat.WithToneMark => EnhancedPinyinConverter
                .ToToneMark(pinyin),
            PinyinFormat.WithToneNumber when targetFormat == PinyinFormat.WithoutTone => EnhancedPinyinConverter
                .RemoveToneMarks(EnhancedPinyinConverter.ToToneMark(pinyin)),
            PinyinFormat.WithToneNumber when targetFormat == PinyinFormat.FirstLetter => EnhancedPinyinConverter
                .GetFirstLetters(EnhancedPinyinConverter.ToToneMark(pinyin)),
            PinyinFormat.WithoutTone when targetFormat == PinyinFormat.FirstLetter => EnhancedPinyinConverter
                .GetFirstLetters(pinyin),
            _ => pinyin
        };
    }
    /// <summary>
    /// 批量获取多个字符的拼音
    /// </summary>
    public async Task<Dictionary<char, string[]>> GetCharsPinyinBatchAsync(
        char[] characters, PinyinFormat format)
    {
        EnsureInitialized();

        if (characters == null || characters.Length == 0)
            return [];

        // 调用优化的数据库批量方法
        return await _database.GetCharsPinyinBatchAsync(characters, format);
    }

    public async Task<Dictionary<string, string[]>> GetCharsPinyinBatchAsync(
        string[] characters, PinyinFormat format)
    {
        EnsureInitialized();

        if (characters == null || characters.Length == 0)
            return [];

        // 调用优化的数据库批量方法
        return await _database.GetCharsPinyinBatchAsync(characters, format);
    }

    /// <summary>
    /// 批量获取多个词语的拼音
    /// </summary>
    public async Task<Dictionary<string, string>> GetWordsPinyinBatchAsync(
        string[] words, PinyinFormat format)
    {
        EnsureInitialized();

        if (words == null || words.Length == 0)
            return [];

        // 调用优化的数据库批量方法
        return await _database.GetWordsPinyinBatchAsync(words, format);
    }

    /// <summary>
    /// 获取文本中每个字符的拼音
    /// </summary>
    public async Task<string[]> GetTextCharactersPinyinAsync(string text, PinyinFormat format)
    {
        EnsureInitialized();

        if (string.IsNullOrEmpty(text))
            return [];

        // 调用文本处理器的批量方法
        return await _textProcessor.GetTextCharactersPinyinAsync(text, format);
    }

    /// <summary>
    /// 高效批量处理大文本
    /// </summary>
    public async Task<string> ProcessLargeTextBatchAsync(
        string text, PinyinFormat format, string separator)
    {
        EnsureInitialized();

        // 使用分块处理大文本
        return await _textProcessor.ProcessLargeTextBatchAsync(text, format, separator);
    }
    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _database?.Dispose();
        _isInitialized = false;
    }
}

/// <summary>
/// 拼音服务配置选项
/// </summary>
public class PinyinServiceOptions
{
    /// <summary>
    /// 数据库路径
    /// </summary>
    public string DatabasePath { get; set; }

    /// <summary>
    /// 是否使用词语优先的拼音转换（更准确的多音字处理）
    /// </summary>
    public bool UseWordFirstConversion { get; set; } = true;

    /// <summary>
    /// 最大词语长度
    /// </summary>
    public int MaxWordLength { get; set; } = 8;

    /// <summary>
    /// 是否使用智能多音字处理
    /// </summary>
    public bool UseSmartPolyphoneHandling { get; set; } = true;

    /// <summary>
    /// 是否保留非中文字符
    /// </summary>
    public bool PreserveNonChinese { get; set; } = true;
}