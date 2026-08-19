namespace GDMENUCardManager.Core
{
    public static class ArchiveMetadataEditPolicy
    {
        public static bool CanEdit(
            GdItem item,
            ArchiveMetadataField field,
            MenuKind menuKind)
        {
            if (item == null || !item.CanEditParsedArchiveMetadata)
                return false;
            if (item.Ip?.Name == "GDMENU" || item.Ip?.Name == "openMenu")
                return false;

            return field switch
            {
                ArchiveMetadataField.Serial => true,
                ArchiveMetadataField.Type => true,
                ArchiveMetadataField.Disc => menuKind == MenuKind.openMenu,
                ArchiveMetadataField.Region => item.DiscType == "Game",
                _ => false
            };
        }
    }
}
