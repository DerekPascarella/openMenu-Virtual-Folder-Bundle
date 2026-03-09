using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public class FolderTreeNode : INotifyPropertyChanged
    {
        private string _Name;
        public string Name
        {
            get => _Name;
            set
            {
                var sanitized = Helper.StripNonPrintableAscii(value);
                if (_Name != sanitized)
                {
                    _Name = sanitized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                    UpdateFullPath();
                }
            }
        }

        private string _FullPath;
        public string FullPath
        {
            get => _FullPath;
            set
            {
                if (_FullPath != value)
                {
                    _FullPath = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _DirectGameCount;
        public int DirectGameCount
        {
            get => _DirectGameCount;
            set
            {
                if (_DirectGameCount != value)
                {
                    _DirectGameCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        private int _TotalGameCount;
        public int TotalGameCount
        {
            get => _TotalGameCount;
            set
            {
                if (_TotalGameCount != value)
                {
                    _TotalGameCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string DisplayName
        {
            get
            {
                return Name;
            }
        }

        public FolderTreeNode Parent { get; set; }

        private bool _IsExpanded = true;
        public bool IsExpanded
        {
            get => _IsExpanded;
            set
            {
                if (_IsExpanded != value)
                {
                    _IsExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _IsEditing;
        public bool IsEditing
        {
            get => _IsEditing;
            set
            {
                if (_IsEditing != value)
                {
                    _IsEditing = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _IsDropTarget;
        public bool IsDropTarget
        {
            get => _IsDropTarget;
            set
            {
                if (_IsDropTarget != value)
                {
                    _IsDropTarget = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<FolderTreeNode> Children { get; } = new ObservableCollection<FolderTreeNode>();

        public string OriginalFullPath { get; set; }

        public bool IsRootNode { get; set; }

        public void UpdateFullPath()
        {
            if (IsRootNode)
            {
                FullPath = "";
            }
            else if (Parent == null || Parent.IsRootNode)
            {
                FullPath = Name;
            }
            else
            {
                FullPath = string.IsNullOrEmpty(Parent.FullPath) ? Name : $"{Parent.FullPath}\\{Name}";
            }

            // Cascade to children
            foreach (var child in Children)
            {
                child.UpdateFullPath();
            }
        }

        public void RecalculateCounts()
        {
            // Don't recalculate root - it's set manually to the total item count
            if (IsRootNode)
                return;

            // Calculate total from children
            TotalGameCount = DirectGameCount;
            foreach (var child in Children)
            {
                child.RecalculateCounts();
                TotalGameCount += child.TotalGameCount;
            }
        }

        public void SortChildren()
        {
            // Sort children alphanumerically by name
            var sortedChildren = Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Children.Clear();
            foreach (var child in sortedChildren)
            {
                Children.Add(child);
                child.SortChildren(); // Recursively sort all descendants
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
