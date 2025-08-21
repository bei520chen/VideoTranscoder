using Sunny.UI;
using System.Windows.Forms;

namespace VideoConverter
{
    internal sealed class FileJob
    {
        public string InputPath = null!;
        public string OutputPath = null!;
        public UIPanel Row = null!;
        public PictureBox LeftIcon = null!;
        public UILabel NameLabel = null!;
        public UIProcessBar Bar = null!;
        public UILabel PercentLabel = null!;
        public PictureBox StatusIcon = null!;
        public UILabel StatusLabel = null!;
        public string? ErrorMessage;
    }
}