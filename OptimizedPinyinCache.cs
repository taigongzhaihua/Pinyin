using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TGZH.Pinyin;

/// <summary>
/// 优化的拼音缓存实现，使用LRU（最近最少使用）算法
/// </summary>
/// <typeparam name="TKey">缓存键类型</typeparam>
/// <typeparam name="TValue">缓存值类型</typeparam>
/// <remarks>
/// 创建LRU缓存
/// </remarks>
/// <param name="capacity">缓存容量</param>
internal class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly int _capacity = capacity;
    private readonly ConcurrentDictionary<TKey, LinkedListNode<CacheItem>> _cache = new();
    private readonly LinkedList<CacheItem> _lruList = [];
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>
    /// 尝试获取缓存项
    /// </summary>
    public bool TryGetValue(TKey key, out TValue value)
    {
        value = default;

        if (!_cache.TryGetValue(key, out var node))
            return false;

        _lock.EnterWriteLock();
        try
        {
            // 移到链表头部（最近使用）
            _lruList.Remove(node);
            _lruList.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 添加或更新缓存项
    /// </summary>
    public void AddOrUpdate(TKey key, TValue value)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_cache.TryGetValue(key, out var existingNode))
            {
                // 更新现有节点
                _lruList.Remove(existingNode);
                var cacheItem = new CacheItem { Key = key, Value = value };
                var newNode = new LinkedListNode<CacheItem>(cacheItem);
                _lruList.AddFirst(newNode);
                _cache[key] = newNode;
            }
            else
            {
                // 添加新节点
                var cacheItem = new CacheItem { Key = key, Value = value };
                var node = new LinkedListNode<CacheItem>(cacheItem);

                // 检查容量
                if (_cache.Count >= _capacity)
                {
                    // 删除最久未使用的项
                    var lastNode = _lruList.Last;
                    if (lastNode != null)
                    {
                        _lruList.RemoveLast();
                        _cache.TryRemove(lastNode.Value.Key, out _);
                    }
                }

                _lruList.AddFirst(node);
                _cache[key] = node;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 清空缓存
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _cache.Clear();
            _lruList.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 缓存项
    /// </summary>
    private class CacheItem
    {
        public TKey Key { get; init; }
        public TValue Value { get; init; }
    }
}

/// <summary>
/// 拼音缓存管理类
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal class PinyinCacheManager
{
    // 汉字拼音缓存
    private readonly LruCache<string, Dictionary<PinyinFormat, string[]>> _charCache;

    // 词语拼音缓存
    private readonly LruCache<string, Dictionary<PinyinFormat, string>> _wordCache;

    // 常用字符集
    private readonly HashSet<string> _commonChars = [];

    // 是否启用缓存
    private bool _enableCache = true;

    /// <summary>
    /// 创建拼音缓存管理器
    /// </summary>
    /// <param name="charCacheSize">字符缓存大小</param>
    /// <param name="wordCacheSize">词语缓存大小</param>
    public PinyinCacheManager(int charCacheSize = 10000, int wordCacheSize = 5000)
    {
        _charCache = new LruCache<string, Dictionary<PinyinFormat, string[]>>(charCacheSize);
        _wordCache = new LruCache<string, Dictionary<PinyinFormat, string>>(wordCacheSize);

        // 初始化常用字符集
        InitializeCommonChars();
    }

    /// <summary>
    /// 初始化常用字符集
    /// </summary>
    private void InitializeCommonChars()
    {
        // 添加一些常用汉字
        const string commonChars = "的一是了我不人在他有这个上们来到时大地为子中你说生国年着就那和要她出也得里后自以会家可下而过天去能对小多然于好心东方";
        for (var i = 0; i < commonChars.Length; i++)
        {
            _commonChars.Add(commonChars[i].ToString());
        }
    }

    /// <summary>
    /// 配置缓存
    /// </summary>
    /// <param name="enableCache">是否启用缓存</param>
    public void Configure(bool enableCache)
    {
        _enableCache = enableCache;

        if (enableCache) return;
        _charCache.Clear();
        _wordCache.Clear();
    }

    /// <summary>
    /// 尝试从缓存获取字符拼音
    /// </summary>
    public bool TryGetCharPinyin(char c, PinyinFormat format, out string[] pinyin)
    {
        pinyin = null;

        if (!_enableCache)
            return false;

        return _charCache.TryGetValue(c.ToString(), out var formatDict) &&
               formatDict.TryGetValue(format, out pinyin);
    }

    /// <summary>
    /// 尝试从缓存获取词语拼音
    /// </summary>
    public bool TryGetWordPinyin(string word, PinyinFormat format, out string pinyin)
    {
        pinyin = null;

        if (!_enableCache || string.IsNullOrEmpty(word))
            return false;

        return _wordCache.TryGetValue(word, out var formatDict) &&
               formatDict.TryGetValue(format, out pinyin);
    }

    /// <summary>
    /// 添加字符拼音到缓存
    /// </summary>
    public void AddCharPinyin(string c, PinyinFormat format, string[] pinyin)
    {
        if (!_enableCache)
            return;

        if (!_charCache.TryGetValue(c, out var formatDict))
        {
            formatDict = [];
        }

        formatDict[format] = pinyin;
        _charCache.AddOrUpdate(c, formatDict);
    }

    /// <summary>
    /// 添加词语拼音到缓存
    /// </summary>
    public void AddWordPinyin(string word, PinyinFormat format, string pinyin)
    {
        if (!_enableCache || string.IsNullOrEmpty(word))
            return;

        if (!_wordCache.TryGetValue(word, out var formatDict))
        {
            formatDict = [];
        }

        formatDict[format] = pinyin;
        _wordCache.AddOrUpdate(word, formatDict);
    }

    /// <summary>
    /// 清空缓存
    /// </summary>
    public void ClearCache()
    {
        _charCache.Clear();
        _wordCache.Clear();
    }

    /// <summary>
    /// 判断是否为常用字符
    /// </summary>
    public bool IsCommonChar(char c)
    {
        return _commonChars.Contains(c.ToString());
    }
}