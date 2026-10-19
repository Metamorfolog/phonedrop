namespace PhoneDrop;

/// <summary>Soubor k odeslání; Dir je podsložka v telefonu (Stažené/Drop/Dir), když přišel jako součást složky.</summary>
public sealed record DropItem(string Path, string? Dir)
{
    // Systémové smetí, které ve složkách nikdo nechce mít v telefonu.
    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase) { "Thumbs.db", "desktop.ini", ".DS_Store" };

    /// <summary>Soubory projde, složky rozbalí do jednotlivých souborů se zachovanou strukturou.</summary>
    public static List<DropItem> Expand(IEnumerable<string> paths)
    {
        var items = new List<DropItem>();
        foreach (var p in paths)
        {
            if (File.Exists(p))
            {
                items.Add(new DropItem(System.IO.Path.GetFullPath(p), null));
            }
            else if (Directory.Exists(p))
            {
                var root = System.IO.Path.GetFullPath(p).TrimEnd('\\', '/');
                var parent = System.IO.Path.GetDirectoryName(root) ?? root;
                var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System | FileAttributes.Hidden | FileAttributes.ReparsePoint,
                });
                foreach (var f in files.Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (Skip.Contains(System.IO.Path.GetFileName(f))) continue;
                    var dir = System.IO.Path.GetRelativePath(parent, System.IO.Path.GetDirectoryName(f)!).Replace('\\', '/');
                    items.Add(new DropItem(f, dir));
                }
            }
        }
        return items;
    }
}
