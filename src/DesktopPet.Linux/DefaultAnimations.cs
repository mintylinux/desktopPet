using System.IO;
using System.Reflection;

namespace DesktopPet.Linux
{
    /// <summary>
    /// Provides the default (eSheep) animations.xml, embedded as a resource in this assembly.
    /// Replaces the original WinForms project's Properties.Resources.animations.
    /// </summary>
    public static class DefaultAnimations
    {
        private static string? _cached;

        public static string Xml
        {
            get
            {
                if (_cached != null) return _cached;
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("animations.xml")
                    ?? throw new FileNotFoundException("Embedded resource 'animations.xml' not found.");
                using var reader = new StreamReader(stream);
                _cached = reader.ReadToEnd();
                return _cached;
            }
        }
    }
}
