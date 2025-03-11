using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TGZH.Pinyin;

/// <summary>
/// 统一的拼音API - 对外暴露的主要接口
/// </summary>
public static class UnifiedPinyinApi
{
    private static PinyinService _service;
    private static readonly object InitLock = new();
    private static bool _isInitialized;
    private static Task _initializeTask;

    /// <summary>
    /// 初始化拼音库
    /// </summary>
    /// <param name="options">配置选项</param>
    public static async Task InitializeAsync(PinyinLibraryOptions options = null)
    {
        if (_isInitialized)
            return;

        // 使用双重检查锁确保只初始化一次
        if (_initializeTask == null)
        {
            lock (InitLock)
            {
                if (_initializeTask == null)
                {
                    var serviceOptions = new PinyinServiceOptions
                    {
                        DatabasePath = options?.DatabasePath,
                        UseWordFirstConversion = options?.PrioritizeWordPinyin ?? true,
                        MaxWordLength = options?.MaxWordLength ?? 8,
                        UseSmartPolyphoneHandling = true,
                        PreserveNonChinese = true
                    };

                    _service = new PinyinService(serviceOptions);
                    _initializeTask = InitializeInternalAsync();
                }
            }
        }

        await _initializeTask;
    }

    private static async Task InitializeInternalAsync()
    {
        try
        {
            await _service.InitializeAsync();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _initializeTask = null;
            throw new InvalidOperationException("初始化拼音库失败", ex);
        }
    }

    /// <summary>
    /// 检查初始化状态
    /// </summary>
    private static void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("拼音库未初始化，请先调用 InitializeAsync 方法");
        }
    }

    /// <summary>
    /// 获取单个汉字的所有拼音
    /// </summary>
    /// <param name="c">汉字</param>
    /// <param name="format">拼音格式</param>
    /// <returns>拼音数组（多音字可能有多个拼音）</returns>
    public static async Task<string[]> GetCharPinyinAsync(char c, PinyinFormat format = PinyinFormat.WithToneMark)
    {
        await InitializeOnDemandAsync();
        return await _service.GetCharPinyinAsync(c, format);
    }

    public static async Task<string[]> GetCharPinyinAsync(string c, PinyinFormat format = PinyinFormat.WithToneMark)
    {
        await InitializeOnDemandAsync();
        return await _service.GetCharPinyinAsync(c, format);
    }

    /// <summary>
    /// 获取文本的拼音
    /// </summary>
    /// <param name="text">文本内容</param>
    /// <param name="format">拼音格式</param>
    /// <param name="separator">拼音分隔符</param>
    /// <returns>拼音结果</returns>
    public static async Task<string> GetTextPinyinAsync(string text,
        PinyinFormat format = PinyinFormat.WithToneMark, string separator = " ")
    {
        await InitializeOnDemandAsync();
        return await _service.GetTextPinyinAsync(text, format, separator);
    }

    /// <summary>
    /// 获取文本的首字母拼音
    /// </summary>
    /// <param name="text">文本内容</param>
    /// <param name="separator">分隔符</param>
    /// <returns>首字母拼音</returns>
    public static async Task<string> GetFirstLettersAsync(string text, string separator = "")
    {
        await InitializeOnDemandAsync();
        return await _service.GetFirstLettersAsync(text, separator);
    }

    /// <summary>
    /// 高效处理大文本
    /// </summary>
    /// <param name="text">大文本内容</param>
    /// <param name="format">拼音格式</param>
    /// <param name="separator">分隔符</param>
    /// <returns>处理结果</returns>
    public static async Task<string> ProcessLargeTextAsync(string text,
        PinyinFormat format = PinyinFormat.WithToneMark, string separator = " ")
    {
        await InitializeOnDemandAsync();
        return await _service.ProcessLargeTextAsync(text, format, separator);
    }

    /// <summary>
    /// 转换拼音格式
    /// </summary>
    /// <param name="pinyin">拼音输入</param>
    /// <param name="sourceFormat">源格式</param>
    /// <param name="targetFormat">目标格式</param>
    /// <returns>转换后的拼音</returns>
    public static string ConvertFormat(string pinyin, PinyinFormat sourceFormat, PinyinFormat targetFormat)
    {
        if (string.IsNullOrEmpty(pinyin) || sourceFormat == targetFormat)
            return pinyin;

        EnsureInitializedSync();
        return PinyinService.ConvertFormat(pinyin, sourceFormat, targetFormat);
    }

    /// <summary>
    /// 按需初始化（异步版）
    /// </summary>
    private static async Task InitializeOnDemandAsync()
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }
    }

    /// <summary>
    /// 确保初始化（同步版）
    /// </summary>
    private static void EnsureInitializedSync()
    {
        if (!_isInitialized)
        {
            InitializeAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// 批量获取多个字符的拼音
    /// </summary>
    /// <param name="characters">要查询的字符数组</param>
    /// <param name="format">拼音格式</param>
    /// <returns>字符及其拼音的字典</returns>
    public static async Task<Dictionary<char, string[]>> GetCharsPinyinBatchAsync(
        char[] characters, PinyinFormat format = PinyinFormat.WithToneMark)
    {
        await InitializeOnDemandAsync();
        return await _service.GetCharsPinyinBatchAsync(characters, format);
    }

    public static async Task<Dictionary<string, string[]>> GetCharsPinyinBatchAsync(
        string[] characters, PinyinFormat format = PinyinFormat.WithToneMark)
    {
        await InitializeOnDemandAsync();
        return await _service.GetCharsPinyinBatchAsync(characters, format);
    }

    /// <summary>
    /// 批量获取多个词语的拼音
    /// </summary>
    /// <param name="words">要查询的词语数组</param>
    /// <param name="format">拼音格式</param>
    /// <returns>词语及其拼音的字典</returns>
    public static async Task<Dictionary<string, string>> GetWordsPinyinBatchAsync(
        string[] words, PinyinFormat format = PinyinFormat.WithToneMark)
    {
        await InitializeOnDemandAsync();
        return await _service.GetWordsPinyinBatchAsync(words, format);
    }

    /// <summary>
    /// 批量处理文本中的所有字符
    /// </summary>
    /// <param name="text">要处理的文本</param>
    /// <param name="format">拼音格式</param>
    /// <returns>每个位置的拼音数组</returns>
    public static async Task<string[]> GetTextCharactersPinyinAsync(
        string text, PinyinFormat format = PinyinFormat.WithToneMark)
    {
        await InitializeOnDemandAsync();
        return await _service.GetTextCharactersPinyinAsync(text, format);
    }

    /// <summary>
    /// 高效批量处理大文本
    /// </summary>
    /// <param name="text">大文本内容</param>
    /// <param name="format">拼音格式</param>
    /// <param name="separator">分隔符</param>
    /// <returns>拼音结果</returns>
    public static async Task<string> ProcessLargeTextBatchAsync(
        string text, PinyinFormat format = PinyinFormat.WithToneMark, string separator = " ")
    {
        await InitializeOnDemandAsync();
        return await _service.ProcessLargeTextBatchAsync(text, format, separator);
    }

    /// <summary>
    /// 判断字符是否是中文
    /// </summary>
    public static bool IsChinese(char c)
    {
        // 对于单个字符，只能判断基本汉字和扩展A区
        return (c >= 0x4E00 && c <= 0x9FFF) || // 基本汉字
               (c >= 0x3400 && c <= 0x4DBF); // 扩展A区
    }

    /// <summary>
    /// 判断指定位置的字符是否是中文（支持扩展B区及更高区域）
    /// </summary>
    public static bool IsChineseAt(string text, int index)
    {
        return ChineseCharacterUtils.IsChineseCodePoint(text, index);
    }
}

/// <summary>
/// 拼音结果类
/// </summary>
public class PinyinResult
{
    /// <summary>
    /// 原始文本
    /// </summary>
    public string Original { get; set; }

    /// <summary>
    /// 带声调拼音
    /// </summary>
    public string WithToneMark { get; set; }

    /// <summary>
    /// 不带声调拼音
    /// </summary>
    public string WithoutTone { get; set; }

    /// <summary>
    /// 数字声调拼音
    /// </summary>
    public string WithToneNumber { get; set; }

    /// <summary>
    /// 拼音首字母
    /// </summary>
    public string FirstLetter { get; set; }
}