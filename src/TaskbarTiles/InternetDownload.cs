using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TaskbarTiles
{
    // .NET Framework path validation rejects filename:Zone.Identifier in File.WriteAllText.
    // Open the named stream through Win32, keeping the normal Internet security marker.
    static class InternetDownload
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        static string Marker(string source)
        {
            Uri uri;
            if (!Uri.TryCreate(source, UriKind.Absolute, out uri) || !ReleaseInfo.AllowedDownloadUri(uri))
                throw new InvalidDataException("Untrusted Internet-download source.");
            return "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + uri.AbsoluteUri + "\r\n";
        }
        static FileStream OpenStream(string file, bool write)
        {
            string path = Path.GetFullPath(file);
            if (!File.Exists(path)) throw new FileNotFoundException("The verified installer no longer exists.");
            SafeFileHandle handle = CreateFile(path + ":Zone.Identifier", write ? 0x40000000u : 0x80000000u,
                7, IntPtr.Zero, write ? 2u : 3u, 0x80, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error(); handle.Dispose();
                throw new IOException("Could not retain the installer's Internet security marker. Use Browser download instead.", new Win32Exception(error));
            }
            try { return new FileStream(handle, write ? FileAccess.Write : FileAccess.Read); }
            catch { handle.Dispose(); throw; }
        }
        internal static void Mark(string file, string source)
        {
            byte[] content = Encoding.UTF8.GetBytes(Marker(source));
            using (var stream = OpenStream(file, true)) { stream.Write(content, 0, content.Length); stream.Flush(); }
            if (!HasMark(file, source)) throw new IOException("Internet-download marker verification failed. Nothing will be run.");
        }
        internal static bool HasMark(string file, string source)
        {
            string expected = Marker(source);
            using (var stream = OpenStream(file, false))
            {
                if (stream.Length > 8192) return false;
                using (var reader = new StreamReader(stream, Encoding.UTF8, true)) return reader.ReadToEnd() == expected;
            }
        }
    }
}
