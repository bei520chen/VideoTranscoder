using System;
using System.IO;

namespace VideoConverter
{
    internal static class BatchOutput
    {
        public static string CreateBatchOutputDir()
        {
            var root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output", DateTime.Now.ToString("yyyyMMdd"));
            var guid = Guid.NewGuid().ToString("N").Substring(0, 8);
            var dir = Path.Combine(root, guid);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}