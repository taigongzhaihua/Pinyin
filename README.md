# TGZH.Pinyin 使用文档

## 目录

- [简介](#简介)
- [安装](#安装)
- [使用说明](#使用说明)
- [基本用法](#基本用法)
- [详细API参考](#详细api参考)
- [高级功能](#高级功能)
- [示例代码](#示例代码)

## 简介

TGZH.Pinyin 是一个高性能的中文拼音转换库，提供了丰富的拼音处理功能：

- 支持多种拼音格式（带声调、不带声调、数字声调、首字母）
- 智能处理多音字
- 支持单字、词语和大文本的转换
- 包含拼音比较、匹配等实用工具
- 支持所有Unicode汉字（包括所有扩展区）
- 高效的批处理和缓存机制

## 安装

### 通过 NuGet 安装

```
Install-Package TGZH.Pinyin
```

或使用 .NET CLI:

```
dotnet add package TGZH.Pinyin
```

也可以通过 Visual Studio 的 NuGet 包管理器搜索 `TGZH.Pinyin` 进行安装。

## 使用说明

### 项目引用

1. NuGet 安装后可直接使用
2. 或在你的解决方案中添加TGZH.Pinyin项目引用

## 基本用法

### 初始化拼音库

在使用任何拼音功能前，需要先初始化拼音库：

```csharp
using TGZH.Pinyin;

// 默认初始化
await UnifiedPinyinApi.InitializeAsync();

// 或者使用自定义选项初始化
var options = new PinyinLibraryOptions
{
    PreloadCommonChars = true,
    PrioritizeWordPinyin = true,
    MaxWordLength = 8
};
await UnifiedPinyinApi.InitializeAsync(options);
```

### 获取汉字拼音

```csharp
// 获取单个汉字的拼音（支持多音字，返回所有可能的拼音）
string[] pinyins = await UnifiedPinyinApi.GetCharPinyinAsync('中');
Console.WriteLine(string.Join(", ", pinyins));  // 输出: zhōng

// 获取扩展区汉字的拼音（使用字符串重载）
string[] extPinyins = await UnifiedPinyinApi.GetCharPinyinAsync("𠀀");

// 不同的拼音格式
string[] withoutTone = await UnifiedPinyinApi.GetCharPinyinAsync('中', PinyinFormat.WithoutTone);
string[] withToneNumber = await UnifiedPinyinApi.GetCharPinyinAsync('中', PinyinFormat.WithToneNumber);
string[] firstLetter = await UnifiedPinyinApi.GetCharPinyinAsync('中', PinyinFormat.FirstLetter);
```

### 获取文本拼音

```csharp
// 获取文本的拼音
string pinyin = await UnifiedPinyinApi.GetTextPinyinAsync("中国");
Console.WriteLine(pinyin);  // 输出: zhōng guó

// 自定义分隔符
string pinyinNoSpace = await UnifiedPinyinApi.GetTextPinyinAsync("中国", PinyinFormat.WithToneMark, "");
Console.WriteLine(pinyinNoSpace);  // 输出: zhōngguó

// 不带声调
string pinyinNoTone = await UnifiedPinyinApi.GetTextPinyinAsync("中国", PinyinFormat.WithoutTone);
Console.WriteLine(pinyinNoTone);  // 输出: zhong guo

// 首字母
string firstLetters = await UnifiedPinyinApi.GetFirstLettersAsync("中国");
Console.WriteLine(firstLetters);  // 输出: zg
```

## 详细API参考

### 枚举

#### PinyinFormat

```csharp
public enum PinyinFormat
{
    // 带声调，如: zhōng guó
    WithToneMark,

    // 不带声调，如: zhong guo
    WithoutTone,

    // 带数字声调，如: zhong1 guo2
    WithToneNumber,

    // 仅首字母，如: z g
    FirstLetter
}
```

### 类和接口

#### UnifiedPinyinApi 类

主要的静态API类，提供所有拼音相关功能。

| 方法 | 描述 | 参数 | 返回值 |
|------|------|------|--------|
| `InitializeAsync` | 初始化拼音库 | `PinyinLibraryOptions options = null` | `Task` |
| `GetCharPinyinAsync` | 获取单个汉字的所有拼音 | `char c, PinyinFormat format = PinyinFormat.WithToneMark` | `Task<string[]>` |
| `GetCharPinyinAsync` | 获取汉字的所有拼音(包括扩展区字符) | `string c, PinyinFormat format = PinyinFormat.WithToneMark` | `Task<string[]>` |
| `GetTextPinyinAsync` | 获取文本的拼音 | `string text, PinyinFormat format = PinyinFormat.WithToneMark, string separator = " "` | `Task<string>` |
| `GetFirstLettersAsync` | 获取文本的首字母拼音 | `string text, string separator = ""` | `Task<string>` |
| `ProcessLargeTextAsync` | 高效处理大文本 | `string text, PinyinFormat format = PinyinFormat.WithToneMark, string separator = " "` | `Task<string>` |
| `GetCharsPinyinBatchAsync` | 批量获取多个字符的拼音 | `char[] characters, PinyinFormat format = PinyinFormat.WithToneMark` | `Task<Dictionary<char, string[]>>` |
| `GetCharsPinyinBatchAsync` | 批量获取多个字符的拼音(`string`类型重载，可支持扩展B-G区查询) | `string[] characters, PinyinFormat format = PinyinFormat.WithToneMark` | `Task<Dictionary<string, string[]>>` |
| `GetWordsPinyinBatchAsync` | 批量获取多个词语的拼音 | `string[] words, PinyinFormat format = PinyinFormat.WithToneMark` | `Task<Dictionary<string, string>>` |
| `GetTextCharactersPinyinAsync` | 批量处理文本中的所有字符 | `string text, PinyinFormat format = PinyinFormat.WithToneMark` | `Task<string[]>` |
| `ProcessLargeTextBatchAsync` | 高效批量处理大文本 | `string text, PinyinFormat format = PinyinFormat.WithToneMark, string separator = " "` | `Task<string>` |
| `ConvertFormat` | 转换拼音格式 | `string pinyin, PinyinFormat sourceFormat, PinyinFormat targetFormat` | `string` |
| `IsChinese` | 判断字符是否是中文 | `char c` | `bool` |
| `IsChineseAt` | 判断指定位置是否是中文 | `string text, int index` | `bool` |

#### PinyinUtils 类

提供拼音相关的实用工具方法。

| 方法 | 描述 | 参数 | 返回值 |
|------|------|------|--------|
| `CompareSimilarity` | 比较两个拼音字符串的相似度 | `string pinyin1, string pinyin2` | `double` (0-1) |
| `NormalizeToStandardPinyin` | 将非标准拼音修正为标准拼音 | `string pinyin` | `string` |
| `IsValidPinyin` | 判断是否符合拼音规则 | `string pinyin` | `bool` |
| `GetInitialsAsync` | 获取所有汉字的拼音首字母 | `string text` | `Task<string>` |
| `MatchesPinyinPattern` | 将带有拼音首字母的文本进行模糊匹配 | `string text, string pattern` | `Task<bool>` |
| `ContainsPinyinAsync` | 检查一个文本是否包含另一个文本的拼音 | `string text, string search` | `Task<bool>` |

#### ChineseCharacterUtils 类

提供处理中文字符的通用方法。

| 方法 | 描述 | 参数 | 返回值 |
|------|------|------|--------|
| `IsChineseChar` | 判断字符是否是基本汉字或扩展A区汉字 | `char c` | `bool` |
| `IsChineseCodePoint` | 判断指定位置是否是中文字符（支持所有Unicode平面） | `string text, int index` | `bool` |
| `ForEachCodePoint` | 以Unicode代码点方式遍历字符串中的每个字符 | `string text, Action<int, int, int> action` | `void` |
| `CountChineseCharacters` | 以代码点的方式计算文本中的中文字符数量 | `string text` | `int` |
| `ContainsChinese` | 判断文本是否包含中文字符 | `string text` | `bool` |

#### PinyinLibraryOptions 类

拼音库初始化选项。

| 属性 | 类型 | 描述 | 默认值 |
|------|------|------|--------|
| `DatabasePath` | `string` | 数据库文件路径，为空时使用默认路径 | `null` |
| `PreloadCommonChars` | `bool` | 是否预加载常用汉字拼音到内存 | `true` |
| `PrioritizeWordPinyin` | `bool` | 是否优先使用词语拼音（多音字处理） | `true` |
| `MaxWordLength` | `int` | 最大词语长度（用于词语拼音识别） | `8` |
| `DataUpdateUrl` | `string` | 数据库更新URL（可选） | `null` |

#### PinyinResult 类

拼音结果类。

| 属性 | 类型 | 描述 |
|------|------|------|
| `Original` | `string` | 原始文本 |
| `WithToneMark` | `string` | 带声调拼音 |
| `WithoutTone` | `string` | 不带声调拼音 |
| `WithToneNumber` | `string` | 数字声调拼音 |
| `FirstLetter` | `string` | 拼音首字母 |

## 高级功能

### 智能处理多音字

TGZH.Pinyin 内置了常见多音字的上下文识别能力，能根据词语智能判断正确读音：

```csharp
// "行"字在不同上下文中的读音不同
string text1 = await UnifiedPinyinApi.GetTextPinyinAsync("银行");  // háng
string text2 = await UnifiedPinyinApi.GetTextPinyinAsync("行走");  // xíng
```

### 大文本处理

针对大文本处理，使用优化的批处理方法：

```csharp
string largeText = File.ReadAllText("大文件.txt");
string pinyin = await UnifiedPinyinApi.ProcessLargeTextAsync(largeText);
```

### 拼音比较与匹配

```csharp
// 比较拼音相似度
double similarity = PinyinUtils.CompareSimilarity("zhong guo", "zhōng guó");
Console.WriteLine(similarity);  // 输出接近 1.0 的值

// 拼音模式匹配
bool matches = await PinyinUtils.MatchesPinyinPattern("中国人", "zgr");
Console.WriteLine(matches);  // 输出: True
```

## 示例代码

### 完整使用示例

```csharp
using System;
using System.Threading.Tasks;
using TGZH.Pinyin;

class Program
{
    static async Task Main()
    {
        // 初始化拼音库
        await UnifiedPinyinApi.InitializeAsync();
        
        // 示例文本
        string text = "中国是一个有着五千年历史的文明古国";
        
        // 获取带声调的拼音
        string withTone = await UnifiedPinyinApi.GetTextPinyinAsync(text);
        Console.WriteLine($"带声调: {withTone}");
        
        // 获取不带声调的拼音
        string withoutTone = await UnifiedPinyinApi.GetTextPinyinAsync(text, PinyinFormat.WithoutTone);
        Console.WriteLine($"不带声调: {withoutTone}");
        
        // 获取数字声调的拼音
        string withNumber = await UnifiedPinyinApi.GetTextPinyinAsync(text, PinyinFormat.WithToneNumber);
        Console.WriteLine($"数字声调: {withNumber}");
        
        // 获取首字母
        string firstLetter = await UnifiedPinyinApi.GetFirstLettersAsync(text);
        Console.WriteLine($"首字母: {firstLetter}");
        
        // 处理多音字
        string multiTone = await UnifiedPinyinApi.GetTextPinyinAsync("银行和行走");
        Console.WriteLine($"多音字处理: {multiTone}");
        
        // 拼音匹配
        bool match = await PinyinUtils.MatchesPinyinPattern("中国银行", "zgyx");
        Console.WriteLine($"拼音匹配 'zgyx': {match}");
        
        // 使用字符串重载方法处理扩展区字符
        string[] extPinyin = await UnifiedPinyinApi.GetCharPinyinAsync("𠀀");
        Console.WriteLine($"扩展区字符拼音: {string.Join(", ", extPinyin)}");
    }
}
```

### 输出结果

```
带声调: zhōng guó shì yī gè yǒu zhe wǔ qiān nián lì shǐ de wén míng gǔ guó
不带声调: zhong guo shi yi ge you zhe wu qian nian li shi de wen ming gu guo
数字声调: zhong1 guo2 shi4 yi1 ge4 you3 zhe5 wu3 qian1 nian2 li4 shi3 de5 wen2 ming2 gu3 guo2
首字母: zgsy1gyzw5qnlsdwmgg
多音字处理: yín háng hé xíng zǒu
拼音匹配 'zgyx': True
扩展区字符拼音: qiū
```

### 自定义配置示例

```csharp
using System;
using System.Threading.Tasks;
using TGZH.Pinyin;

class Program
{
    static async Task Main()
    {
        // 自定义配置初始化
        var options = new PinyinLibraryOptions
        {
            // 自定义词典路径（如果有）
            DatabasePath = "custom_pinyin_db.sqlite",
            
            // 是否预加载常用汉字
            PreloadCommonChars = true,
            
            // 是否优先使用词语拼音（影响多音字处理）
            PrioritizeWordPinyin = true,
            
            // 最大词语长度限制
            MaxWordLength = 10
        };
        
        // 使用自定义配置初始化
        await UnifiedPinyinApi.InitializeAsync(options);
        
        // 使用功能
        var text = "长城长度很长";
        var pinyin = await UnifiedPinyinApi.GetTextPinyinAsync(text);
        Console.WriteLine($"原文: {text}");
        Console.WriteLine($"拼音: {pinyin}");
    }
}
```

---

## 许可证

此库遵循 MIT 许可证。

## 联系方式

如有问题或建议，请在项目中提交Issue或联系开发者。