using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Manages undo/redo operations for the application.
    /// Supports up to 10 levels of undo/redo history.
    /// </summary>
    public class UndoManager : INotifyPropertyChanged
    {
        private const int MaxHistorySize = 10;

        // Using LinkedList for efficient add/remove from both ends
        // Last item in list = most recent operation (top of stack)
        private readonly LinkedList<UndoOperation> _undoStack = new LinkedList<UndoOperation>();
        private readonly LinkedList<UndoOperation> _redoStack = new LinkedList<UndoOperation>();

        public event PropertyChangedEventHandler PropertyChanged;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;

        public string UndoDescription => _undoStack.Count > 0 ? _undoStack.Last.Value.Description : "";
        public string RedoDescription => _redoStack.Count > 0 ? _redoStack.Last.Value.Description : "";

        /// <summary>
        /// Records a new change that can be undone.
        /// Clears any pending redo operations (new changes invalidate redo history).
        /// Trims history to MaxHistorySize if needed.
        /// </summary>
        public void RecordChange(UndoOperation operation)
        {
            if (operation == null) return;

            // Push to undo stack
            _undoStack.AddLast(operation);

            // Trim if exceeds max size (remove oldest from front)
            while (_undoStack.Count > MaxHistorySize)
            {
                _undoStack.RemoveFirst();
            }

            // Clear redo stack - new changes invalidate redo history
            _redoStack.Clear();

            RaiseAllPropertyChanges();
        }

        /// <summary>
        /// Undoes the most recent change.
        /// </summary>
        public void Undo()
        {
            if (_undoStack.Count == 0) return;

            // Pop from undo stack
            var operation = _undoStack.Last.Value;
            _undoStack.RemoveLast();

            // Execute undo
            operation.Undo();

            // Push to redo stack
            _redoStack.AddLast(operation);

            // Trim redo stack if needed
            while (_redoStack.Count > MaxHistorySize)
            {
                _redoStack.RemoveFirst();
            }

            RaiseAllPropertyChanges();
        }

        /// <summary>
        /// Redoes the most recently undone change.
        /// </summary>
        public void Redo()
        {
            if (_redoStack.Count == 0) return;

            // Pop from redo stack
            var operation = _redoStack.Last.Value;
            _redoStack.RemoveLast();

            // Execute redo
            operation.Redo();

            // Push to undo stack
            _undoStack.AddLast(operation);

            // Trim undo stack if needed (unlikely but for consistency)
            while (_undoStack.Count > MaxHistorySize)
            {
                _undoStack.RemoveFirst();
            }

            RaiseAllPropertyChanges();
        }

        /// <summary>
        /// Clears all undo/redo history.
        /// Call this when loading a new SD card.
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();

            RaiseAllPropertyChanges();
        }

        private void RaiseAllPropertyChanges()
        {
            RaisePropertyChanged(nameof(CanUndo));
            RaisePropertyChanged(nameof(CanRedo));
            RaisePropertyChanged(nameof(UndoCount));
            RaisePropertyChanged(nameof(RedoCount));
            RaisePropertyChanged(nameof(UndoDescription));
            RaisePropertyChanged(nameof(RedoDescription));
        }

        private void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Base class for all undoable operations.
    /// </summary>
    public abstract class UndoOperation
    {
        /// <summary>
        /// A human-readable description of what this operation does (for potential UI display).
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Reverses the operation.
        /// </summary>
        public abstract void Undo();

        /// <summary>
        /// Re-applies the operation after it was undone.
        /// </summary>
        public abstract void Redo();
    }

    /// <summary>
    /// Represents an undoable property edit on a GdItem.
    /// </summary>
    public class PropertyEditOperation : UndoOperation
    {
        public GdItem Item { get; set; }
        public string PropertyName { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }

        public override string Description => $"Edit {PropertyName}";

        public override void Undo()
        {
            SetPropertyValue(OldValue);
        }

        public override void Redo()
        {
            SetPropertyValue(NewValue);
        }

        private void SetPropertyValue(object value)
        {
            switch (PropertyName)
            {
                case nameof(GdItem.Name):
                    Item.Name = value as string;
                    break;
                case nameof(GdItem.ProductNumber):
                    Item.ProductNumber = value as string;
                    break;
                case nameof(GdItem.Folder):
                    Item.Folder = value as string;
                    break;
                case nameof(GdItem.DiscType):
                    Item.DiscType = value as string;
                    break;
                case nameof(GdItem.Disc):
                    Item.Disc = value as string;
                    break;
            }
        }
    }

    /// <summary>
    /// Represents an undoable artwork change (add, update, or delete).
    /// </summary>
    public class ArtworkChangeOperation : UndoOperation
    {
        public string Serial { get; set; }
        public byte[] OldBoxPvrData { get; set; }
        public byte[] NewBoxPvrData { get; set; }
        public byte[] OldIconPvrData { get; set; }
        public byte[] NewIconPvrData { get; set; }
        public BoxDatManager BoxDat { get; set; }
        public IconDatManager IconDat { get; set; }

        /// <summary>
        /// Action to refresh artwork status on affected items after undo/redo.
        /// </summary>
        public Action<string> RefreshArtworkStatus { get; set; }

        public override string Description
        {
            get
            {
                if (OldBoxPvrData == null && NewBoxPvrData != null)
                    return "Add Artwork";
                if (OldBoxPvrData != null && NewBoxPvrData == null)
                    return "Delete Artwork";
                return "Change Artwork";
            }
        }

        public override void Undo()
        {
            ApplyArtwork(OldBoxPvrData, OldIconPvrData);
        }

        public override void Redo()
        {
            ApplyArtwork(NewBoxPvrData, NewIconPvrData);
        }

        private void ApplyArtwork(byte[] boxData, byte[] iconData)
        {
            if (boxData == null)
            {
                // Delete artwork
                BoxDat?.DeleteEntryForSerial(Serial);
                IconDat?.DeleteEntryForSerial(Serial);
            }
            else
            {
                // Set artwork
                BoxDat?.SetArtworkForSerial(Serial, boxData);
                if (iconData != null)
                {
                    IconDat?.SetIconForSerial(Serial, iconData);
                }
            }

            RefreshArtworkStatus?.Invoke(Serial);
        }
    }

    /// <summary>
    /// Represents an undoable list reorder operation (sort or drag-drop).
    /// </summary>
    public class ListReorderOperation : UndoOperation
    {
        public ObservableCollection<GdItem> ItemList { get; set; }
        public List<GdItem> OldOrder { get; set; }
        public List<GdItem> NewOrder { get; set; }
        private string _description;

        public ListReorderOperation(string description = "Reorder List")
        {
            _description = description;
        }

        public override string Description => _description;

        public override void Undo()
        {
            ApplyOrder(OldOrder);
        }

        public override void Redo()
        {
            ApplyOrder(NewOrder);
        }

        private void ApplyOrder(List<GdItem> order)
        {
            if (ItemList == null || order == null) return;

            ItemList.Clear();
            foreach (var item in order)
            {
                ItemList.Add(item);
            }
        }
    }

    /// <summary>
    /// Represents an undoable item addition.
    /// </summary>
    public class ItemAddOperation : UndoOperation
    {
        public ObservableCollection<GdItem> ItemList { get; set; }
        public GdItem Item { get; set; }
        public int Index { get; set; }

        public override string Description => "Add Item";

        public override void Undo()
        {
            if (ItemList == null || Item == null) return;
            ItemList.Remove(Item);
        }

        public override void Redo()
        {
            if (ItemList == null || Item == null) return;

            if (Index >= 0 && Index <= ItemList.Count)
                ItemList.Insert(Index, Item);
            else
                ItemList.Add(Item);
        }
    }

    /// <summary>
    /// Represents an undoable item removal.
    /// </summary>
    public class ItemRemoveOperation : UndoOperation
    {
        public ObservableCollection<GdItem> ItemList { get; set; }
        public GdItem Item { get; set; }
        public int Index { get; set; }

        public override string Description => "Remove Item";

        public override void Undo()
        {
            if (ItemList == null || Item == null) return;

            if (Index >= 0 && Index <= ItemList.Count)
                ItemList.Insert(Index, Item);
            else
                ItemList.Add(Item);
        }

        public override void Redo()
        {
            if (ItemList == null || Item == null) return;
            ItemList.Remove(Item);
        }
    }

    /// <summary>
    /// Represents multiple items being added at once (e.g., drag-drop multiple files).
    /// </summary>
    public class MultiItemAddOperation : UndoOperation
    {
        public ObservableCollection<GdItem> ItemList { get; set; }
        public List<(GdItem Item, int Index)> Items { get; set; } = new List<(GdItem, int)>();

        public override string Description => Items.Count == 1 ? "Add Item" : $"Add {Items.Count} Items";

        public override void Undo()
        {
            if (ItemList == null) return;

            // Remove in reverse order to maintain correct indices
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                ItemList.Remove(Items[i].Item);
            }
        }

        public override void Redo()
        {
            if (ItemList == null) return;

            foreach (var (item, index) in Items)
            {
                if (index >= 0 && index <= ItemList.Count)
                    ItemList.Insert(index, item);
                else
                    ItemList.Add(item);
            }
        }
    }

    /// <summary>
    /// Represents multiple items being removed at once.
    /// </summary>
    public class MultiItemRemoveOperation : UndoOperation
    {
        public ObservableCollection<GdItem> ItemList { get; set; }
        public List<(GdItem Item, int Index)> Items { get; set; } = new List<(GdItem, int)>();

        public override string Description => Items.Count == 1 ? "Remove Item" : $"Remove {Items.Count} Items";

        public override void Undo()
        {
            if (ItemList == null) return;

            // Re-insert in order by index
            var sorted = new List<(GdItem Item, int Index)>(Items);
            sorted.Sort((a, b) => a.Index.CompareTo(b.Index));

            foreach (var (item, index) in sorted)
            {
                if (index >= 0 && index <= ItemList.Count)
                    ItemList.Insert(index, item);
                else
                    ItemList.Add(item);
            }
        }

        public override void Redo()
        {
            if (ItemList == null) return;

            // Remove in reverse index order to maintain correct indices
            var sorted = new List<(GdItem Item, int Index)>(Items);
            sorted.Sort((a, b) => b.Index.CompareTo(a.Index));

            foreach (var (item, _) in sorted)
            {
                ItemList.Remove(item);
            }
        }
    }

    /// <summary>
    /// Represents multiple property edits on different items (e.g., bulk rename, bulk folder assignment).
    /// </summary>
    public class MultiPropertyEditOperation : UndoOperation
    {
        public string PropertyName { get; set; }
        public List<(GdItem Item, object OldValue, object NewValue)> Edits { get; set; } = new List<(GdItem, object, object)>();
        private string _customDescription;

        public MultiPropertyEditOperation(string description = null)
        {
            _customDescription = description;
        }

        public override string Description => _customDescription ?? (Edits.Count == 1 ? $"Edit {PropertyName}" : $"Edit {Edits.Count} Items");

        public override void Undo()
        {
            foreach (var (item, oldValue, _) in Edits)
            {
                SetPropertyValue(item, oldValue);
            }
        }

        public override void Redo()
        {
            foreach (var (item, _, newValue) in Edits)
            {
                SetPropertyValue(item, newValue);
            }
        }

        private void SetPropertyValue(GdItem item, object value)
        {
            switch (PropertyName)
            {
                case nameof(GdItem.Name):
                    item.Name = value as string;
                    break;
                case nameof(GdItem.ProductNumber):
                    item.ProductNumber = value as string;
                    break;
                case nameof(GdItem.Folder):
                    item.Folder = value as string;
                    break;
                case nameof(GdItem.DiscType):
                    item.DiscType = value as string;
                    break;
                case nameof(GdItem.Disc):
                    item.Disc = value as string;
                    break;
            }
        }
    }

    public class FilterApplyOperation : UndoOperation
    {
        public string FilterText { get; set; }
        public Action<string> ApplyFilter { get; set; }
        public Action ClearFilter { get; set; }

        public override string Description => $"Filter: {FilterText}";

        public override void Undo()
        {
            ClearFilter?.Invoke();
        }

        public override void Redo()
        {
            ApplyFilter?.Invoke(FilterText);
        }
    }

    public class AltFoldersChangeOperation : UndoOperation
    {
        public GdItem Item { get; set; }
        public List<string> OldAltFolders { get; set; }
        public List<string> NewAltFolders { get; set; }

        public override string Description => "Assign Additional Folder Paths";

        public override void Undo()
        {
            Item.AlternativeFolders = new List<string>(OldAltFolders);
        }

        public override void Redo()
        {
            Item.AlternativeFolders = new List<string>(NewAltFolders);
        }
    }

    public class BatchFolderRenameOperation : UndoOperation
    {
        public class ItemSnapshot
        {
            public GdItem Item { get; set; }
            public string OldFolder { get; set; }
            public string NewFolder { get; set; }
            public List<string> OldAltFolders { get; set; }
            public List<string> NewAltFolders { get; set; }
        }

        public List<ItemSnapshot> Snapshots { get; set; } = new List<ItemSnapshot>();

        public override string Description => "Batch Folder Rename";

        public override void Undo()
        {
            foreach (var s in Snapshots)
            {
                s.Item.Folder = s.OldFolder;
                s.Item.AlternativeFolders = new List<string>(s.OldAltFolders);
            }
        }

        public override void Redo()
        {
            foreach (var s in Snapshots)
            {
                s.Item.Folder = s.NewFolder;
                s.Item.AlternativeFolders = new List<string>(s.NewAltFolders);
            }
        }
    }
}
