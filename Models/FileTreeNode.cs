using System.Collections.ObjectModel;

namespace CsvViewer.Models;

/// <summary>
/// 文件夹树节点，用于在 TreeView 中展示文件夹和 CSV 文件。
/// </summary>
public sealed class FileTreeNode
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string? RelativePath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsRemote { get; init; }
    public ObservableCollection<FileTreeNode> Children { get; } = [];

    /// <summary>
    /// 指示当前目录节点是否含有占位子节点（用于支持懒加载展开）。
    /// </summary>
    public bool HasDummyChild { get; set; }
}
