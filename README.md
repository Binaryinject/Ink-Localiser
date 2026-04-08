# InkTagger

[English](README.en.md) | [中文](README.md)

**一个简单的工具，帮助 Ink 项目实现本地化和语音配音。**

![Tagged Ink File](docs/demo-tagged.png)

![Generated CSV File](docs/demo-csv.png)

## 目录
- [概述](#概述)
- [命令行工具](#命令行工具)
- [DryDB 数据库](#drydb-数据库)
- [限制](#限制)
- [开发流程](#开发流程)
- [ID 格式](#id-格式)
- [发布版本](#发布版本)
- [注意事项](#注意事项)
- [技术实现](#技术实现)
- [相关项目](#相关项目)
- [致谢](#致谢)
- [许可证](#许可证)

## 概述

Inkle 的 Ink 是一门优秀的叙事脚本语言，非常适合制作分支剧情游戏。

Ink 的设计初衷是拼接文本片段，因此原生并不支持本地化，也没有将语音文件与文本行关联的机制。

但实际上，很多工作室只是用 Ink 来编写完整的对话文本，并不使用高级的文本拼接功能。这就带来了一个问题：怎么翻译这些文本？怎么播放对应的配音？

本工具会扫描 Ink 文件，为每行文本生成唯一的本地化 ID，并以标签形式写回文件末尾。这样，每行文本都有了唯一标识，可以用于本地化查询或触发对应的语音。

以 `@` 开头的行会被忽略，不会生成 ID。可以用来标记命令行或元数据。

工具还支持导出 CSV 或 JSON 文件，包含所有 ID 和对应文本，方便翻译工作。

每次运行时，工具会保留已有的 ID，只为新增的文本行生成新 ID。

举个例子，原始文件：
![Source Ink File](docs/demo-plain.png)

处理后变成：
![Tagged Ink File](docs/demo-tagged.png)

同时生成 CSV 文件：
![Generated CSV File](docs/demo-csv.png)

以及 JSON 文件：
![Generated JSON File](docs/demo-json.png)

## 命令行工具

几个简单示例：

扫描 `inkFiles` 文件夹下所有 Ink 文件，生成 ID 并导出到 `output/strings.json`：

`InkTagger.exe --folder=inkFiles/ --json=output/strings.json`

扫描 `inkFiles` 文件夹下以 `start` 开头的 Ink 文件，导出到 `output/strings.csv`：

`InkTagger.exe --folder=inkFiles/ --filePattern=start*.ink --csv=output/strings.csv`

### 参数说明

- `--folder=<folder>`：Ink 文件所在的根目录（相对路径）。默认为当前目录。

- `--filePattern=<pattern>`：文件匹配模式，如 `--filePattern=start-*.ink`。默认为 `*.ink`。

- `--csv=<csvPath>`：CSV 导出路径，如 `--csv=output/strings.csv`。不指定则不导出。

- `--json=<jsonPath>`：JSON 导出路径，如 `--json=output/strings.json`。不指定则不导出。

- `--drydb=<path>`：DryDB 数据库输出目录。配合本地化流程使用时，会基于 CSV 生成 `.drydb` 文件，默认启用 Zstandard 压缩。

- `--drydb-no-compress`：禁用 DryDB 的 Zstandard 压缩。

- `--drydb-table-prefix=<prefix>`：为 DryDB 表名添加前缀，如 `--drydb-table-prefix=loc_`。

- `--drydb-csv=<csvFolder>`：跳过 Ink 处理，直接将指定目录下的 CSV 文件转换为 DryDB。默认递归搜索子目录。

- `--drydb-csv-out=<outFolder>`：指定 DryDB 输出目录。不指定则输出到 CSV 同级目录。

- `--only-csv-to-drydb`：仅执行 CSV 转 DryDB，跳过 Ink 处理。需配合 `--drydb-csv` 使用。

说明：
- `--drydb-csv` 默认递归搜索（使用 `SearchOption.AllDirectories`）。如需非递归，请指定只包含目标 CSV 的单一目录。
- 单独使用 `--drydb`（不带 `--drydb-csv`）时，会在正常流程中基于生成的 CSV 产出 DryDB 文件。

- `--retag`：重新生成所有 ID，不保留旧 ID。

- `--help`：显示帮助。

## DryDB 数据库

### 简介

DryDB 是一种基于 B+Tree 的键值数据库格式，专为只读场景优化，非常适合游戏运行时的本地化数据存储。

项目地址：[hadashiA/DryDB](https://github.com/hadashiA/DryDB)

说明：当前本项目已统一使用 `DryDB` 包名、命令行参数与 `.drydb` 文件后缀。

### 优势

相比 CSV 或 JSON：

- **查询快**：B+Tree 结构，O(log n) 时间复杂度，远快于线性搜索
- **内存省**：按需加载，无需全量载入内存
- **体积小**：内置 Zstandard 压缩
- **只读优化**：专为不可变数据设计

### 使用示例

**本地化流程中同时生成 DryDB：**
```bash
InkTagger.exe --folder=inkFiles/ --csv=output/strings.csv --drydb=output/
```

**单独将 CSV 转为 DryDB：**
```bash
InkTagger.exe --only-csv-to-drydb --drydb-csv=localization/ --drydb-csv-out=output/
```

**生成无压缩的 DryDB 并添加表前缀：**
```bash
InkTagger.exe --folder=inkFiles/ --csv=output/strings.csv --drydb=output/ --drydb-no-compress --drydb-table-prefix=loc_
```

### 文件结构

每个 CSV 对应一个 `.drydb` 文件，表名取自文件名（可加前缀）。运行时用本地化 ID 作为 key 查询即可获取对应文本。

## 限制

Ink 支持文本片段拼接，例如：
```
{shuffle:
- Here is a sentence <>
- Here is a different sentence <>
}
that will end up saying the same thing.

* I talked about sailing ships [] and the preponderance of seamonsters.
    -> MarineLife
* I didn't like monkeys [at all.] in any way whatsoever.
    -> MonkeyBusiness
```

**本工具不支持这种拼接**，原因如下：

* **本地化需求**：翻译拼接的文本片段非常困难，不同语言的语法结构差异很大。
* **配音需求**：演员无法自然地朗读拼接的句子片段。

如果单行存在多个文本片段或使用了 `<>` 拼接，工具会报错。

## 开发流程

正常开发 Ink 脚本，把它当作游戏的主文本源。

用 InkTagger 为文本添加 ID 并导出翻译文件。每次修改 Ink 后重新运行即可，已有 ID 会保留。

运行时加载 Ink 和对应语言的 JSON/CSV。推进剧情时，不要直接用 Ink 返回的文本，而是读取标签中的 ID，再从翻译文件中查询实际文本。同样的 ID 也可以用来触发对应的语音文件。

简单说：**运行时只用 Ink 做流程控制，文本内容从外部文件读取。**

**伪代码：**
```csharp
var story = new Story(storyJsonAsset);

// 加载 DryDB 数据库
var dryDBPath = Path.Combine(Application.streamingAssetsPath, "strings.drydb");
var database = await ReadOnlyDatabase.OpenFileAsync(dryDBPath);
var locTable = database.GetTable("your_table_name"); // 表名来自 Ink 文件名

while (story.canContinue) {
    story.Continue();

    // 从标签中提取 ID，如 #id:Main_Intro_45EW
    var stringID = extractIDFromTags(story.currentTags);

    // 用 ID 查询本地化文本（直接使用字符串 key）
    var valueBytes = locTable.Get(stringID);
    var localizedText = Encoding.UTF8.GetString(valueBytes);
    DisplayText(localizedText);

    // 也可以用同一个 ID 播放语音
    PlayAudio(stringID);

    // 处理选项
    foreach (var choice in story.currentChoices) {
        var choiceID = extractIDFromTags(choice.tags);
        var choiceBytes = locTable.Get(choiceID);
        var localizedChoice = Encoding.UTF8.GetString(choiceBytes);
        DisplayChoice(localizedChoice);
    }
}
```

## ID 格式

ID 结构：`<filename>_<knot>(_<stitch>)_<code>`

* `filename`：Ink 文件名
* `knot`：所在 knot 名称
* `stitch`：所在 stitch 名称（如有）
* `code`：4 位随机码，保证在当前 knot/stitch 内唯一

这种格式方便开发时定位文本来源。ID 一旦生成就不会变，即使移动了文本位置。如果想重新生成，删掉旧 ID 再运行工具即可。

## 发布版本

各平台的发布版本在[这里](https://github.com/Binaryinject/InkTagger/releases)。

也提供 Lib 版本，可作为 DLL 集成到工具链中。依赖 Inkle 的 `ink_compiler.dll` 和 `ink-engine-runtime.dll`。

## 注意事项

这个工具比较简单，可能有未覆盖的边界情况。

**警告**：工具会直接修改 `.ink` 文件！强烈建议使用版本控制。

**Inky 不会自动刷新**：如果在 Inky 打开时运行工具，需要按 Ctrl-R（Mac 上是 CMD-R）手动刷新。

## 技术实现

使用 .NET / C# 开发。

内部使用 Inkle 的 **Ink Parser** 解析文件，提取文本内容。测试覆盖有限，如果遇到问题欢迎反馈。

## 相关项目

- [InkCommandStyle](https://github.com/Binaryinject/InkCommandStyle) - VSCode 的 Ink 语法高亮插件，功能包括：
  - 完整语法高亮：对话格式、选择标记、跳转符号、Knot/Stitch 定义、`@` 自定义命令等
  - 智能跳转：Ctrl+点击跳转到 Knot/Stitch 定义
  - 可视化调试面板：树形展示结构、选项统计、点击跳转源码
  - 故事预览：交互式运行测试、自动隐藏 `@` 命令、实时更新
  - 大纲视图：快速浏览文件结构

## 致谢

感谢 [Inkle](https://www.inklestudios.com/) 和 **Joseph Humfrey** 创造了 [Ink](https://www.inklestudios.com/ink/) 这么好用的工具。

## 许可证

MIT 许可证，详见根目录。欢迎反馈使用体验！
