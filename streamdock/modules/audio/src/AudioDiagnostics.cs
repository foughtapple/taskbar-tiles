using System;
using System.IO;
namespace TaskbarTilesAudio {
static class AudioDiagnostics {
static readonly object Gate=new object();
internal static void Write(string value) { try {lock(Gate) { var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FoughtApple","TaskbarTilesStreamDock");Directory.CreateDirectory(root);var file=Path.Combine(root,"audio-diagnostic.log");if(File.Exists(file)&&new FileInfo(file).Length>65536){if(File.Exists(file+".1"))File.Delete(file+".1");File.Move(file,file+".1");}File.AppendAllText(file,DateTime.UtcNow.ToString("o")+" "+value+Environment.NewLine);}}catch{} }
}
}
