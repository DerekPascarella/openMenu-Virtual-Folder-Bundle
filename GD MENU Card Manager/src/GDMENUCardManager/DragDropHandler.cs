using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GDMENUCardManager.Core;
using GongSolutions.Wpf.DragDrop;
using GongSolutions.Wpf.DragDrop.Utilities;

namespace GDMENUCardManager
{
    internal class DropResult
    {
        public bool IsReorder { get; set; }
        public bool IsAdd { get; set; }
        public List<GdItem> OldOrder { get; set; }
        public List<GdItem> NewOrder { get; set; }
        public List<(GdItem Item, int Index)> AddedItems { get; set; } = new List<(GdItem, int)>();
        public List<string> UnsupportedRedumpGdi { get; } = new List<string>();
    }

    internal static class DragDropHandler
    {
        public static void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.DragInfo == null)
            {
                if (dropInfo.Data is DataObject data && data.ContainsFileDropList())
                {
                    // Don't allow dropping external files at position 0 (menu item slot)
                    if (dropInfo.UnfilteredInsertIndex == 0 &&
                        dropInfo.TargetCollection is ObservableCollection<GdItem> targetList &&
                        targetList.Count > 0 && targetList[0].IsMenuItem)
                    {
                        dropInfo.Effects = DragDropEffects.None;
                    }
                    else
                    {
                        dropInfo.Effects = DragDropEffects.Copy;
                    }
                }
            }
            else if (DefaultDropHandler.CanAcceptData(dropInfo))
            {
                // Check if the dragged item is a menu item
                var draggedItems = DefaultDropHandler.ExtractData(dropInfo.Data).OfType<GdItem>().ToList();
                bool hasMenuItem = draggedItems.Any(item => item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu");

                if (hasMenuItem)
                {
                    // Don't allow dragging menu items
                    dropInfo.Effects = DragDropEffects.None;
                }
                else if (dropInfo.UnfilteredInsertIndex == 0)
                {
                    // Don't allow dropping items at position 0 (would push menu down)
                    dropInfo.Effects = DragDropEffects.None;
                }
                else
                {
                    dropInfo.Effects = DragDropEffects.Move;
                }
            }

            if (dropInfo.Effects != DragDropEffects.None)
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
        }

        public static async Task<DropResult> Drop(IDropInfo dropInfo, Manager manager, Func<int, IProgress<string>> addProgressFactory = null)
        {
            var invalid = new List<string>();
            var result = new DropResult();

            var insertIndex = dropInfo.UnfilteredInsertIndex;
            var destinationList = dropInfo.TargetCollection.TryGetList();

            if (dropInfo.DragInfo == null)
            {
                // External file drop, so adding new items.
                if (!(dropInfo.Data is DataObject data) || !data.ContainsFileDropList())
                    return null;

                result.IsAdd = true;

                var droppedFiles = data.GetFileDropList().Cast<string>().ToList();
                var companions = await ImageHelper.GetCompanionFilePathsAsync(droppedFiles);
                var addPaths = droppedFiles
                    .Where(path => !companions.Contains(path))
                    .ToArray();
                var added = await manager.AddGames(
                    addPaths,
                    insertIndex,
                    AddGamesUndoProfile.WpfExternalDrop,
                    addProgressFactory?.Invoke(addPaths.Length));
                result.AddedItems.AddRange(added.AddedItems);
                result.UnsupportedRedumpGdi.AddRange(added.UnsupportedRedumpGdi);
                invalid.AddRange(added.InvalidDetails.Select(failure =>
                    $"{failure.Path} - {failure.Message}"));
            }
            else
            {
                // Internal reorder
                result.IsReorder = true;

                // Capture old order before reorder
                if (destinationList is ObservableCollection<GdItem> gdItemList)
                {
                    result.OldOrder = new List<GdItem>(gdItemList);
                }

                var data = DefaultDropHandler.ExtractData(dropInfo.Data).OfType<object>().ToList();

                var sourceList = dropInfo.DragInfo.SourceCollection.TryGetList();
                if (sourceList != null)
                {
                    foreach (var o in data)
                    {
                        var index = sourceList.IndexOf(o);
                        if (index != -1)
                        {
                            sourceList.RemoveAt(index);
                            if (destinationList != null && Equals(sourceList, destinationList) && index < insertIndex)
                                --insertIndex;
                        }
                    }
                }

                if (destinationList != null)
                    foreach (var o in data)
                        destinationList.Insert(insertIndex++, o);

                // Capture new order after reorder
                if (destinationList is ObservableCollection<GdItem> gdItemListAfter)
                {
                    result.NewOrder = new List<GdItem>(gdItemListAfter);
                }
            }

            if (invalid.Any())
            {
                // The result gets discarded when this throws, so fold any rejected
                // redump GDIs into the same list instead of losing them
                foreach (var name in result.UnsupportedRedumpGdi)
                    invalid.Add($"{name} - {LegacyRedumpGdiDetector.ShortMessage}");

                throw new InvalidDropException(string.Join(Environment.NewLine + Environment.NewLine, invalid));
            }

            return result;
        }
    }

    internal class InvalidDropException : Exception
    {
        public InvalidDropException(string message) : base(message)
        {
        }
    }
}
