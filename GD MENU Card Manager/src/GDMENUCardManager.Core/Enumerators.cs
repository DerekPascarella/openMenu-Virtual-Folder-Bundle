namespace GDMENUCardManager.Core
{
    /// <summary>
    /// What Save Changes will do with this item's folder.
    /// </summary>
    public enum WorkMode
    {
        None,
        New,
        Move
    }

    /// <summary>
    /// How the item is stored on disk, which decides the conversion path on save.
    /// </summary>
    public enum FileFormat
    {
        Uncompressed,
        SevenZip,
        RedumpCueBin,
        CueBinNonGame,
        Chd
    }

    public enum SpecialDisc
    {
        None,
        CodeBreaker,
        BleemGame
    }

    public enum RenameBy
    {
        Ip,
        Folder,
        File,
    }

    public enum MenuKind // Folder name must match the enum name, case sensitive.
    {
        None,
        gdMenu,
        openMenu
    }
}