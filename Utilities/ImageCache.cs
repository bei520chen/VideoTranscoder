using System;
using System.Drawing;
using System.IO;

namespace VideoConverter
{
    internal sealed class ImageCache
    {
        private ImageCache() { }
        public static ImageCache Instance { get; } = new ImageCache();

        // 导入界面图
        public Image? ImportNormal { get; private set; }
        public Image? ImportHover { get; private set; }

        // 行状态图标
        public Image? IcoFile { get; private set; }
        public Image? IcoSuccess { get; private set; }
        public Image? IcoError { get; private set; }
        public Image? IcoRunning { get; private set; }
        public Image? IcoWaiting { get; private set; }

        public void EnsureImportImagesLoaded()
        {
            if (ImportNormal != null && ImportHover != null) return;
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                ImportNormal = Image.FromFile(Path.Combine(baseDir, "image", "img_drag_import.png"));
                ImportHover = Image.FromFile(Path.Combine(baseDir, "image", "img_drag_import_activated.png"));
            }
            catch
            {
                // 忽略加载失败
            }
        }

        public void EnsureRowIconsLoaded()
        {
            if (IcoFile != null) return;
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                IcoFile = Image.FromFile(Path.Combine(dir, "image", "ico_file.png"));
                IcoSuccess = Image.FromFile(Path.Combine(dir, "image", "ico_success.png"));
                IcoError = Image.FromFile(Path.Combine(dir, "image", "ico_error.png"));
                IcoRunning = Image.FromFile(Path.Combine(dir, "image", "ico_running.png"));
                IcoWaiting = Image.FromFile(Path.Combine(dir, "image", "ico_waiting.png"));
            }
            catch
            {
                // 容错
            }
        }
    }
}