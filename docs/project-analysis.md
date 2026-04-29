# CSV Viewer WPF 项目分析文档

## 1. 项目概述

CSV Viewer 是一个基于 .NET 8 WPF 的 Windows 桌面 CSV/TXT 只读阅读器。项目目标是提供接近 Excel 与 VS Code 体验的轻量级数据查看工具，面向需要频繁查看本地或 SVN 远程 CSV/文本表格文件的使用场景。

当前项目已经具备多页签、文件夹树、最近打开、快速打开、搜索高亮、冻结窗格、临时单元格刷色、全局设置和 SVN 远程文件读取能力。

## 2. 技术栈

| 类型 | 技术 |
| --- | --- |
| UI 框架 | WPF |
| 运行平台 | .NET 8 Windows |
| 语言 | C# |
| CSV 解析 | CsvHelper |
| 编码支持 | System.Text.Encoding.CodePages |
| 架构模式 | MVVM + 少量 Code-behind 事件协调 |
| 远程文件 | SVN CLI，依赖本机 `svn.exe` |

## 3. 项目结构

| 路径 | 职责 |
| --- | --- |
| `App.xaml` | 全局主题资源、控件样式、滚动条样式 |
| `MainWindow.xaml` / `MainWindow.xaml.cs` | 主窗口、工具栏、文件树、多页签宿主、全局快捷键和 UI 事件转发 |
| `CsvDocumentGrid.xaml` / `CsvDocumentGrid.xaml.cs` | 单个文档的表格控件、冻结窗格、复制、列宽同步、刷色、表头整列选择 |
| `QuickOpenWindow.xaml` / `.cs` | `Ctrl+P` 快速打开窗口 |
| `SettingsWindow.xaml` / `.cs` | 全局设置窗口 |
| `ViewModels/MainViewModel.cs` | 应用级状态、命令、文档集合、SVN 模式、文件树、最近文件 |
| `ViewModels/CsvDocumentViewModel.cs` | 单文档数据、搜索、冻结状态、远程重载 |
| `Services/CsvFileService.cs` | CSV/TXT 读取、编码和分隔符处理、DataTable 构建 |
| `Services/SvnFolderService.cs` | SVN 远程列目录和单文件缓存 |
| `Services/AppStateService.cs` | 应用状态持久化 |
| `Services/ClipboardService.cs` | 表格选区复制 |
| `Models/*` | 应用状态、文件树节点、最近文件、加载结果等数据模型 |
| `Converters/*` | 搜索高亮、文档可见性等 WPF 转换器 |
| `Commands/RelayCommand.cs` | MVVM 命令封装 |

## 4. 核心功能

### 4.1 文件打开与多文档

应用支持打开本地 CSV/TXT 文件，也支持从左侧文件树或最近打开列表进入文件。每个打开的文件对应一个 `CsvDocumentViewModel`，并在 `MainViewModel.Documents` 中维护。

多文档使用页签展示。页签头由 `TabControl` 渲染，内容区由 `ItemsControl` 承载每个文档的 `CsvDocumentGrid`，这样每个页签都有独立表格控件实例，减少切换时重建表格造成的卡顿。

### 4.2 CSV 加载

`CsvFileService` 负责文件加载流程：

1. 检测或使用指定编码。
2. 检测或使用指定分隔符。
3. 使用 CsvHelper 读取全部行。
4. 判断首行是否像表头。
5. 构建 `DataTable`。
6. 无表头时生成 Excel 风格列名，例如 `A`、`B`、`AA`。

当前实现以一次性加载为主，适合中小型文件。超大文件场景下存在内存和首屏加载时间压力。

### 4.3 表格显示

表格显示由 `CsvDocumentGrid` 负责。核心控件是 WPF `DataGrid`，主表格绑定 `ScrollableTableView`，冻结行区域绑定 `FrozenTableView`。

关键交互包括：

| 功能 | 实现方式 |
| --- | --- |
| 只读显示 | `DataGrid.IsReadOnly=True` |
| 自动生成列 | `AutoGenerateColumns=True` |
| 禁止排序 | `CanUserSortColumns=False` |
| 行号 | `LoadingRow` 设置 `Row.Header` |
| 搜索高亮 | `TextBlock.ElementStyle` + `SearchHighlightBrushConverter` |
| 临时刷色 | 控件内存字典保存单元格坐标和 Brush |
| 列宽同步 | 监听主表格列宽变化，同步到冻结行表格 |
| 横向滚动同步 | 两个表格的 `ScrollChanged` 互相同步 |

### 4.4 冻结窗格

WPF 原生 `DataGrid` 只支持冻结列，不支持冻结行。当前项目通过双 `DataGrid` 实现冻结行：

| 区域 | 数据源 |
| --- | --- |
| 冻结行区域 | `FrozenTableView` |
| 可滚动主体区域 | `ScrollableTableView` |

冻结到当前单元格时，会同时记录冻结行数和冻结列数。冻结列使用 `DataGrid.FrozenColumnCount`，冻结行通过拆分 `DataTable` 视图实现。

近期已处理的问题：冻结/取消冻结会触发表格列重新生成，导致用户调整过的列宽被重置。当前实现会在冻结前保存列宽，在冻结后及列自动生成完成后恢复列宽。

### 4.5 搜索

搜索入口位于主工具栏，文本绑定到当前 `SelectedDocument.SearchText`。回车或点击搜索会触发 `ApplySearchCommand`。

搜索逻辑位于 `CsvDocumentViewModel.ApplySearchAsync`：

1. 空关键字时恢复原始表。
2. 非空关键字时遍历 `_sourceTable`。
3. 任意单元格包含关键字则导入过滤表。
4. 将过滤结果设为 `_activeTable`。
5. 搜索命中的单元格由 `SearchHighlightBrushConverter` 高亮。

当前搜索是内存全表扫描。对大文件可以后续引入后台索引、分页或增量搜索。

### 4.6 临时刷色

刷色功能位于主工具栏 `刷色` 按钮。使用方式：

1. 选中一个或多个单元格。
2. 点击 `刷色`。
3. 当前选区被标记为临时黄色。

实现特性：

| 特性 | 说明 |
| --- | --- |
| 存储位置 | `CsvDocumentGrid` 控件实例内存字典 |
| 生命周期 | 页签关闭后控件销毁，颜色自然清空 |
| 文件影响 | 不写入 CSV，不持久化 |
| 冻结兼容 | 冻结区和滚动区按视觉行号映射同一坐标体系 |
| 整列刷色 | 点击表头选中整列后执行刷色 |

### 4.7 快速打开

`Ctrl+P` 打开 `QuickOpenWindow`，交互参考 VS Code Command Palette。

当前设计：

| 项目 | 行为 |
| --- | --- |
| 空输入 | 显示最近打开 |
| 输入文件名 | 在文件树索引中模糊搜索 |
| Enter | 打开当前选中项 |
| Esc | 关闭弹窗 |
| 外观 | 无标题栏、透明圆角、深色面板、禁用横向滚动条 |

### 4.8 SVN 模式

SVN 模式通过工具栏 Toggle 开启。项目不执行全量 checkout，而是使用 SVN CLI 远程列目录和按需读取单文件。

核心流程：

1. 根据路径模板和分支生成 SVN 根 URL。
2. 调用 `svn list -R <url>` 获取远程目录文件列表。
3. 构建左侧远程文件树。
4. 打开文件时调用 `svn cat <file-url>`。
5. 将文件缓存到进程级临时目录。
6. 用本地缓存路径交给 `CsvFileService` 解析。

当前缓存路径包含进程 ID，属于会话级临时缓存。项目启动时还会清理历史 checkout 缓存目录。

### 4.9 最近打开与状态持久化

应用状态由 `AppStateService` 持久化，主要包括：

| 状态 | 说明 |
| --- | --- |
| 最近打开 | 支持本地文件和 SVN 文件 |
| 最近文件夹 | 启动后恢复文件树 |
| SVN 模式 | 上次是否开启 SVN |
| SVN 路径模板 | 支持 `{0}` 分支占位符 |
| SVN 分支预设 | 可在设置窗口增删 |
| 编码/分隔符 | 全局设置 |
| 是否隐藏表头 | 全局设置 |

## 5. 架构分析

### 5.1 分层结构

项目整体采用轻量 MVVM：

| 层级 | 主要职责 |
| --- | --- |
| View | XAML 布局、控件样式、用户事件入口 |
| ViewModel | 应用状态、命令、业务状态变更 |
| Service | CSV 解析、SVN 操作、剪贴板、状态持久化 |
| Model | 持久化和传输数据结构 |

Code-behind 没有完全消除，主要承担 WPF 控件级协调，例如：

| 文件 | Code-behind 职责 |
| --- | --- |
| `MainWindow.xaml.cs` | 快捷键、窗口弹出、活动表格控件定位、按钮事件转发 |
| `CsvDocumentGrid.xaml.cs` | DataGrid 事件、冻结、复制、刷色、列宽/滚动同步 |
| `QuickOpenWindow.xaml.cs` | 命令面板键盘交互和结果打开 |
| `SettingsWindow.xaml.cs` | 关闭窗口 |

该取舍适合当前 WPF 项目，因为很多行为与控件实例、视觉树、滚动同步强相关，放在 ViewModel 会增加复杂度。

### 5.2 数据流

本地文件数据流：

```text
用户打开文件
-> MainViewModel.OpenFileAsync
-> CsvFileService.Load
-> CsvLoadResult
-> CsvDocumentViewModel
-> MainViewModel.Documents
-> CsvDocumentGrid.DataContext
-> DataGrid 渲染 DataView
```

SVN 文件数据流：

```text
用户开启 SVN 模式
-> MainViewModel.SetSvnModeAsync
-> SvnFolderService.ListFilesAsync
-> 构建 FileTreeNode
-> 用户选择文件
-> SvnFolderService.CacheFileAsync
-> CsvFileService.Load
-> CsvDocumentViewModel
-> DataGrid 渲染
```

冻结窗格数据流：

```text
用户选中单元格
-> MainWindow 转发到活动 CsvDocumentGrid
-> CsvDocumentGrid 计算冻结行/列
-> CsvDocumentViewModel.SetFrozenRowCount
-> RebuildSplitViews
-> FrozenTableView / ScrollableTableView 更新
-> 双 DataGrid 重绘并恢复列宽
```

刷色数据流：

```text
用户选中单元格或点击表头选中整列
-> 点击刷色
-> CsvDocumentGrid 读取 SelectedCells
-> 转换为视觉行号 + 列名
-> 写入控件内存字典
-> CellPaintVersion 自增
-> DataGridCell 背景 MultiBinding 重新计算
```

## 6. UI 与交互设计分析

项目当前 UI 风格已经向现代桌面工具升级：

| 区域 | 风格 |
| --- | --- |
| 顶部工具栏 | 深色命令栏 |
| 左侧文件区 | 卡片式侧栏 |
| 内容区 | 卡片式表格容器 |
| 页签 | VS Code 风格深色标签 |
| 快速打开 | VS Code Command Palette 风格 |
| 滚动条 | 全局 VS Code 风格细滚动条 |
| 设置窗口 | 卡片式表单布局 |

整体交互更偏向开发者工具，而不是传统 Office 软件。表格行为则保留 Excel 式操作预期，例如冻结窗格、表头整列选择和行列编号。

## 7. 当前优势

| 优势 | 说明 |
| --- | --- |
| 功能覆盖完整 | 已覆盖本地、SVN、多页签、搜索、冻结、刷色、快速打开 |
| 远程读取策略合理 | SVN 使用 list + cat，避免全量 checkout |
| 页签切换体验较好 | 每文档独立表格控件实例，减少反复重建 |
| UI 统一度提升 | 全局主题、滚动条、页签、弹窗样式一致 |
| 状态持久化实用 | 常用设置、分支、最近文件都可恢复 |
| 临时标注不污染数据 | 刷色只在控件内存中保存，不影响源文件 |

## 8. 当前风险与限制

### 8.1 大文件性能

当前 CSV 加载会一次性读入所有行并构建 `DataTable`。搜索、冻结和过滤也基于内存表操作。对于几十万行以上文件，可能出现明显内存占用和 UI 延迟。

建议后续评估：

| 方向 | 说明 |
| --- | --- |
| 分页加载 | 只加载当前可视范围或分页数据 |
| 虚拟数据源 | 使用自定义集合替代完整 `DataTable` |
| 后台索引 | 搜索时避免每次全表扫描 |
| 流式预览 | 首屏优先展示前 N 行，后台继续加载 |

### 8.2 冻结行实现成本

冻结行通过复制 `DataTable` 行实现，会产生额外内存和同步复杂度。冻结行数量很大时，内存成本会增加。

建议后续评估是否需要：

| 方向 | 说明 |
| --- | --- |
| 限制冻结行数量 | 避免误冻结大量行 |
| 自定义 Grid 渲染 | 降低双 DataGrid 同步复杂度 |
| 行视图引用 | 尽量避免复制行数据 |

### 8.3 SVN 依赖外部命令

SVN 功能依赖本机安装 `svn.exe` 并加入 PATH。缺失时会抛出可读错误，但用户首次使用前仍需要环境准备。

可优化点：

| 方向 | 说明 |
| --- | --- |
| 设置页检测 SVN | 展示 svn 可用性和版本 |
| 错误提示增强 | 提供安装指引 |
| 认证策略说明 | 明确依赖 SVN 客户端已有认证缓存 |

### 8.4 UI 事件逻辑偏多

`CsvDocumentGrid.xaml.cs` 承担了大量控件级逻辑，包括冻结、滚动同步、列宽同步、刷色、整列选择。短期可维护，长期可能变大。

建议后续按功能拆分为内部辅助类：

| 候选类 | 职责 |
| --- | --- |
| `DataGridFreezeCoordinator` | 冻结、滚动、列宽同步 |
| `CellPaintCoordinator` | 单元格刷色和整列选择 |
| `DataGridSelectionHelper` | 选区计算和坐标转换 |

### 8.5 测试覆盖不足

当前项目主要依赖手工运行验证。CSV 解析、编码检测、分隔符检测、SVN URL 拼接、状态持久化等逻辑适合补充单元测试。

优先测试建议：

| 模块 | 测试重点 |
| --- | --- |
| `CsvFileService` | 表头识别、列名去重、空文件、不等列数 |
| `DelimiterDetectionService` | 逗号、Tab、分号、竖线检测 |
| `EncodingDetectionService` | UTF-8、GBK、BOM 文件 |
| `SvnFolderService.CombineUrl` | URL 拼接边界 |
| `AppStateService` | 默认值、损坏配置恢复 |

## 9. 后续优化路线

### 9.1 短期优化

| 优先级 | 建议 | 价值 |
| --- | --- | --- |
| 高 | 给刷色增加颜色选择和清除刷色 | 提升标注能力 |
| 高 | 增加当前选区状态提示 | 用户知道选中了多少行/列/格 |
| 中 | Quick Open 支持拼音或更强模糊匹配 | 提升找文件效率 |
| 中 | 设置页增加 SVN 可用性检查 | 降低 SVN 使用门槛 |
| 中 | 表格右键菜单 | 复制、刷色、清除颜色、冻结更自然 |

### 9.2 中期优化

| 优先级 | 建议 | 价值 |
| --- | --- | --- |
| 高 | 大文件加载优化 | 支撑更大 CSV 数据 |
| 高 | 搜索异步取消 | 避免连续输入/搜索造成等待 |
| 中 | 文件树懒加载和索引缓存 | 提升大目录体验 |
| 中 | 最近文件分组 | 区分本地和 SVN 来源 |
| 中 | 错误面板统一化 | 替代分散 MessageBox |

### 9.3 长期优化

| 优先级 | 建议 | 价值 |
| --- | --- | --- |
| 高 | 引入测试项目 | 保证核心解析逻辑稳定 |
| 中 | 自定义高性能表格渲染 | 替代 DataGrid 限制 |
| 中 | 插件式数据源 | 后续支持 HTTP、Git、数据库导出等 |
| 低 | 主题系统 | 支持浅色/深色切换和自定义主题 |

## 10. 维护建议

1. UI 样式优先放在 `App.xaml`，窗口内仅保留窗口特有样式。
2. 与控件视觉树强相关的逻辑可以留在 Code-behind，但应避免混入业务状态。
3. 应用级状态继续放在 `MainViewModel`，单文档状态继续放在 `CsvDocumentViewModel`。
4. 临时 UI 状态，例如刷色、滚动位置、列宽，可以保留在 `CsvDocumentGrid`。
5. 持久化状态必须明确生命周期，避免把临时标注误写入 `AppState`。
6. SVN 相关逻辑应继续避免 checkout 全量目录，保持按需读取策略。
7. 每次改动 XAML 后建议执行 `dotnet build`，必要时再 `dotnet run` 验证运行期资源解析。

## 11. 构建与运行

构建命令：

```powershell
dotnet build "CsvViewer.csproj" /p:UseAppHost=false /p:OutputPath="$env:TEMP\CsvViewerVerify\"
```

运行命令：

```powershell
dotnet run --project "CsvViewer.csproj"
```

## 12. 总结

当前项目已经从基础 CSV 查看器演进为偏开发者工具风格的桌面表格阅读器。功能上覆盖本地和 SVN 远程文件查看，交互上融合 Excel 的表格操作习惯与 VS Code 的快速打开、深色标签和工具化视觉风格。

后续主要技术挑战集中在大文件性能、`CsvDocumentGrid` 复杂度控制、SVN 环境易用性和自动化测试覆盖。短期继续沿用当前架构是合理的；如果未来目标变成处理超大 CSV 或复杂标注编辑，则需要重新评估 `DataTable + DataGrid` 的承载能力。
