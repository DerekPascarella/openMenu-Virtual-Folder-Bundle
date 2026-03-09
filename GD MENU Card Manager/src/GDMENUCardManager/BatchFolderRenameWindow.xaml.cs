using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GDMENUCardManager
{
    public partial class BatchFolderRenameWindow : Window, INotifyPropertyChanged
    {
        private Point _dragStartPoint;
        private FolderTreeNode _draggedNode;
        private FolderTreeNode _clickedNode;
        private FolderTreeNode _currentDropTarget;
        private Stack<UndoOperation> _undoStack = new Stack<UndoOperation>();
        private const int MaxUndoOperations = 10;

        public ObservableCollection<FolderTreeNode> RootNodes { get; } = new ObservableCollection<FolderTreeNode>();

        private bool _canUndo;
        public bool CanUndo
        {
            get => _canUndo;
            set
            {
                if (_canUndo != value)
                {
                    _canUndo = value;
                    OnPropertyChanged();
                }
            }
        }

        private abstract class UndoOperation
        {
            public abstract void Undo();
        }

        private class MoveOperation : UndoOperation
        {
            public FolderTreeNode Node { get; set; }
            public FolderTreeNode OldParent { get; set; }
            public FolderTreeNode NewParent { get; set; }
            public int OldIndex { get; set; }

            public override void Undo()
            {
                // Remove from new parent
                NewParent.Children.Remove(Node);

                // Add back to old parent at original position
                Node.Parent = OldParent;
                if (OldIndex >= OldParent.Children.Count)
                    OldParent.Children.Add(Node);
                else
                    OldParent.Children.Insert(OldIndex, Node);

                // Recalculate counts
                var node = OldParent;
                while (node != null)
                {
                    node.RecalculateCounts();
                    node = node.Parent;
                }
                node = NewParent;
                while (node != null)
                {
                    node.RecalculateCounts();
                    node = node.Parent;
                }

                // Update full paths for the node and all its children
                Node.UpdateFullPath();

                // Sort both old and new parent's children
                OldParent?.SortChildren();
                NewParent?.SortChildren();
            }
        }

        private class RenameOperation : UndoOperation
        {
            public FolderTreeNode Node { get; set; }
            public string OldName { get; set; }
            public string NewName { get; set; }

            public override void Undo()
            {
                Node.Name = OldName;

                // Sort parent's children to reflect old alphabetical order
                Node.Parent?.SortChildren();
            }
        }

        public Dictionary<string, string> FolderMappings { get; private set; }

        public BatchFolderRenameWindow(Dictionary<string, int> folderCounts, int totalItemCount)
        {
            InitializeComponent();
            DataContext = this;

            BuildTree(folderCounts, totalItemCount);
        }

        private void BuildTree(Dictionary<string, int> folderCounts, int totalItemCount)
        {
            var allNodes = new Dictionary<string, FolderTreeNode>(StringComparer.Ordinal);
            var topLevelNodes = new List<FolderTreeNode>();

            // Sort paths by depth (shallowest first) to ensure parents are created before children
            var sortedPaths = folderCounts.Keys
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .OrderBy(p => p.Count(c => c == '\\'))
                .ThenBy(p => p);

            foreach (var path in sortedPaths)
            {
                var segments = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                FolderTreeNode parent = null;
                string currentPath = "";

                for (int i = 0; i < segments.Length; i++)
                {
                    currentPath = i == 0 ? segments[i] : $"{currentPath}\\{segments[i]}";

                    if (!allNodes.ContainsKey(currentPath))
                    {
                        var node = new FolderTreeNode
                        {
                            Name = segments[i],
                            FullPath = currentPath,
                            OriginalFullPath = currentPath,
                            Parent = parent
                        };

                        // Set direct game count only for leaf nodes (full paths in folderCounts)
                        if (currentPath == path && folderCounts.ContainsKey(path))
                        {
                            node.DirectGameCount = folderCounts[path];
                        }

                        allNodes[currentPath] = node;

                        if (parent == null)
                        {
                            topLevelNodes.Add(node);
                        }
                        else
                        {
                            parent.Children.Add(node);
                        }
                    }

                    parent = allNodes[currentPath];
                }
            }

            // Create virtual root node
            var rootNode = new FolderTreeNode
            {
                Name = "(Root)",
                IsRootNode = true,
                IsExpanded = true,
                FullPath = "",
                OriginalFullPath = "",
                DirectGameCount = totalItemCount,
                TotalGameCount = totalItemCount
            };

            // Add all top-level nodes as children of root
            foreach (var topNode in topLevelNodes)
            {
                topNode.Parent = rootNode;
                rootNode.Children.Add(topNode);
                topNode.RecalculateCounts();
            }

            // Sort the entire tree alphanumerically
            rootNode.SortChildren();

            // Don't recalculate root - we set it manually to the total item count
            RootNodes.Add(rootNode);
        }

        private string _editingOriginalName;

        private void TreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement element && element.DataContext is FolderTreeNode node)
            {
                // Don't allow editing the root node
                if (!node.IsRootNode)
                {
                    _editingOriginalName = node.Name;
                    node.IsEditing = true;
                    e.Handled = true;
                }
            }
        }

        private void EditTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        }

        private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FolderTreeNode node)
            {
                node.IsEditing = false;

                // Validate printable ASCII
                if (!Core.Helper.IsValidPrintableAscii(node.Name))
                {
                    MessageBox.Show(
                        "Only printable ASCII characters (letters, numbers, and standard symbols) are supported by openMenu.",
                        "Invalid Characters", MessageBoxButton.OK, MessageBoxImage.Warning);
                    node.Name = "PLEASE RENAME";
                    _editingOriginalName = null;
                    return;
                }

                // Check if name was actually changed
                if (_editingOriginalName != null && _editingOriginalName != node.Name)
                {
                    RecordRename(node, _editingOriginalName, node.Name);

                    // Sort parent's children to reflect new alphabetical order
                    if (node.Parent != null)
                    {
                        node.Parent.SortChildren();
                    }
                }
                _editingOriginalName = null;
            }
        }

        private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox && textBox.DataContext is FolderTreeNode node)
                {
                    if (!Core.Helper.IsValidPrintableAscii(textBox.Text))
                    {
                        MessageBox.Show(
                            "Only printable ASCII characters (letters, numbers, and standard symbols) are supported by openMenu.",
                            "Invalid Characters", MessageBoxButton.OK, MessageBoxImage.Warning);
                        node.Name = "PLEASE RENAME";
                        _editingOriginalName = null;
                    }
                    node.IsEditing = false;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (sender is TextBox textBox && textBox.DataContext is FolderTreeNode node)
                {
                    // Revert changes
                    var originalName = node.OriginalFullPath.Split('\\').Last();
                    node.Name = originalName;
                    node.IsEditing = false;
                }
                e.Handled = true;
            }
        }

        // Drag and Drop implementation
        private TreeViewItem FindTreeViewItem(DependencyObject source)
        {
            while (source != null && !(source is TreeViewItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as TreeViewItem;
        }

        private void TreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);

            // Find and store which node was clicked
            var treeViewItem = FindTreeViewItem(e.OriginalSource as DependencyObject);
            if (treeViewItem != null && treeViewItem.DataContext is FolderTreeNode node)
            {
                _clickedNode = node;
            }
            else
            {
                _clickedNode = null;
            }
        }

        private void TreeView_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedNode == null && _clickedNode != null)
            {
                // Don't allow dragging the root node
                if (_clickedNode.IsRootNode)
                {
                    return;
                }

                Point currentPosition = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPosition;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _draggedNode = _clickedNode;
                    DragDrop.DoDragDrop(FolderTreeView, _clickedNode, DragDropEffects.Move);
                    _draggedNode = null;
                    _clickedNode = null;
                }
            }
        }

        private void TreeView_DragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(FolderTreeNode)))
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void TreeView_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(FolderTreeNode)))
            {
                e.Effects = DragDropEffects.Move;

                // Highlight the current drop target
                var targetElement = e.OriginalSource as FrameworkElement;
                var targetNode = targetElement?.DataContext as FolderTreeNode;

                if (targetNode != _currentDropTarget)
                {
                    // Clear previous highlight
                    if (_currentDropTarget != null)
                    {
                        _currentDropTarget.IsDropTarget = false;
                    }

                    // Set new highlight
                    _currentDropTarget = targetNode;
                    if (_currentDropTarget != null)
                    {
                        _currentDropTarget.IsDropTarget = true;
                    }
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
                ClearDropTarget();
            }
            e.Handled = true;
        }

        private void ClearDropTarget()
        {
            if (_currentDropTarget != null)
            {
                _currentDropTarget.IsDropTarget = false;
                _currentDropTarget = null;
            }
        }

        private void TreeView_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(typeof(FolderTreeNode)))
                {
                    var droppedNode = e.Data.GetData(typeof(FolderTreeNode)) as FolderTreeNode;
                    var targetElement = e.OriginalSource as FrameworkElement;
                    var targetNode = targetElement?.DataContext as FolderTreeNode;

                    if (droppedNode != null && targetNode != null && droppedNode != targetNode)
                    {
                        // Prevent moving the root node
                        if (droppedNode.IsRootNode)
                        {
                            return;
                        }

                        // Prevent dropping node onto itself or its own descendants
                        if (IsDescendant(targetNode, droppedNode))
                        {
                            MessageBox.Show("Cannot move a folder into its own subfolder.", "Invalid Operation", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        // Track for undo
                        RecordMove(droppedNode, droppedNode.Parent, targetNode);

                        // Remove from old parent
                        if (droppedNode.Parent != null)
                        {
                            droppedNode.Parent.Children.Remove(droppedNode);
                            droppedNode.Parent.RecalculateCounts();
                        }

                        // Add to new parent
                        droppedNode.Parent = targetNode;
                        targetNode.Children.Add(droppedNode);
                        targetNode.IsExpanded = true;

                        // Recalculate counts for entire tree path
                        var node = targetNode;
                        while (node != null)
                        {
                            node.RecalculateCounts();
                            node = node.Parent;
                        }

                        // Update full paths for the dropped node and all its children
                        droppedNode.UpdateFullPath();

                        // Sort children of the target node
                        targetNode.SortChildren();
                    }
                }
            }
            finally
            {
                ClearDropTarget();
            }
        }

        private void TreeView_DragLeave(object sender, DragEventArgs e)
        {
            ClearDropTarget();
        }

        private bool IsDescendant(FolderTreeNode potentialDescendant, FolderTreeNode ancestor)
        {
            var current = potentialDescendant;
            while (current != null)
            {
                if (current == ancestor)
                    return true;
                current = current.Parent;
            }
            return false;
        }

        private void RecordMove(FolderTreeNode node, FolderTreeNode oldParent, FolderTreeNode newParent)
        {
            var oldIndex = oldParent?.Children.IndexOf(node) ?? -1;

            var operation = new MoveOperation
            {
                Node = node,
                OldParent = oldParent,
                NewParent = newParent,
                OldIndex = oldIndex
            };

            // Limit to 10 operations
            if (_undoStack.Count >= MaxUndoOperations)
            {
                // Remove oldest operation (at bottom of stack)
                var temp = new Stack<UndoOperation>(_undoStack.Reverse().Skip(1).Reverse());
                _undoStack = temp;
            }

            _undoStack.Push(operation);
            CanUndo = true;
        }

        private void RecordRename(FolderTreeNode node, string oldName, string newName)
        {
            var operation = new RenameOperation
            {
                Node = node,
                OldName = oldName,
                NewName = newName
            };

            // Limit to 10 operations
            if (_undoStack.Count >= MaxUndoOperations)
            {
                // Remove oldest operation (at bottom of stack)
                var temp = new Stack<UndoOperation>(_undoStack.Reverse().Skip(1).Reverse());
                _undoStack = temp;
            }

            _undoStack.Push(operation);
            CanUndo = true;
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0)
            {
                var operation = _undoStack.Pop();
                operation.Undo();
                CanUndo = _undoStack.Count > 0;
            }
        }

        private void CollectMappings(FolderTreeNode node, Dictionary<string, string> mappings)
        {
            // Skip the virtual root node
            if (!node.IsRootNode)
            {
                if (node.OriginalFullPath != node.FullPath)
                {
                    mappings[node.OriginalFullPath] = node.FullPath;
                }
            }

            foreach (var child in node.Children)
            {
                CollectMappings(child, mappings);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            FolderMappings = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var root in RootNodes)
            {
                CollectMappings(root, FolderMappings);
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
