using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DesktopPet.Linux
{
    public sealed record PetInfo(string Id, string DisplayName, string XmlResourceName, string? IconResourceName);

    /// <summary>
    /// Enumerates the bundled alternate pet characters/colors (embedded from the repo's Pets/
    /// folder, e.g. blue_sheep, green_sheep, neko, pikachu, ...), so the user can spawn the
    /// default pet or switch to a different one, similar to the original app's "Screen Mates".
    /// </summary>
    public static class PetCatalog
    {
        private static List<PetInfo>? _cache;

        public static IReadOnlyList<PetInfo> AvailablePets
        {
            get
            {
                if (_cache != null) return _cache;

                var asm = Assembly.GetExecutingAssembly();
                var names = asm.GetManifestResourceNames();

                var xmlByFolder = new Dictionary<string, string>();
                var iconByFolder = new Dictionary<string, string>();

                foreach (var name in names)
                {
                    // Expect names shaped like "pets.<folder>...animations.xml" / "...icon.png"
                    if (!name.StartsWith("pets.", StringComparison.Ordinal)) continue;

                    if (name.EndsWith("animations.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        string folder = ExtractFolder(name, "animations.xml");
                        xmlByFolder[folder] = name;
                    }
                    else if (name.EndsWith("icon.png", StringComparison.OrdinalIgnoreCase))
                    {
                        string folder = ExtractFolder(name, "icon.png");
                        iconByFolder[folder] = name;
                    }
                }

                var list = new List<PetInfo>();
                foreach (var (folder, xmlRes) in xmlByFolder.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                {
                    iconByFolder.TryGetValue(folder, out var iconRes);
                    list.Add(new PetInfo(folder, ToDisplayName(folder), xmlRes, iconRes));
                }

                _cache = list;
                return _cache;
            }
        }

        private static string ExtractFolder(string resourceName, string suffix)
        {
            // Strip leading "pets." and trailing "<separator>animations.xml"/"icon.png",
            // tolerating either '.' or '/'/'\' as the separator MSBuild produced.
            string trimmed = resourceName.Substring("pets.".Length);
            trimmed = trimmed.Substring(0, trimmed.Length - suffix.Length);
            return trimmed.TrimEnd('.', '/', '\\');
        }

        private static string ToDisplayName(string folderId)
        {
            var words = folderId.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

        public static string LoadXml(PetInfo pet)
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(pet.XmlResourceName)
                ?? throw new FileNotFoundException(pet.XmlResourceName);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
