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

        // 图标缓存
        private Image? _icoFile, _icoSuccess, _icoError, _icoRunning, _icoWaiting;
        // 在类字段区域添加（任意位置与其它常量一起）
        private const int ImportIconToTextGap = 13;   // 图标与“拖拽视频文件或导入”之间的垂直间距
        private const int TextToButtonSafeGap = 8;    // 文本底与按钮顶的安全间距（避免被按钮遮挡）
        // 颜色常量
        private static readonly Color ColorSuccess = Color.FromArgb(40, 167, 69);     // 绿色
        private static readonly Color ColorError   = Color.FromArgb(239, 68, 68);     // #EF4444
        private static readonly Color ColorInfo    = Color.FromArgb(14, 165, 233);    // #0EA5E9
        private static readonly Color ColorMuted   = Color.FromArgb(148, 163, 184);   // Slate-400
        private static readonly Color ColorDim     = Color.FromArgb(203, 213, 225);   // Slate-300
        private static readonly Color ColorRowBorder = Color.FromArgb(220, 220, 220);
        private static readonly Color ColorProgress  = Color.FromArgb(34, 197, 94);   // 绿色进度
        private static readonly Color ColorProgressBack = Color.FromArgb(229, 231, 235); // 灰底

        // 允许的扩展名
        private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".avi", ".mkv", ".mov", ".flv", ".wmv" };

        // 任务与行 UI
        private readonly List<FileJob> _jobs = new();

        private enum AppStage { ImportIdle, ImportHover, ListReady, Transcoding, AllDone }
        private AppStage _stage = AppStage.ImportIdle;
        private volatile bool _stopRequested = false;

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
                Width = 120,
                Height = 32,
                Text = "继续导入",
                Visible = false
            };
            btnStart = new UIButton
            {
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
                Width = 320,
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
                Top = 10,
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
            _stage = AppStage.ImportIdle;
            // 首次进入导入模式居中布局
            CenterImportLayout();
        }

        private void CenterImportLayout()
        {
            if (!_importMode || !_uiReady) return;
            if (dropPanel is null || btnImportCentered is null || dropPanel.IsDisposed || btnImportCentered.IsDisposed) return;

            // 居中放置拖拽区域（防小窗口收缩）
            var desiredW = 320;
            var desiredH = 444;
            dropPanel.Width = Math.Min(desiredW, ClientSize.Width);
            dropPanel.Height = Math.Min(desiredH, ClientSize.Height);

            dropPanel.Left = Math.Max(0, (ClientSize.Width - dropPanel.Width) / 2);
            dropPanel.Top  = Math.Max(0, (ClientSize.Height - dropPanel.Height) / 2);

            // 虚线矩形（相对 dropPanel 客户区）
            const int dashedPadding = 12;
            int dashedLeft  = dashedPadding;
            int dashedRight = dropPanel.Width - dashedPadding;
            int dashedWidth = dashedRight - dashedLeft;

            // 根据图标与文本间距，计算按钮的最小 Top，保证：图标 -> 文本(16px高) -> 安全间距 -> 按钮
            int minButtonTop = ImportIconTop + ImportIconH + (int)ImportTextHeight + TextToButtonSafeGap + ImportIconToTextGap;

            // 既满足设计的 248，又至少不遮挡文本
            int desiredTop  = Math.Max(248, minButtonTop);
            int desiredLeft = dashedLeft + (dashedWidth - btnImportCentered.Width) / 2;

            // 边界保护
            desiredTop  = Math.Max(dashedPadding, Math.Min(desiredTop, dropPanel.Height - dashedPadding - btnImportCentered.Height));
            desiredLeft = Math.Max(dashedLeft, Math.Min(desiredLeft,  dashedRight - btnImportCentered.Width));

            btnImportCentered.Top  = desiredTop;
            btnImportCentered.Left = desiredLeft;
        }

        private void EnterNormalModeLayout()
        {
            // 切换到列表布局但窗口宽高保持不变
            _importMode = false;
            _dropHover = false;
            dropPanel.Visible = false;

            // 布局：底部两个按钮，列表填充其上方
            const int margin = 12;
            btnStart.Visible = true;
            btnImportMore.Visible = true;

            // 按钮在底部两侧
            btnStart.Left = margin;
            btnStart.Top = ClientSize.Height - margin - btnStart.Height;
            btnImportMore.Left = ClientSize.Width - margin - btnImportMore.Width;
            btnImportMore.Top = btnStart.Top;

            // 列表填充按钮上方
            listPanel.Visible = true;
            listPanel.Left = margin;
            listPanel.Top = margin;
            listPanel.Width = ClientSize.Width - margin * 2;
            listPanel.Height = btnStart.Top - margin - listPanel.Top;

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

        // 导入图标尺寸与位置（相对 dropPanel）
        private const int ImportIconTop = 164;
        private const int ImportIconW = 56;
        private const int ImportIconH = 56;
        // 文案高度
        private const float ImportTextHeight = 18f;

        private void DropPanel_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var isHover = _stage == AppStage.ImportHover;
            dropPanel.FillColor = isHover ? Color.FromArgb(240, 249, 255) : Color.White;
            var borderColor = isHover ? Color.FromArgb(14, 165, 233) : Color.FromArgb(150, 150, 150);

            var dashedRect = new Rectangle(12, 12, dropPanel.Width - 24, dropPanel.Height - 24);
            using (var pen = new Pen(borderColor, 2f) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(pen, dashedRect);

            if (_importMode)
            {
                EnsureImportImagesLoaded();

                // 图标：用统一常量
                int iconLeft = dashedRect.Left + (dashedRect.Width - ImportIconW) / 2;
                var iconRect = new Rectangle(iconLeft, ImportIconTop, ImportIconW, ImportIconH);
                var iconImg = isHover ? _imgImportHover : _imgImportNormal;
                if (iconImg != null)
                    g.DrawImage(iconImg, iconRect);

                // 文案：只按 ImportIconToTextGap 计算，不再夹紧到按钮
                var text = "拖拽视频文件或导入";
                using var font = new Font("OPlusSans 3.0", 12f, FontStyle.Regular, GraphicsUnit.Point);
                using var brush = new SolidBrush(Color.FromArgb(100, 116, 139));

                float textTop = iconRect.Bottom + ImportIconToTextGap;
                // 边界保护：不超出虚线框
                textTop = Math.Max(dashedRect.Top, Math.Min(textTop, dashedRect.Bottom - ImportTextHeight));

                var textRect = new RectangleF(dashedRect.Left, textTop, dashedRect.Width, ImportTextHeight);
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
                if (_importMode) _stage = AppStage.ImportHover; // 状态=悬停
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
                    if (_importMode) _stage = AppStage.ImportHover;
                    dropPanel.Invalidate();
                }
            }
        }

        private void DropPanel_DragLeave(object? sender, EventArgs e)
        {
            _dropHover = false;
            if (_importMode) _stage = AppStage.ImportIdle; // 回到空态
            dropPanel.Invalidate();
        }

        private void DropPanel_DragDrop(object? sender, DragEventArgs e)
        {
            _dropHover = false;
            if (_importMode) _stage = AppStage.ImportIdle; // 先回空态，AddFiles 后进入列表态
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
                    EnterNormalModeLayout();      // 切换到列表布局，窗口不变
                    _stage = AppStage.ListReady;  // 状态=列表空态
                }
                else
                {
                    LayoutRows();
                }
                btnStart.Enabled = true;
            }
        }
        private void EnsureRowIconsLoaded()
        {
            if (_icoFile != null) return;
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                _icoFile = Image.FromFile(Path.Combine(dir, "image", "ico_file.png"));
                _icoSuccess = Image.FromFile(Path.Combine(dir, "image", "ico_success.png"));
                _icoError = Image.FromFile(Path.Combine(dir, "image", "ico_error.png"));
                _icoRunning = Image.FromFile(Path.Combine(dir, "image", "ico_running.png"));
                _icoWaiting = Image.FromFile(Path.Combine(dir, "image", "ico_waiting.png"));
            }
            catch
            {
                // 缺失时容错：不显示图标即可
            }
        }

        private FileJob CreateJob(string inputPath)
        {
            EnsureRowIconsLoaded();

            var dir = Path.GetDirectoryName(inputPath) ?? "";
            var name = Path.GetFileNameWithoutExtension(inputPath);
            var outputPath = Path.Combine(dir, $"{name}_1080p.mp4");

            var row = new UIPanel
            {
                Width = listPanel.ClientSize.Width - 24,
                Height = 64,
                Radius = 4,
                FillColor = Color.White,
                RectColor = ColorRowBorder
            };

            // 左侧文件图标
            var picFile = new PictureBox
            {
                Left = 10,
                Top = 8,
                Width = 16,
                Height = 16,
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image = _icoFile
            };

            var lblName = new UILabel
            {
                Left = picFile.Right + 8,
                Top = 6,
                Width = row.Width - 220,
                Height = 22,
                Text = $"{Path.GetFileName(inputPath)}",
                Font = new Font("微软雅黑", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 63, 65)
            };

            var bar = new UIProcessBar
            {
                Left = lblName.Left,
                Top = 34,
                Width = row.Width - 220,
                Height = 12,
                Maximum = 100,
                Value = 0,
                Visible = false,
                StyleCustomMode = true,
                FillColor = ColorProgressBack,
                ForeColor = ColorProgress,
                RectColor = Color.Transparent
            };

            var lblPercent = new UILabel
            {
                Left = bar.Right + 10,
                Top = bar.Top - 2,
                Width = 60,
                Height = 20,
                Text = "0%",
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 63, 65),
                Visible = false
            };

            // 右侧状态图标
            var picStatus = new PictureBox
            {
                Width = 16,
                Height = 16,
                Top = lblName.Top + 2,
                SizeMode = PictureBoxSizeMode.StretchImage
            };

            var lblStatus = new UILabel
            {
                Top = lblName.Top,
                Width = 80,
                Height = 20,
                Text = "等待中",
                Font = new Font("微软雅黑", 9.5F, FontStyle.Regular),
                ForeColor = ColorMuted,
                Cursor = Cursors.Default
            };

            row.Controls.Add(picFile);
            row.Controls.Add(lblName);
            row.Controls.Add(bar);
            row.Controls.Add(lblPercent);
            row.Controls.Add(picStatus);
            row.Controls.Add(lblStatus);

            var job = new FileJob
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                Row = row,
                LeftIcon = picFile,
                NameLabel = lblName,
                Bar = bar,
                PercentLabel = lblPercent,
                StatusIcon = picStatus,
                StatusLabel = lblStatus
            };

            // 查看原因点击
            lblStatus.Click += (_, __) =>
            {
                if (!string.IsNullOrEmpty(job.ErrorMessage) && lblStatus.Text == "查看原因")
                    UIMessageBox.Show(job.ErrorMessage, "失败原因");
            };

            // 初始为等待中样式
            ApplyStatusStyle(job, "等待中");

            return job;
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

            if (_importMode)
            {
                CenterImportLayout();
                return;
            }

            // 常规模式：按钮底部，两侧布局；列表填充其上方
            if (listPanel is null || listPanel.IsDisposed) return;
            const int margin = 12;

            btnStart.Left = margin;
            btnStart.Top = ClientSize.Height - margin - btnStart.Height;

            btnImportMore.Left = ClientSize.Width - margin - btnImportMore.Width;
            btnImportMore.Top = btnStart.Top;

            listPanel.Left = margin;
            listPanel.Top = margin;
            listPanel.Width = ClientSize.Width - margin * 2;
            listPanel.Height = btnStart.Top - margin - listPanel.Top;

            LayoutRows();
        }

        private void LayoutRows()
        {
            if (listPanel is null || listPanel.IsDisposed) return;
            var y = 8 - listPanel.AutoScrollPosition.Y;
            foreach (var job in _jobs)
            {
                job.Row.Left = 8;
                job.Row.Top = y;
                job.Row.Width = listPanel.ClientSize.Width - 24;

                // 宽度联动
                job.NameLabel.Width = job.Row.Width - 220;
                job.Bar.Left = job.NameLabel.Left;
                job.Bar.Width = job.Row.Width - 220;

                // 右侧状态区域靠右
                int rightPadding = 12;
                job.StatusLabel.Left = job.Row.Width - rightPadding - job.StatusLabel.Width;
                job.StatusIcon.Left = job.StatusLabel.Left - 4 - job.StatusIcon.Width;

                // 百分比紧跟进度条
                job.PercentLabel.Left = job.Bar.Right + 10;

                y += job.Row.Height + 8;
            }
        }

        private async void BtnStart_Click(object? sender, EventArgs e)
        {
            // 若正在转码，点击视为停止后续任务（当前文件转完后生效）
            if (_stage == AppStage.Transcoding)
            {
                _stopRequested = true;
                btnStart.Enabled = false;
                btnStart.Text = "停止中...";
                return;
            }

            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                UIMessageBox.Show("未找到 ffmpeg.exe，请放到程序目录。", "提示");
                return;
            }

            // 进入“转码中”
            _stopRequested = false;
            _stage = AppStage.Transcoding;
            btnStart.Text = "停止";
            btnStart.Enabled = true;
            btnImportMore.Enabled = false;

            try
            {
                foreach (var job in _jobs)
                {
                    if (_stopRequested) break;
                    await TranscodeOne(job);
                }

                if (_stopRequested)
                {
                    _stage = AppStage.ListReady; // 停止后回到列表可继续状态
                }
                else
                {
                    var allDone = _jobs.All(j => j.StatusLabel.Text == "成功");
                    _stage = allDone ? AppStage.AllDone : AppStage.ListReady;
                    if (allDone) UIMessageBox.Show("全部文件转码完成！", "提示");
                }
            }
            catch (Exception ex)
            {
                _stage = AppStage.ListReady;
                UIMessageBox.Show($"转码过程中出现错误：{ex.Message}", "错误");
            }
            finally
            {
                btnStart.Text = "开始转码";
                btnStart.Enabled = _jobs.Any(j => j.StatusLabel.Text is "等待" or "失败");
                btnImportMore.Enabled = true;
                dropPanel.Invalidate();
            }
        }

        private async Task TranscodeOne(FileJob job)
        {
            if (job.StatusLabel.Text is "转码成功") return;

            SafeUI(() => ApplyStatusStyle(job, "转码中"));
            UpdateRow(job, progress: 0);

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
            }, TimeSpan.FromMilliseconds(300));

            try
            {
                await conversion.ProcessAsynchronously();
                SafeUI(() => UpdateRow(job, progress: 100, status: "转码成功", success: true));
            }
            catch (Exception ex)
            {
                job.ErrorMessage = ex.ToString();
                SafeUI(() => ApplyStatusStyle(job, "查看原因"));
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
                ApplyStatusStyle(job, status);
            }
            if (success)
            {
                job.Row.RectColor = Color.FromArgb(190, 230, 200);
            }
        }

        private void SafeUI(Action action)
        {
            if (IsDisposed) return;
            if (IsHandleCreated)
                BeginInvoke(action);
        }

        private void ApplyStatusStyle(FileJob job, string state)
        {
            // 默认
            job.Row.RectColor = ColorRowBorder;
            job.NameLabel.ForeColor = Color.FromArgb(60, 63, 65);

            switch (state)
            {
                case "转码中":
                    job.StatusLabel.Text = "转码中";
                    job.StatusLabel.ForeColor = Color.FromArgb(139, 92, 246); // 紫色
                    job.StatusLabel.Cursor = Cursors.Default;
                    job.StatusIcon.Image = _icoRunning;

                    job.Bar.Visible = true;
                    job.PercentLabel.Visible = true;
                    break;

                case "转码成功":
                case "成功":
                    job.StatusLabel.Text = "转码成功";
                    job.StatusLabel.ForeColor = ColorSuccess;
                    job.StatusLabel.Cursor = Cursors.Default;
                    job.StatusIcon.Image = _icoSuccess;

                    job.Bar.Visible = false;
                    job.PercentLabel.Visible = false;
                    job.Row.RectColor = Color.FromArgb(190, 230, 200);
                    break;

                case "查看原因":
                case "失败":
                    job.StatusLabel.Text = "查看原因";
                    job.StatusLabel.ForeColor = ColorInfo;
                    job.StatusLabel.Cursor = Cursors.Hand;
                    job.StatusIcon.Image = _icoError;

                    job.Bar.Visible = false;
                    job.PercentLabel.Visible = false;
                    job.NameLabel.ForeColor = ColorError;
                    break;

                case "等待中":
                case "等待":
                default:
                    job.StatusLabel.Text = "等待中";
                    job.StatusLabel.ForeColor = ColorMuted;
                    job.StatusLabel.Cursor = Cursors.Default;
                    job.StatusIcon.Image = _icoWaiting;

                    job.Bar.Visible = false;
                    job.PercentLabel.Visible = false;
                    break;
            }
        }

        // 文件任务与其 UI
        private sealed class FileJob
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
}
