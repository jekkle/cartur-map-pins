using System.IO;

namespace CarturMapPins
{
    /// Saves a file whole without File.Replace.
    ///
    /// File.Replace is Win32 ReplaceFile, which makes a randomly named "<file>~RFxxxx.TMP" beside
    /// the target and deletes it again within the call. Under Mod Organizer's virtual file system
    /// that file lands in the overwrite folder and vanishes while MO2 is listing it, so MO2 reports
    /// "error reported listing files ... pins.txt~RF271b96fa.TMP: c000000f" (Duk3nat0r, Nexus,
    /// 2026-10-22). Writing a fixed-name .tmp and renaming it uses only plain create, delete and
    /// move, with no file that appears and disappears inside one call.
    internal static class SafeFile
    {
        public static void Write(string path, string[] lines)
        {
            string temp = path + ".tmp";
            File.WriteAllLines(temp, lines);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        /// A crash between the delete and the move leaves only the .tmp, and it is the newer copy.
        public static void Recover(string path)
        {
            string temp = path + ".tmp";
            if (!File.Exists(path) && File.Exists(temp))
                File.Move(temp, path);
        }
    }
}
