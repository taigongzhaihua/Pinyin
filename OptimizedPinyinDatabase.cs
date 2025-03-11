using Microsoft.Data.Sqlite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace TGZH.Pinyin;

/// <summary>
/// 优化的拼音数据库访问类
/// </summary>
internal partial class OptimizedPinyinDatabase : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection _connection;
    private bool _isInitialized;
    private readonly PinyinCacheManager _cacheManager;

    // 批量查询缓冲区和合并窗口
    private readonly ConcurrentQueue<QueuedQuery> _queryQueue = new();
    private readonly System.Timers.Timer _batchTimer;

    // 初始化锁
    private readonly object _initLock = new();

    /// <summary>
    /// 创建拼音数据库实例
    /// </summary>
    /// <param name="dbPath">数据库文件路径，如果为空则使用默认路径</param>
    public OptimizedPinyinDatabase(string dbPath = null)
    {
        _dbPath = dbPath ?? GetDefaultDbPath();
        _cacheManager = new PinyinCacheManager();

        // 创建批处理定时器，每50毫秒执行一次批量查询
        _batchTimer = new System.Timers.Timer(50);
        _batchTimer.Elapsed += async (_, _) => await ProcessQueryQueueAsync();
        _batchTimer.AutoReset = true;
    }

    /// <summary>
    /// 获取默认数据库路径
    /// </summary>
    private static string GetDefaultDbPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pinyinDir = Path.Combine(appDataPath, "TGZH.Pinyin");

        // 确保目录存在
        if (!Directory.Exists(pinyinDir))
        {
            Directory.CreateDirectory(pinyinDir);
        }

        return Path.Combine(pinyinDir, "PinyinData.db");
    }

    /// <summary>
    /// 初始化数据库
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        lock (_initLock)
        {
            if (_isInitialized)
                return;

            try
            {
                // 确保数据库文件存在
                EnsureDatabaseFileExists();

                // 打开数据库连接
                var connectionString = $"Data Source={_dbPath}";
                _connection = new SqliteConnection(connectionString);
                _connection.Open();

                // 启动批处理定时器
                _batchTimer.Start();

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化拼音数据库失败: {ex.Message}");
                throw;
            }
        }

        // 在单独的线程完成数据库结构和初始数据的设置
        await Task.Run(async () =>
        {
            // 确保数据库结构正确
            await EnsureSchemaCreatedAsync();

            // 检查并导入基础数据
            if (await IsDatabaseEmptyAsync())
            {
                await ImportBasicDataAsync();
            }

            // 预热缓存
            await WarmupCacheAsync();
        });
    }

    /// <summary>
    /// 确保数据库文件存在
    /// </summary>
    private void EnsureDatabaseFileExists()
    {
        // 如果数据库文件不存在，创建一个空文件
        if (File.Exists(_dbPath)) return;
        // 创建目录（如果不存在）
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 创建空文件
        using (File.Create(_dbPath)) { }
    }

    /// <summary>
    /// 确保数据库架构已创建
    /// </summary>
    private async Task EnsureSchemaCreatedAsync()
    {
        if (_connection == null)
            throw new InvalidOperationException("数据库连接未初始化");

        await using var transaction = _connection.BeginTransaction();
        try
        {
            // 创建字符表
            await using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = """
                                  
                                                          CREATE TABLE IF NOT EXISTS Characters (
                                                              Character TEXT PRIMARY KEY,
                                                              CodePoint INTEGER NOT NULL,
                                                              WithToneMark TEXT NOT NULL,
                                                              WithoutTone TEXT NOT NULL,
                                                              WithToneNumber TEXT NOT NULL,
                                                              FirstLetter TEXT NOT NULL,
                                                              Frequency INTEGER DEFAULT 0
                                                          );
                                                          CREATE INDEX IF NOT EXISTS idx_character_frequency ON Characters(Frequency DESC);
                                                          CREATE INDEX IF NOT EXISTS idx_character_codepoint ON Characters(CodePoint);
                                                      
                                  """;
                await cmd.ExecuteNonQueryAsync();
            }

            // 创建词语表
            await using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = """
                                  
                                                          CREATE TABLE IF NOT EXISTS Words (
                                                              Word TEXT PRIMARY KEY,
                                                              WithToneMark TEXT NOT NULL,
                                                              WithoutTone TEXT NOT NULL,
                                                              WithToneNumber TEXT NOT NULL,
                                                              FirstLetter TEXT NOT NULL,
                                                              Frequency INTEGER DEFAULT 0
                                                          );
                                                          CREATE INDEX IF NOT EXISTS idx_word_frequency ON Words(Frequency DESC);
                                                          CREATE INDEX IF NOT EXISTS idx_word_length ON Words(LENGTH(Word) DESC);
                                                      
                                  """;
                await cmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Debug.WriteLine($"创建数据库结构失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 检查数据库是否为空
    /// </summary>
    private async Task<bool> IsDatabaseEmptyAsync()
    {
        if (_connection == null)
            throw new InvalidOperationException("数据库连接未初始化");

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Characters";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) == 0;
    }

    /// <summary>
    /// 导入基础拼音数据
    /// </summary>
    private async Task ImportBasicDataAsync()
    {
        try
        {
            // 导入内置的基础数据
            var assembly = Assembly.GetExecutingAssembly();

            // 导入汉字数据
            const string characterResourceName = "TGZH.Pinyin.Resources.BasicCharacterPinyin.txt";
            await using (var stream = assembly.GetManifestResourceStream(characterResourceName))
            {
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var content = await reader.ReadToEndAsync();
                    await ImportCharacterDataAsync(content);
                }
            }

            // 导入词语数据
            const string wordResourceName = "TGZH.Pinyin.Resources.BasicWordPinyin.txt";
            await using (var stream = assembly.GetManifestResourceStream(wordResourceName))
            {
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    var content = await reader.ReadToEndAsync();
                    await ImportWordDataAsync(content);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"导入基础拼音数据失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 导入汉字数据
    /// </summary>
    private async Task ImportCharacterDataAsync(string content)
    {
        if (_connection == null)
            throw new InvalidOperationException("数据库连接未初始化");

        await using var transaction = _connection.BeginTransaction();
        try
        {
            using (var reader = new StringReader(content))
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    // 忽略注释和空行
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith(value: '#'))
                        continue;

                    // 解析行数据
                    if (!ParseCharacterLine(line, out var character, out var withTone,
                            out var withoutTone, out var withNumber, out var firstLetter)) continue;
                    // 计算字符的Unicode码点
                    int codePoint;
                    switch (character.Length)
                    {
                        case 1:
                            codePoint = character[0];
                            break;
                        case 2 when char.IsHighSurrogate(character[0]) &&
                                    char.IsLowSurrogate(character[1]):
                            codePoint = char.ConvertToUtf32(character[0], character[1]);
                            break;
                        default:
                            Debug.WriteLine($"无法处理的字符: '{character}'");
                            continue;
                    }

                    // 插入到数据库
                    await using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = """
                                      
                                                                      INSERT OR REPLACE INTO Characters 
                                                                      (Character, CodePoint, WithToneMark, WithoutTone, WithToneNumber, FirstLetter, Frequency)
                                                                      VALUES ($char, $codePoint, $withTone, $withoutTone, $withNumber, $firstLetter, 500)
                                                                  
                                      """;

                    cmd.Parameters.AddWithValue("$char", character);
                    cmd.Parameters.AddWithValue("$codePoint", codePoint);
                    cmd.Parameters.AddWithValue("$withTone", withTone);
                    cmd.Parameters.AddWithValue("$withoutTone", withoutTone);
                    cmd.Parameters.AddWithValue("$withNumber", withNumber);
                    cmd.Parameters.AddWithValue("$firstLetter", firstLetter);

                    await cmd.ExecuteNonQueryAsync();
                }
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Debug.WriteLine($"导入汉字数据失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 解析汉字数据行
    /// </summary>
    private static bool ParseCharacterLine(string line, out string character, out string withTone,
        out string withoutTone, out string withNumber, out string firstLetter)
    {
        character = null;
        withTone = null;
        withoutTone = null;
        withNumber = null;
        firstLetter = null;

        try
        {
            // 移除注释部分
            var commentIndex = line.IndexOf('#');
            if (commentIndex > 0)
            {
                line = line[..commentIndex].Trim();
            }

            // 解析格式: 字符=拼音 或 字符:拼音
            string[] parts;
            if (line.Contains('='))
            {
                parts = line.Split('=', 2);
            }
            else if (line.Contains(':'))
            {
                parts = line.Split(':', 2);
            }
            else
            {
                return false;
            }

            if (parts.Length != 2)
                return false;

            var charPart = parts[0].Trim();
            var pinyinPart = parts[1].Trim();

            // 处理字符部分
            if (charPart.StartsWith("U+", StringComparison.OrdinalIgnoreCase))
            {
                // Unicode编码格式
                var unicodeStr = charPart[2..];
                if (!int.TryParse(unicodeStr, System.Globalization.NumberStyles.HexNumber,
                        null, out var unicode))
                {
                    return false;
                }

                if (unicode > 0x10FFFF)
                {
                    return false;
                }

                try
                {
                    character = char.ConvertFromUtf32(unicode);
                }
                catch
                {
                    return false;
                }
            }
            else if (charPart.Length >= 1)
            {
                // 直接字符
                character = charPart[0].ToString();
            }
            else
            {
                return false;
            }

            // 处理拼音部分
            var pinyins = pinyinPart.Split(',');
            if (pinyins.Length == 0)
                return false;

            // 使用所有拼音，逗号分隔
            withTone = string.Join(",", pinyins.Select(p => p.Trim()));

            // 转换其他格式
            withoutTone = string.Join(",", pinyins.Select(p => EnhancedPinyinConverter.RemoveToneMarks(p.Trim())));
            withNumber = string.Join(",", pinyins.Select(p => EnhancedPinyinConverter.ToToneNumber(p.Trim())));

            // 首字母使用第一个拼音的首字母
            firstLetter = EnhancedPinyinConverter.RemoveToneMarks(pinyins[0].Trim())[0].ToString();

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"解析拼音行失败: {line}, 错误: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 导入词语数据
    /// </summary>
    private async Task ImportWordDataAsync(string content)
    {
        if (_connection == null)
            throw new InvalidOperationException("数据库连接未初始化");

        await using var transaction = _connection.BeginTransaction();
        try
        {
            using (var reader = new StringReader(content))
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    // 忽略注释和空行
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                        continue;

                    // 解析行数据
                    if (!ParseWordLine(line, out var word, out var withTone,
                            out var withoutTone, out var withNumber, out var firstLetter)) continue;
                    // 插入到数据库
                    await using var cmd = _connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = """
                                      
                                                                      INSERT OR REPLACE INTO Words
                                                                      (Word, WithToneMark, WithoutTone, WithToneNumber, FirstLetter, Frequency)
                                                                      VALUES ($word, $withTone, $withoutTone, $withNumber, $firstLetter, 500)
                                                                  
                                      """;

                    cmd.Parameters.AddWithValue("$word", word);
                    cmd.Parameters.AddWithValue("$withTone", withTone);
                    cmd.Parameters.AddWithValue("$withoutTone", withoutTone);
                    cmd.Parameters.AddWithValue("$withNumber", withNumber);
                    cmd.Parameters.AddWithValue("$firstLetter", firstLetter);

                    await cmd.ExecuteNonQueryAsync();
                }
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            Debug.WriteLine($"导入词语数据失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 解析词语数据行
    /// </summary>
    private static bool ParseWordLine(string line, out string word, out string withTone,
        out string withoutTone, out string withNumber, out string firstLetter)
    {
        word = null;
        withTone = null;
        withoutTone = null;
        withNumber = null;
        firstLetter = null;

        try
        {
            // 移除注释
            var commentIndex = line.IndexOf('#');
            if (commentIndex > 0)
            {
                line = line[..commentIndex].Trim();
            }

            // 解析格式: 词语=拼音 或 词语:拼音
            string[] parts;
            if (line.Contains('='))
            {
                parts = line.Split('=', 2);
            }
            else if (line.Contains(':'))
            {
                parts = line.Split(':', 2);
            }
            else
            {
                return false;
            }

            if (parts.Length != 2)
                return false;

            word = parts[0].Trim();
            var pinyinPart = parts[1].Trim();

            // 词语长度必须大于1
            if (string.IsNullOrEmpty(word) || word.Length <= 1)
                return false;

            withTone = pinyinPart;
            withoutTone = EnhancedPinyinConverter.RemoveToneMarks(withTone);
            withNumber = EnhancedPinyinConverter.ToToneNumber(withTone);

            // 计算首字母
            var sb = new StringBuilder();
            foreach (var part in withoutTone.Split(' '))
            {
                if (!string.IsNullOrEmpty(part))
                    sb.Append(part[0]);
            }
            firstLetter = sb.ToString();

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取汉字拼音
    /// </summary>
    public async Task<string[]> GetCharPinyinAsync(char c, PinyinFormat format)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("数据库未初始化");

        // 尝试从缓存获取
        if (_cacheManager.TryGetCharPinyin(c, format, out var cachedPinyin))
        {
            return cachedPinyin;
        }

        // 创建查询任务
        var taskCompletionSource = new TaskCompletionSource<string[]>();
        _queryQueue.Enqueue(new QueuedQuery
        {
            Character = c,
            Format = format,
            Result = taskCompletionSource
        });

        // 返回异步任务
        return await taskCompletionSource.Task;
    }

    /// <summary>
    /// 处理查询队列
    /// </summary>
    private async Task ProcessQueryQueueAsync()
    {
        if (_connection == null || _queryQueue.IsEmpty)
            return;

        // 收集当前队列中的所有查询
        var queries = new List<QueuedQuery>();
        while (_queryQueue.TryDequeue(out var query))
        {
            queries.Add(query);
        }

        if (queries.Count == 0)
            return;

        try
        {
            // 按格式分组批量查询
            var queryGroups = queries.GroupBy(q => q.Format);

            foreach (var group in queryGroups)
            {
                var format = group.Key;
                var charsToQuery = group.Select(q => q.Character).Distinct().ToArray();

                // 批量查询数据库
                var results = await BatchQueryCharactersAsync(charsToQuery, format);

                // 设置查询结果
                foreach (var query in group)
                {
                    if (results.TryGetValue(query.Character, out var pinyin))
                    {
                        query.Result.SetResult(pinyin);

                        // 添加到缓存
                        _cacheManager.AddCharPinyin(query.Character, format, pinyin);
                    }
                    else
                    {
                        // 未找到结果，使用默认值
                        var defaultResult = new[] { query.Character.ToString() };
                        query.Result.SetResult(defaultResult);

                        // 缓存默认结果
                        _cacheManager.AddCharPinyin(query.Character, format, defaultResult);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 处理异常
            foreach (var query in queries)
            {
                query.Result.TrySetException(ex);
            }
            Debug.WriteLine($"批量查询拼音失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 批量查询汉字拼音
    /// </summary>
    private async Task<Dictionary<char, string[]>> BatchQueryCharactersAsync(
        char[] chars, PinyinFormat format)
    {
        if (_connection == null || chars.Length == 0)
            return [];

        var result = new Dictionary<char, string[]>();
        var columnName = GetColumnName(format);

        // 分批处理，避免参数过多
        const int batchSize = 500;
        for (var i = 0; i < chars.Length; i += batchSize)
        {
            var batch = chars.Skip(i).Take(batchSize).ToArray();

            // 构建查询
            var parameters = new List<SqliteParameter>();
            var placeholders = new StringBuilder();

            for (var j = 0; j < batch.Length; j++)
            {
                var paramName = $"@p{j}";
                if (j > 0)
                    placeholders.Append(',');
                placeholders.Append(paramName);
                parameters.Add(new SqliteParameter(paramName, batch[j].ToString()));
            }

            // 执行查询
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"""
                               
                                                   SELECT Character, {columnName}
                                                   FROM Characters
                                                   WHERE Character IN ({placeholders})
                                               
                               """;

            foreach (var param in parameters)
            {
                cmd.Parameters.Add(param);
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var charStr = reader.GetString(0);
                var pinyin = reader.GetString(1).Split(',');

                // 只处理单个字符
                if (charStr.Length != 1)
                    continue;

                result[charStr[0]] = pinyin;
            }
        }

        return result;
    }

    /// <summary>
    /// 根据格式获取列名
    /// </summary>
    private static string GetColumnName(PinyinFormat format)
    {
        return format switch
        {
            PinyinFormat.WithoutTone => "WithoutTone",
            PinyinFormat.WithToneNumber => "WithToneNumber",
            PinyinFormat.FirstLetter => "FirstLetter",
            _ => "WithToneMark"
        };
    }

    /// <summary>
    /// 获取词语拼音
    /// </summary>
    public async Task<string> GetWordPinyinAsync(string word, PinyinFormat format)
    {
        if (string.IsNullOrEmpty(word) || word.Length <= 1)
            return null;

        if (!_isInitialized)
            throw new InvalidOperationException("数据库未初始化");

        // 检查缓存
        if (_cacheManager.TryGetWordPinyin(word, format, out var cachedPinyin))
        {
            return cachedPinyin;
        }

        try
        {
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT {GetColumnName(format)} FROM Words WHERE Word = @word";
            cmd.Parameters.AddWithValue("@word", word);

            var result = await cmd.ExecuteScalarAsync();
            if (result == null || result == DBNull.Value) return null;
            var pinyin = result.ToString();

            // 添加到缓存
            _cacheManager.AddWordPinyin(word, format, pinyin);

            return pinyin;

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"查询词语拼音失败: {ex.Message}");
            return null;
        }
    }
    /// <summary>
    /// 批量获取多个汉字的拼音
    /// </summary>
    /// <param name="chars">要查询的汉字数组</param>
    /// <param name="format">拼音格式</param>
    /// <returns>拼音结果字典，键为汉字，值为拼音数组</returns>
    public async Task<Dictionary<char, string[]>> GetCharsPinyinBatchAsync(
        char[] chars, PinyinFormat format)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("数据库未初始化");

        if (chars == null || chars.Length == 0)
            return [];

        var result = new Dictionary<char, string[]>();
        var uniqueChars = chars.Distinct().ToArray();

        // 从缓存获取
        var charsToQuery = new List<char>();
        foreach (var c in uniqueChars)
        {
            if (_cacheManager.TryGetCharPinyin(c, format, out var cachedPinyin))
            {
                result[c] = cachedPinyin;
            }
            else
            {
                charsToQuery.Add(c);
            }
        }

        // 如果所有字符都在缓存中找到，直接返回
        if (charsToQuery.Count == 0)
            return result;

        // 分批处理查询
        const int batchSize = 500;
        for (var i = 0; i < charsToQuery.Count; i += batchSize)
        {
            var batch = charsToQuery.Skip(i).Take(batchSize).ToArray();

            // 构建批量查询SQL
            var columnName = GetColumnName(format);
            var sb = new StringBuilder();
            var parameters = new List<SqliteParameter>();

            sb.Append($"SELECT Character, {columnName} FROM Characters WHERE Character IN (");

            for (var j = 0; j < batch.Length; j++)
            {
                var paramName = $"@p{j}";
                if (j > 0) sb.Append(',');
                sb.Append(paramName);
                parameters.Add(new SqliteParameter(paramName, batch[j].ToString()));
            }

            sb.Append(")");

            // 执行批量查询
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = sb.ToString();

            foreach (var param in parameters)
            {
                cmd.Parameters.Add(param);
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var charStr = reader.GetString(0);
                if (charStr.Length != 1) continue;

                var c = charStr[0];
                var pinyin = reader.GetString(1).Split(',');

                result[c] = pinyin;

                // 添加到缓存
                _cacheManager.AddCharPinyin(c, format, pinyin);
            }

            // 处理未找到的字符
            foreach (var c in batch.Where(c => !result.ContainsKey(c)))
            {
                var defaultPinyin = new[] { c.ToString() };
                result[c] = defaultPinyin;

                // 缓存默认结果
                _cacheManager.AddCharPinyin(c, format, defaultPinyin);
            }
        }

        return result;
    }

    /// <summary>
    /// 批量获取多个词语的拼音
    /// </summary>
    /// <param name="words">要查询的词语数组</param>
    /// <param name="format">拼音格式</param>
    /// <returns>拼音结果字典，键为词语，值为拼音</returns>
    public async Task<Dictionary<string, string>> GetWordsPinyinBatchAsync(
        string[] words, PinyinFormat format)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("数据库未初始化");

        if (words == null || words.Length == 0)
            return [];

        var result = new Dictionary<string, string>();

        // 过滤有效词语（长度>1）
        var validWords = words.Where(w => !string.IsNullOrEmpty(w) && w.Length > 1).Distinct().ToArray();

        // 从缓存获取
        var wordsToQuery = new List<string>();
        foreach (var word in validWords)
        {
            if (_cacheManager.TryGetWordPinyin(word, format, out var cachedPinyin))
            {
                result[word] = cachedPinyin;
            }
            else
            {
                wordsToQuery.Add(word);
            }
        }

        // 如果所有词语都在缓存中找到，直接返回
        if (wordsToQuery.Count == 0)
            return result;

        // 分批处理查询
        const int batchSize = 200;
        for (var i = 0; i < wordsToQuery.Count; i += batchSize)
        {
            var batch = wordsToQuery.Skip(i).Take(batchSize).ToArray();

            // 构建批量查询SQL
            var columnName = GetColumnName(format);
            var sb = new StringBuilder();
            var parameters = new List<SqliteParameter>();

            sb.Append($"SELECT Word, {columnName} FROM Words WHERE Word IN (");

            for (var j = 0; j < batch.Length; j++)
            {
                var paramName = $"@p{j}";
                if (j > 0) sb.Append(',');
                sb.Append(paramName);
                parameters.Add(new SqliteParameter(paramName, batch[j]));
            }

            sb.Append(')');

            // 执行批量查询
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = sb.ToString();

            foreach (var param in parameters)
            {
                cmd.Parameters.Add(param);
            }

            // 记录查到的词语
            var foundWords = new HashSet<string>();

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var word = reader.GetString(0);
                var pinyin = reader.GetString(1);

                result[word] = pinyin;
                foundWords.Add(word);

                // 添加到缓存
                _cacheManager.AddWordPinyin(word, format, pinyin);
            }

            // 找出未查到的词语
            var missingWords = batch.Except(foundWords).ToList();

            // 如果有未查到的词语，从单字表中查询
            if (missingWords.Count <= 0) continue;
            {
                // 收集所有需要查询的单字
                var allChars = new HashSet<char>();
                foreach (var c in missingWords.SelectMany(word => word))
                {
                    allChars.Add(c);
                }

                // 批量查询单字拼音
                var charDict = await GetCharsPinyinBatchAsync(allChars.ToArray(), format);

                // 为每个未查到的词语组合单字拼音
                foreach (var word in missingWords)
                {
                    var combinedPinyin = new StringBuilder();
                    var allCharsFound = true;

                    foreach (var c in word)
                    {
                        if (charDict.TryGetValue(c, out var charPinyin))
                        {
                            if (combinedPinyin.Length > 0)
                                combinedPinyin.Append(' '); // 或其他分隔符，根据format决定
                            combinedPinyin.Append(charPinyin[0]);

                        }
                        else
                        {
                            allCharsFound = false;
                            break;
                        }
                    }

                    if (!allCharsFound) continue;
                    var wordPinyin = combinedPinyin.ToString();
                    result[word] = wordPinyin;

                    // 添加到缓存
                    _cacheManager.AddWordPinyin(word, format, wordPinyin);
                }
            }
        }

        return result;
    }


    /// <summary>
    /// 预热缓存
    /// </summary>
    private async Task WarmupCacheAsync(int count = 2000)
    {
        if (_connection == null)
            return;

        try
        {
            // 加载常用汉字
            await using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"""
                               
                                                   SELECT Character, WithToneMark, WithoutTone, WithToneNumber, FirstLetter
                                                   FROM Characters
                                                   ORDER BY Frequency DESC
                                                   LIMIT {count}
                                               
                               """;

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var charStr = reader.GetString(0);
                if (charStr.Length != 1)
                    continue;

                var c = charStr[0];

                // 加载各种格式到缓存
                _cacheManager.AddCharPinyin(c, PinyinFormat.WithToneMark, reader.GetString(1).Split(','));
                _cacheManager.AddCharPinyin(c, PinyinFormat.WithoutTone, reader.GetString(2).Split(','));
                _cacheManager.AddCharPinyin(c, PinyinFormat.WithToneNumber, reader.GetString(3).Split(','));
                _cacheManager.AddCharPinyin(c, PinyinFormat.FirstLetter, reader.GetString(4).Split(','));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"预热缓存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _batchTimer?.Stop();
        _batchTimer?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
        _isInitialized = false;
    }

    /// <summary>
    /// 队列化的查询项
    /// </summary>
    private class QueuedQuery
    {
        public char Character { get; init; }
        public PinyinFormat Format { get; init; }
        public TaskCompletionSource<string[]> Result { get; init; }
    }
}