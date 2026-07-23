using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MsBox.Avalonia;
using MsBox.Avalonia.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public class FolderToolsWindow : Window, INotifyPropertyChanged
    {
        // in-process format so we can hand the node itself to the drop side without it
        // ever going near the system clipboard
        private static readonly DataFormat<FolderTreeNode> NodeFormat =
            DataFormat.CreateInProcessFormat<FolderTreeNode>("gdmcm-folder-tree-node");
        // macOS drops the in-process format from the drag before it hands it to the OS and
        // then rejects a drag that carries nothing, so we tack on this small marker too
        private static readonly DataFormat<byte[]> NodeMarkerFormat =
            DataFormat.CreateBytesApplicationFormat("gdmcm-folder-tree-node-marker");

        private readonly Core.Manager _manager;

        public new event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Folder Artwork tab

        public ObservableCollection<FolderArtRow> Folders { get; } = new ObservableCollection<FolderArtRow>();
        public ObservableCollection<FolderArtOrphanRow> Orphans { get; } = new ObservableCollection<FolderArtOrphanRow>();

        public bool HasOrphans => Orphans.Count > 0;

        // Batch Folder Move/Rename tab

        private Point _dragStartPoint;
        // DoDragDropAsync wants the press event that started things, so hold onto it until
        // the pointer has moved far enough to count as a drag
        private PointerPressedEventArgs _dragTriggerEvent;
        private FolderTreeNode _draggedNode;
        private FolderTreeNode _clickedNode;
        private FolderTreeNode _currentDropTarget;
        private Stack<UndoOperation> _undoStack = new Stack<UndoOperation>();
        private const int MaxUndoOperations = 10;
        private string _editingOriginalName;

        public ObservableCollection<FolderTreeNode> RootNodes { get; } = new ObservableCollection<FolderTreeNode>();

        // The batch rename tab is disabled when a search filter is active or the list has no folders.
        public bool CanBatchRename { get; }

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

        // Set when the user clicks Apply Changes. The main window applies these mappings after the dialog closes.
        public Dictionary<string, string> FolderMappings { get; private set; }

        public FolderToolsWindow()
        {
            InitializeComponent();
        }

        public FolderToolsWindow(Core.Manager manager, Dictionary<string, int> folderCounts, int totalItemCount)
        {
            InitializeComponent();

            _manager = manager;
            RefreshLists();

            CanBatchRename = folderCounts != null && folderCounts.Count > 0;
            if (CanBatchRename)
                BuildTree(folderCounts, totalItemCount);

            DataContext = this;

            var tree = this.FindControl<TreeView>("FolderTreeView");
            tree.AddHandler(DragDrop.DragOverEvent, Tree_DragOver);
            tree.AddHandler(DragDrop.DropEvent, Tree_Drop);
            tree.AddHandler(DragDrop.DragLeaveEvent, Tree_DragLeave);

            // Tunnel strategy so these fire before TreeViewItem handles the pointer for selection
            tree.AddHandler(PointerPressedEvent, Tree_PointerPressed, RoutingStrategies.Tunnel);
            tree.AddHandler(PointerMovedEvent, Tree_PointerMoved, RoutingStrategies.Tunnel);

            tree.DoubleTapped += Tree_DoubleTapped;

            // KeyDown instead of KeyUp so the rename editor's handled Escape doesn't also close the window
            this.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    Close();
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // Folder Artwork tab

        private void RefreshLists()
        {
            var paths = _manager.GetAllFolderArtPaths();

            Folders.Clear();
            foreach (var path in paths)
                Folders.Add(new FolderArtRow(path, _manager.FolderArtDat?.HasArtworkForFolder(path) == true));

            Orphans.Clear();
            if (_manager.FolderArtDat != null)
            {
                foreach (var (key, orphanPath) in _manager.FolderArtDat.GetOrphans(paths))
                    Orphans.Add(new FolderArtOrphanRow { Key = key, Path = orphanPath });
            }

            OnPropertyChanged(nameof(HasOrphans));
        }

        private async void AssignManage_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.CommandParameter is FolderArtRow row))
                return;

            var paths = Folders.Select(f => f.Path).ToList();
            var index = paths.IndexOf(row.Path);
            if (index < 0)
                return;

            var editor = new FolderArtEditorWindow(_manager, paths, index);
            await editor.ShowDialog(this);

            RefreshLists();
        }

        private async void OrphanView_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.CommandParameter is FolderArtOrphanRow row))
                return;

            var viewer = new FolderArtEditorWindow(_manager, row.Key);
            await viewer.ShowDialog(this);
        }

        private async void OrphanReassign_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.CommandParameter is FolderArtOrphanRow row))
                return;

            var picker = new FolderPickerWindow(_manager.GetAllFolderArtPaths());
            var selected = await picker.ShowDialog<string>(this);
            if (string.IsNullOrEmpty(selected))
                return;

            _manager.FolderArtDat?.ReassignKeyToFolder(row.Key, selected);
            RefreshLists();
        }

        private async void OrphanDelete_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as Button)?.CommandParameter is FolderArtOrphanRow row))
                return;

            var result = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams
            {
                ContentTitle = "Confirmation",
                ContentMessage = $"Delete unassigned artwork for '{row.Display}'? This cannot be undone once DAT files are saved.",
                Icon = MsBox.Avalonia.Enums.Icon.Warning,
                ShowInCenter = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = new ButtonDefinition[]
                {
                    new ButtonDefinition { Name = "Delete" },
                    new ButtonDefinition { Name = "Cancel" }
                }
            }).ShowWindowDialogAsync(this);

            if (result == "Delete")
            {
                _manager.FolderArtDat?.DeleteEntryForKey(row.Key);
                RefreshLists();
            }
        }

        // Batch Folder Move/Rename tab

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

            // Don't recalculate root, we set it manually to the total item count.
            RootNodes.Add(rootNode);
        }

        // Inline rename

        private void Tree_DoubleTapped(object sender, RoutedEventArgs e)
        {
            var node = (e.Source as Control)?.DataContext as FolderTreeNode;
            if (node == null || node.IsRootNode)
                return;

            _editingOriginalName = node.Name;
            node.IsEditing = true;
            e.Handled = true;
        }

        private void EditTextBox_AttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Focus();
                textBox.SelectionStart = 0;
                textBox.SelectionEnd = textBox.Text?.Length ?? 0;
            }
        }

        private async void CommitRename(FolderTreeNode node)
        {
            node.IsEditing = false;

            // Validate printable ASCII
            if (!Core.Helper.IsValidPrintableAscii(node.Name))
            {
                await MessageBoxManager.GetMessageBoxStandard("Information",
                    "Only printable ASCII characters (letters, numbers, and standard symbols) are supported by openMenu.",
                    icon: MsBox.Avalonia.Enums.Icon.Warning).ShowWindowDialogAsync(this);
                node.Name = "PLEASE RENAME";
                _editingOriginalName = null;
                return;
            }

            // Check if name was actually changed
            if (_editingOriginalName != null && _editingOriginalName != node.Name)
            {
                RecordRename(node, _editingOriginalName, node.Name);

                // Sort parent's children to reflect new alphabetical order
                node.Parent?.SortChildren();
            }
            _editingOriginalName = null;
        }

        private void EditTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is TextBox textBox && textBox.DataContext is FolderTreeNode node))
                return;

            if (e.Key == Key.Enter)
            {
                CommitRename(node);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Revert changes
                var originalName = node.OriginalFullPath.Split('\\').Last();
                node.Name = originalName;
                CommitRename(node);
                e.Handled = true;
            }
        }

        private void EditTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is FolderTreeNode node && node.IsEditing)
                CommitRename(node);
        }

        // Drag and drop

        private void Tree_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            var tree = this.FindControl<TreeView>("FolderTreeView");

            if (!e.GetCurrentPoint(tree).Properties.IsLeftButtonPressed)
            {
                _clickedNode = null;
                _dragTriggerEvent = null;
                return;
            }

            _dragStartPoint = e.GetPosition(this);

            var node = (e.Source as Control)?.DataContext as FolderTreeNode;

            // Don't treat clicks inside an active rename editor as a drag start
            if (node != null && node.IsEditing)
                node = null;

            _clickedNode = node;
            _dragTriggerEvent = node != null ? e : null;
        }

        private async void Tree_PointerMoved(object sender, PointerEventArgs e)
        {
            if (_clickedNode == null || _draggedNode != null || _dragTriggerEvent == null)
                return;

            if (_clickedNode.IsRootNode)
                return;

            var tree = this.FindControl<TreeView>("FolderTreeView");
            if (!e.GetCurrentPoint(tree).Properties.IsLeftButtonPressed)
                return;

            var currentPosition = e.GetPosition(this);
            if (Math.Abs(currentPosition.X - _dragStartPoint.X) < 4 &&
                Math.Abs(currentPosition.Y - _dragStartPoint.Y) < 4)
                return;

            _draggedNode = _clickedNode;

            // the drag takes over the DataTransfer from here, so we leave it alone and let
            // Avalonia dispose it when the drag ends
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(NodeFormat, _draggedNode));
            data.Add(DataTransferItem.Create(NodeMarkerFormat, new byte[] { 1 }));

            try
            {
                await DragDrop.DoDragDropAsync(_dragTriggerEvent, data, DragDropEffects.Move);
            }
            catch (Exception)
            {
                // A failed platform drag just cancels the move
            }

            _draggedNode = null;
            _clickedNode = null;
            _dragTriggerEvent = null;
            ClearDropTarget();
        }

        private void Tree_DragOver(object sender, DragEventArgs e)
        {
            e.DragEffects = _draggedNode != null ? DragDropEffects.Move : DragDropEffects.None;

            var targetNode = (e.Source as Control)?.DataContext as FolderTreeNode;

            if (targetNode != _currentDropTarget)
            {
                // Clear previous highlight
                if (_currentDropTarget != null)
                    _currentDropTarget.IsDropTarget = false;

                // Set new highlight
                _currentDropTarget = targetNode;
                if (_currentDropTarget != null)
                    _currentDropTarget.IsDropTarget = true;
            }

            e.Handled = true;
        }

        private void Tree_DragLeave(object sender, RoutedEventArgs e)
        {
            ClearDropTarget();
        }

        private async void Tree_Drop(object sender, DragEventArgs e)
        {
            try
            {
                var droppedNode = _draggedNode;
                var targetNode = (e.Source as Control)?.DataContext as FolderTreeNode;

                if (droppedNode == null || targetNode == null || droppedNode == targetNode)
                    return;

                // Prevent moving the root node
                if (droppedNode.IsRootNode)
                    return;

                // Prevent dropping node onto itself or its own descendants
                if (IsDescendant(targetNode, droppedNode))
                {
                    await MessageBoxManager.GetMessageBoxStandard("Information",
                        "Cannot move a folder into its own subfolder.",
                        icon: MsBox.Avalonia.Enums.Icon.Warning).ShowWindowDialogAsync(this);
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

                _draggedNode = null;
            }
            finally
            {
                ClearDropTarget();
            }
        }

        private void ClearDropTarget()
        {
            if (_currentDropTarget != null)
            {
                _currentDropTarget.IsDropTarget = false;
                _currentDropTarget = null;
            }
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

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            FolderMappings = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var root in RootNodes)
            {
                CollectMappings(root, FolderMappings);
            }

            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
