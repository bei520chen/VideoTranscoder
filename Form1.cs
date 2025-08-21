using FFMpegCore;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VideoConverter
{
    public partial class MainForm : UIForm
    {
        public MainForm()
        {
            InitializeComponent();
            ConfigureFFmpeg();
        }

        // 顶部控件
        private UIButton btnImportMore = null!;
        private UIButton btnStart = null!;
        private OpenFileDialog openFileDialog = null!;
        private SaveFileDialog saveFileDialog = null!;

        // 拖拽区域 + 列表区域
        private UIPanel dropPanel = null!;
        private UIPanel listPanel = null!;

        // 导入模式控件/状态
        private UIButton btnImportCentered = null!;
        private bool _dropHover = false;    
        private bool _importMode = true;

        // 初始化完成标志，防止构造期 OnResize 访问未初始化控件
        private bool _uiReady = false;

        // 导入图标图片缓存
        private Image? _imgImportNormal;
        private Image? _imgImportHover;

        // 允许的扩展名
        private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".avi", ".mkv", ".mov", ".flv", ".wmv" };

        // 任务与行 UI
        private readonly List<FileJob> _jobs = new();

        private void ConfigureFFmpeg()
        {
            var bin = AppDomain.CurrentDomain.BaseDirectory;
            GlobalFFOptions.Configure(new FFOptions
            {
                BinaryFolder = bin,
                TemporaryFilesFolder = Path.GetTempPath()
            });
        }

        private void InitializeComponent()
        {
            Text = "素材转码助手";
            Width = 320;     // 导入模式窗口宽
            Height = 500;    // 导入模式窗口高
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            // 默认背景色：白色
            BackColor = Color.FromArgb(255, 255, 255);

            // 顶部按钮（导入模式下先隐藏）
            btnImportMore = new UIButton
            {
                Left = 40,
                Top = 28,
                Width = 120,
                Height = 32,
                Text = "继续导入",
                Visible = false
            };
            btnStart = new UIButton
            {
                Left = btnImportMore.Right + 16,
                Top = 28,
                Width = 120,
                Height = 32,
                Text = "开始转码",
                Enabled = false,
                Visible = false
            };
            btnImportMore.Click += BtnImportMore_Click;
            btnStart.Click += BtnStart_Click;

            openFileDialog = new OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.mkv;*.mov;*.flv;*.wmv",
                Multiselect = true
            };
            saveFileDialog = new SaveFileDialog { Filter = "MP4文件|*.mp4" };

            // 拖拽区域（导入模式：320x444，居中）
            dropPanel = new UIPanel
            {
                Top = 12,
                Width = 320, // 由 296 改为 320，以满足左右 120px 边距（虚线宽 296）
                Height = 444,
                Radius = 6,
                FillColor = Color.White,
                RectColor = Color.FromArgb(180, 180, 180),
                AllowDrop = true
            };
            dropPanel.DragEnter += DropPanel_DragEnter;
            dropPanel.DragOver += DropPanel_DragOver;
            dropPanel.DragLeave += DropPanel_DragLeave;
            dropPanel.DragDrop += DropPanel_DragDrop;
            dropPanel.Paint += DropPanel_Paint;

            // 中间的“导入文件”按钮（#0284C7）
            btnImportCentered = new UIButton
            {
                Text = "导入文件",
                Width = 76,
                Height = 32,
                Radius = 4,
                Top=10,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                FillColor = Color.FromArgb(2, 132, 199),
                FillHoverColor = Color.FromArgb(14, 165, 233),
                FillPressColor = Color.FromArgb(2, 132, 199),
                RectColor = Color.Transparent
            };
            btnImportCentered.Click += (_, __) =>
            {
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                    AddFiles(openFileDialog.FileNames);
            };
            dropPanel.Controls.Add(btnImportCentered);

            // 文件列表区域（导入模式下先隐藏）
            listPanel = new UIPanel
            {
                Left = 40,
                Top = dropPanel.Bottom + 16,
                Width = 630,
                Height = 320,
                Radius = 6,
                FillColor = Color.White,
                RectColor = Color.FromArgb(220, 220, 220),
                AutoScroll = true,
                Visible = false
            };

            Controls.Add(btnImportMore);
            Controls.Add(btnStart);
            Controls.Add(dropPanel);
            Controls.Add(listPanel);

            _uiReady = true;

            // 首次进入导入模式居中布局
            CenterImportLayout();
        }

        private void CenterImportLayout()
        {
            if (!_importMode || !_uiReady) return;
            if (dropPanel is null || btnImportCentered is null || dropPanel.IsDisposed || btnImportCentered.IsDisposed) return;

            // 居中放置拖拽区域
            var x = (ClientSize.Width - dropPanel.Width) / 2;
            var y = (ClientSize.Height - dropPanel.Height) / 2;
            dropPanel.Left = Math.Max(0, x);
            dropPanel.Top = Math.Max(0, y);

            // 虚线矩形（相对 dropPanel 客户区）
            const int dashedPadding = 12;
            int dashedLeft = dashedPadding;
            int dashedRight = dropPanel.Width - dashedPadding;
            int dashedWidth = dashedRight - dashedLeft;

            // 导入文件按钮：水平居中；顶部 248px
            int desiredTop = 248;
            int desiredLeft = dashedLeft + (dashedWidth - btnImportCentered.Width) / 2;

            // 边界保护
            desiredTop = Math.Max(dashedPadding, Math.Min(desiredTop, dropPanel.Height - dashedPadding - btnImportCentered.Height));
            desiredLeft = Math.Max(dashedLeft, Math.Min(desiredLeft, dashedRight - btnImportCentered.Width));

            btnImportCentered.Top = desiredTop;
            btnImportCentered.Left = desiredLeft;
        }

        private void EnterNormalModeLayout()
        {
            // 恢复到常规布局（原有 720x520 结构）
            _importMode = false;
            _dropHover = false;
            dropPanel.FillColor = Color.White;

            Width = 720;
            Height = 520;

            btnImportMore.Visible = true;
            btnStart.Visible = true;

            // 顶部按钮位置
            btnImportMore.Left = 40;
            btnImportMore.Top = 28;
            btnStart.Left = btnImportMore.Right + 16;
            btnStart.Top = 28;

            // 顶部提示拖拽区域（100 高）
            dropPanel.Width = ClientSize.Width - 80;
            dropPanel.Height = 100;
            dropPanel.Left = 40;
            dropPanel.Top = 80;

            // 列表区域靠下填充
            listPanel.Visible = true;
            listPanel.Left = 40;
            listPanel.Top = dropPanel.Bottom + 16;
            listPanel.Width = ClientSize.Width - 80;
            listPanel.Height = ClientSize.Height - listPanel.Top - 40;

            // 导入模式按钮隐藏
            btnImportCentered.Visible = false;

            dropPanel.Invalidate();
            LayoutRows();
        }

        private void EnsureImportImagesLoaded()
        {
            if (_imgImportNormal != null && _imgImportHover != null) return;
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _imgImportNormal = Image.FromFile(Path.Combine(baseDir, "image", "img_drag_import.png"));
                _imgImportHover = Image.FromFile(Path.Combine(baseDir, "image", "img_drag_import_activated.png"));
            }
            catch
            {
                // 忽略加载失败，绘制时做空值判断
            }
        }

        private void DropPanel_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Hover 填充色
            dropPanel.FillColor = _dropHover ? Color.FromArgb(240, 249, 255) : Color.White; // #F0F9FF

            // 虚线矩形：距边 12px
            var borderColor = _dropHover ? Color.FromArgb(14, 165, 233) : Color.FromArgb(150, 150, 150);
            var dashedRect = new Rectangle(12, 12, dropPanel.Width - 24, dropPanel.Height - 24);
            using (var pen = new Pen(borderColor, 2f) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(pen, dashedRect);

            if (_importMode)
            {
                // 图片：水平居中
                EnsureImportImagesLoaded();
                const int iconW = 56, iconH = 56;
                int iconTop = 164;
                int iconLeft = dashedRect.Left + (dashedRect.Width - iconW) / 2;
                var iconRect = new Rectangle(iconLeft, iconTop, iconW, iconH);
                var iconImg = _dropHover ? _imgImportHover : _imgImportNormal;
                if (iconImg != null)
                    g.DrawImage(iconImg, iconRect);

                // 文案：左右居中，避免被按钮遮挡
                var text = "拖拽视频文件或导入";
                using var font = new Font("OPlusSans 3.0", 12f, FontStyle.Regular, GraphicsUnit.Point);
                using var brush = new SolidBrush(Color.FromArgb(100, 116, 139));

                float textHeight = 16f;
                int gapBelowText = 8; // 文本底部与按钮顶部的安全间距
                float defaultTop = iconRect.Bottom + 12;
                // 按钮是子控件，会覆盖文本，确保文本底部 < 按钮顶部 - gap
                float maxTop = btnImportCentered.Top - gapBelowText - textHeight;
                float textTop = Math.Min(defaultTop, maxTop);
                // 防御：若空间不足，仍不低于虚线顶
                textTop = Math.Max(dashedRect.Top, textTop);

                var textRect = new RectangleF(dashedRect.Left, textTop, dashedRect.Width, textHeight);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, font, brush, textRect, sf);
            }
        }

        private void DropPanel_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                _dropHover = true;
                dropPanel.Invalidate();
            }
        }

        private void DropPanel_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                if (!_dropHover)
                {
                    _dropHover = true;
                    dropPanel.Invalidate();
                }
            }
        }

        private void DropPanel_DragLeave(object? sender, EventArgs e)
        {
            _dropHover = false;
            dropPanel.Invalidate();
        }

        private void DropPanel_DragDrop(object? sender, DragEventArgs e)
        {
            _dropHover = false;
            dropPanel.Invalidate();

            if (e.Data == null) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
                AddFiles(files);
        }

        private void BtnImportMore_Click(object? sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
                AddFiles(openFileDialog.FileNames);
        }

        private void AddFiles(IEnumerable<string> files)
        {
            var existing = new HashSet<string>(_jobs.Select(j => j.InputPath), StringComparer.OrdinalIgnoreCase);
            var addedAny = false;

            foreach (var file in files)
            {
                if (!File.Exists(file)) continue;
                var ext = Path.GetExtension(file);
                if (!AllowedExt.Contains(ext)) continue;
                if (existing.Contains(file)) continue;

                var job = CreateJob(file);
                _jobs.Add(job);
                AddRow(job);
                addedAny = true;
            }

            if (addedAny)
            {
                if (_importMode)
                {
                    // 第一次添加文件后退出导入模式，展示列表界面
                    EnterNormalModeLayout();
                }
                else
                {
                    LayoutRows();
                }
                btnStart.Enabled = true;
            }
        }

        private FileJob CreateJob(string inputPath)
        {
            var dir = Path.GetDirectoryName(inputPath) ?? "";
            var name = Path.GetFileNameWithoutExtension(inputPath);
            var outputPath = Path.Combine(dir, $"{name}_1080p.mp4");

            // 行容器
            var row = new UIPanel
            {
                Width = listPanel.ClientSize.Width - 24,
                Height = 64,
                Radius = 4,
                FillColor = Color.White,
                RectColor = Color.FromArgb(230, 230, 230)
            };

            // 文件名
            var lblName = new UILabel
            {
                Left = 10,
                Top = 8,
                Width = row.Width - 220,
                Height = 20,
                Text = $"{Path.GetFileName(inputPath)}",
                Font = new Font("微软雅黑", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 63, 65)
            };

            // 进度条
            var bar = new UIProcessBar
            {
                Left = 10,
                Top = 32,
                Width = row.Width - 220,
                Height = 18,
                Maximum = 100,
                Value = 0
            };

            // 百分比
            var lblPercent = new UILabel
            {
                Left = bar.Right + 10,
                Top = bar.Top - 2,
                Width = 60,
                Height = 20,
                Text = "0%",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 63, 65)
            };

            // 状态
            var lblStatus = new UILabel
            {
                Left = lblPercent.Right + 8,
                Top = bar.Top - 2,
                Width = 80,
                Height = 20,
                Text = "等待",
                Font = new Font("微软雅黑", 10F, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 100, 100)
            };

            row.Controls.Add(lblName);
            row.Controls.Add(bar);
            row.Controls.Add(lblPercent);
            row.Controls.Add(lblStatus);

            return new FileJob
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                Row = row,
                NameLabel = lblName,
                Bar = bar,
                PercentLabel = lblPercent,
                StatusLabel = lblStatus
            };
        }

        private void AddRow(FileJob job)
        {
            listPanel.Controls.Add(job.Row);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            // 构造期/未就绪时忽略布局，避免空引用
            if (!_uiReady) return;

            // 导入模式：确保拖拽区域与按钮保持居中
            if (_importMode)
            {
                CenterImportLayout();
                return;
            }

            // 常规模式：更新列表区域和拖拽区域宽度
            if (listPanel is null || listPanel.IsDisposed) return; // 防御
            var margin = 40;
            if (dropPanel is not null && !dropPanel.IsDisposed)
            {
                dropPanel.Left = margin;
                dropPanel.Top = 80;
                dropPanel.Width = ClientSize.Width - margin * 2;
                dropPanel.Height = 100;
            }

            listPanel.Left = margin;
            listPanel.Top = dropPanel.Bottom + 16;
            listPanel.Width = ClientSize.Width - margin * 2;
            listPanel.Height = ClientSize.Height - listPanel.Top - margin;

            LayoutRows();
        }

        private void LayoutRows()
        {
            if (listPanel is null || listPanel.IsDisposed) return; // 防御未初始化
            var y = 8 - listPanel.AutoScrollPosition.Y;
            foreach (var job in _jobs)
            {
                job.Row.Left = 8;
                job.Row.Top = y;
                job.Row.Width = listPanel.ClientSize.Width - 24;
                // 同步内部控件宽度
                job.NameLabel.Width = job.Row.Width - 220;
                job.Bar.Width = job.Row.Width - 220;
                job.PercentLabel.Left = job.Bar.Right + 10;
                job.StatusLabel.Left = job.PercentLabel.Right + 8;

                y += job.Row.Height + 8;
            }
        }

        private async void BtnStart_Click(object? sender, EventArgs e)
        {
            // 校验 ffmpeg.exe
            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                UIMessageBox.Show("未找到 ffmpeg.exe，请放到程序目录。", "提示");
                return;
            }

            btnStart.Enabled = false;
            btnImportMore.Enabled = false;

            try
            {
                // 顺序逐个转码（如需并行，可改为 Task.WhenAll 并限制并发度）
                foreach (var job in _jobs)
                {
                    await TranscodeOne(job);
                }

                UIMessageBox.Show("全部文件转码完成！", "提示");
            }
            catch (Exception ex)
            {
                UIMessageBox.Show($"转码过程中出现错误：{ex.Message}", "错误");
            }
            finally
            {
                btnStart.Enabled = _jobs.Any(j => j.StatusLabel.Text is "等待" or "失败");
                btnImportMore.Enabled = true;
            }
        }

        private async Task TranscodeOne(FileJob job)
        {
            // 已成功的跳过
            if (job.StatusLabel.Text == "成功") return;

            UpdateRow(job, status: "转码中", progress: 0);

            var vf = "scale='if(lt(iw,ih),1080,-2)':'if(lt(iw,ih),-2,1080)',format=yuv420p";

            var conversion = FFMpegArguments
                .FromFileInput(job.InputPath)
                .OutputToFile(job.OutputPath, true, opt => opt
                    .WithVideoCodec("libx264")
                    .WithAudioCodec("aac")
                    .WithAudioBitrate(128_000)
                    .WithCustomArgument($"-vf \"{vf}\"")
                    .WithFramerate(30));

            conversion.NotifyOnProgress(percent =>
            {
                var p = (int)Math.Clamp(percent, 0, 100);
                SafeUI(() => UpdateRow(job, progress: p));
            }, TimeSpan.FromMilliseconds(500));

            try
            {
                await conversion.ProcessAsynchronously();
                SafeUI(() => UpdateRow(job, progress: 100, status: "成功", success: true));
            }
            catch (Exception ex)
            {
                SafeUI(() => UpdateRow(job, status: "失败"));
                // 记录日志或提示（不打断批处理）
                Console.WriteLine($"转码失败: {job.InputPath} => {ex}");
            }
        }

        private void UpdateRow(FileJob job, int? progress = null, string? status = null, bool success = false)
        {
            if (IsDisposed) return;

            if (progress.HasValue)
            {
                job.Bar.Value = progress.Value;
                job.PercentLabel.Text = $"{progress.Value}%";
            }
            if (status != null)
            {
                job.StatusLabel.Text = status;
                job.StatusLabel.ForeColor = status == "成功"
                    ? Color.FromArgb(40, 167, 69)
                    : (status == "失败" ? Color.FromArgb(200, 50, 50) : Color.FromArgb(100, 100, 100));
            }
            if (success)
            {
                // 可选：行高亮或完成样式
                job.Row.RectColor = Color.FromArgb(190, 230, 200);
            }
        }

        private void SafeUI(Action action)
        {
            if (IsDisposed) return;
            if (IsHandleCreated)
                BeginInvoke(action);
        }

        // 文件任务与其 UI
        private sealed class FileJob
        {
            public string InputPath = null!;
            public string OutputPath = null!;
            public UIPanel Row = null!;
            public UILabel NameLabel = null!;
            public UIProcessBar Bar = null!;
            public UILabel PercentLabel = null!;
            public UILabel StatusLabel = null!;
        }
    }
}
