using Sunny.UI;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System;
using System.Collections.Generic;

namespace VideoConverter
{
    public partial class MainForm : UIForm  
    {
        public MainForm()
        {
            InitializeComponent();
            _transcode.ConfigureFFmpeg();
        }

        // 统一进度条配色
        private static readonly Color ProgressBarForeColor = Color.FromArgb(16, 185, 129);   // 前景（进度颜色）
        private static readonly Color ProgressBarFillColor = Color.FromArgb(229, 231, 235);  // 背景

        // 顶部控件（表头内）
        private UIPanel headerPanel = null!;
        private UIButton btnImportMore = null!;
        private UIButton btnStart = null!;
        private OpenFileDialog openFileDialog = null!;

        // 拖拽区域 + 列表区域
        private UIPanel dropPanel = null!;
        private UIPanel contentPanel = null!;
        private UIPanel listPanel = null!;
        private UIPanel bottomPanel = null!;
        private UIButton btnOpenFolder = null!;
        private UIButton btnUpload = null!;
        private ContextMenuStrip uploadMenu = new();

        // 导入模式控件/状态
        private UIButton btnImportCentered = null!;
        private bool _dropHover = false;
        private bool _importMode = true;

        // 初始化完成标志
        private bool _uiReady = false;

        // 服务与缓存
        private readonly TranscodeService _transcode = new();
        private readonly UploadTargetsProvider _uploadProvider = new();
        private readonly ImageCache _images = ImageCache.Instance;

        // 允许的扩展名
        private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".avi", ".mkv", ".mov", ".flv", ".wmv" };

        // 行与任务
        private readonly List<FileJob> _jobs = new();

        // 并行与取消
        private int _maxParallel = 2;
        private CancellationTokenSource? _cts;

        // 批次输出目录
        private string? _currentBatchDir;

        private enum AppStage { ImportIdle, ImportHover, ListReady, Transcoding, AllDone }
        private AppStage _stage = AppStage.ImportIdle;

        private void InitializeComponent()
        {
            Text = "素材转码助手";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(350, 500); // 固定客户区
            MinimumSize = new Size(320 + (Width - ClientSize.Width), 500 + (Height - ClientSize.Height));
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            BackColor = Color.White;

            openFileDialog = new OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.mkv;*.mov;*.flv;*.wmv",
                Multiselect = true
            };

            headerPanel = new UIPanel
            {
                Left = 0,
                Top = 0,
                Width = ClientSize.Width,
                Height = 32,
                FillColor = Color.White,
                RectColor = Color.Transparent
            };
            headerPanel.Visible = false;

            btnImportMore = new UIButton
            {
                Left = 8,
                Top = 4,
                Width = 90,
                Height = 24,
                Text = "继续导入",
                Radius = 4
            };
            btnImportMore.Click += BtnImportMore_Click;

            btnStart = new UIButton
            {
                Width = 90,
                Height = 24,
                Radius = 4,
                Text = "开始转码",
                FillColor = Color.FromArgb(2, 132, 199),
                FillHoverColor = ControlPaint.Light(Color.FromArgb(2, 132, 199)),
                FillPressColor = Color.FromArgb(2, 132, 199),
                ForeColor = Color.White,
                Top = 4
            };
            btnStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnStart.Click += BtnStart_Click;

            headerPanel.Controls.Add(btnImportMore);
            headerPanel.Controls.Add(btnStart);

            dropPanel = new UIPanel
            {
                Left = 0,
                Top = 32,
                Width = ClientSize.Width,
                Height = 468,
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

            btnImportCentered = new UIButton
            {
                Text = "导入文件",
                Width = 76,
                Height = 32,
                Radius = 4,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                FillColor = Color.FromArgb(2, 132, 199),
                FillHoverColor = ControlPaint.Light(Color.FromArgb(2, 132, 199)),
                FillPressColor = Color.FromArgb(2, 132, 199),
                RectColor = Color.Transparent
            };
            btnImportCentered.Click += (_, __) =>
            {
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                    AddFiles(openFileDialog.FileNames);
            };
            dropPanel.Controls.Add(btnImportCentered);

            contentPanel = new UIPanel
            {
                Left = 0,
                Top = 32,
                Width = ClientSize.Width,
                Height = 468,
                FillColor = Color.White,
                RectColor = Color.Transparent,
                Visible = false
            };

            bottomPanel = new UIPanel
            {
                Left = 0,
                Width = contentPanel.Width,
                Height = 64,
                Top = 248,
                FillColor = Color.White,
                RectColor = Color.Transparent,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };

            btnOpenFolder = new UIButton
            {
                Left = 8,
                Top = 12,
                Width = 120,
                Height = 40,
                Text = "打开文件夹",
                Radius = 6,
                FillColor = Color.White,
                FillHoverColor = Color.White,
                FillPressColor = Color.White,
                RectColor = Color.FromArgb(203, 213, 225),
                RectHoverColor = Color.FromArgb(203, 213, 225),
                ForeColor = Color.FromArgb(51, 65, 85),
                ForeHoverColor = Color.FromArgb(51, 65, 85),
                ForePressColor = Color.FromArgb(51, 65, 85)
            };
            btnOpenFolder.Click += (_, __) =>
            {
                if (!string.IsNullOrEmpty(_currentBatchDir) && Directory.Exists(_currentBatchDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _currentBatchDir,
                        UseShellExecute = true
                    });
                }
                else
                {
                    UIMessageBox.Show("当前批次输出目录不存在。", "提示");
                }
            };

            btnUpload = new UIButton
            {
                Width = 120,
                Height = 40,
                Text = "上传至...",
                Radius = 6,
                FillColor = Color.FromArgb(2, 132, 199),
                FillHoverColor = ControlPaint.Light(Color.FromArgb(2, 132, 199)),
                FillPressColor = Color.FromArgb(2, 132, 199),
                ForeColor = Color.White
            };
            btnUpload.Left = bottomPanel.Width - btnUpload.Width - 8;
            btnUpload.Top = 12;
            btnUpload.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            btnUpload.MouseEnter += async (_, __) => await ShowUploadMenuAsync();
            btnUpload.Click += async (_, __) => await ShowUploadMenuAsync();

            bottomPanel.Controls.Add(btnOpenFolder);
            bottomPanel.Controls.Add(btnUpload);

            listPanel = new UIPanel
            {
                Left = 0,
                Top = 0,
                Width = contentPanel.Width,
                Height = 248,
                Radius = 6,
                FillColor = Color.White,
                RectColor = Color.FromArgb(220, 220, 220),
                AutoScroll = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };

            contentPanel.Controls.Add(listPanel);
            contentPanel.Controls.Add(bottomPanel);

            Controls.Add(headerPanel);
            Controls.Add(dropPanel);
            Controls.Add(contentPanel);

            _uiReady = true;
            _stage = AppStage.ImportIdle;

            LayoutContent();
            CenterImportLayout();
        }

        private void LayoutContent()
        {
            headerPanel.Width = ClientSize.Width;
            headerPanel.Height = 32;

            dropPanel.Left = 0;
            dropPanel.Top = 32;
            dropPanel.Width = ClientSize.Width;
            dropPanel.Height = 468;

            contentPanel.Left = 0;
            contentPanel.Top = 32;
            contentPanel.Width = ClientSize.Width;
            contentPanel.Height = 468;

            listPanel.Left = 0;
            listPanel.Top = 0;
            listPanel.Width = contentPanel.Width;
            listPanel.Height = Math.Max(0, contentPanel.Height - bottomPanel.Height);
            listPanel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            listPanel.AutoScroll = true;

            bottomPanel.Width = contentPanel.Width;
            bottomPanel.Top = listPanel.Bottom;

            btnStart.Left = headerPanel.Width - btnStart.Width - 8;
        }

        private void CenterImportLayout()
        {
            if (!_importMode || !_uiReady) return;
            if (dropPanel is null || btnImportCentered is null || dropPanel.IsDisposed || btnImportCentered.IsDisposed) return;

            const int dashedPadding = 12;
            var dashedWidth = dropPanel.Width - dashedPadding * 2;
            var desiredLeft = dashedPadding + (dashedWidth - btnImportCentered.Width) / 2;

            // 根据图标与文本间距，计算按钮最小 Top（图标 -> 文案 -> 安全间距 -> 按钮）
            int minButtonTop = 164 + 56 + (int)18f + 8 + 13;
            int desiredTop = Math.Max(248, minButtonTop);
            desiredTop = Math.Min(desiredTop, dropPanel.Height - dashedPadding - btnImportCentered.Height);

            btnImportCentered.Left = Math.Max(dashedPadding, desiredLeft);
            btnImportCentered.Top = Math.Max(dashedPadding, desiredTop);
        }

        private void EnterNormalModeLayout()
        {
            _importMode = false;
            _dropHover = false;

            // 切换可见性
            headerPanel.Visible = true;
            dropPanel.Visible = false;
            contentPanel.Visible = true;

            // 顶栏按钮可见（继续导入、开始转码）
            btnImportMore.Enabled = true;
            btnStart.Enabled = _jobs.Any(j => j.StatusLabel.Text is "等待中" or "查看原因" or "失败");
            btnStart.Text = "开始转码";
            btnStart.FillColor = Color.FromArgb(2, 132, 199);
            btnStart.FillHoverColor = ControlPaint.Light(Color.FromArgb(2, 132, 199));
            btnStart.FillPressColor = Color.FromArgb(2, 132, 199);

            LayoutContent();
            LayoutRows();
        }

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
                _images.EnsureImportImagesLoaded();

                int iconLeft = dashedRect.Left + (dashedRect.Width - 56) / 2;
                var iconRect = new Rectangle(iconLeft, 164, 56, 56);
                var iconImg = isHover ? _images.ImportHover : _images.ImportNormal;
                if (iconImg != null)
                    g.DrawImage(iconImg, iconRect);

                var text = "拖拽视频文件或导入";
                using var font = new Font("OPlusSans 3.0", 12f, FontStyle.Regular, GraphicsUnit.Point);
                using var brush = new SolidBrush(Color.FromArgb(100, 116, 139));

                float textTop = iconRect.Bottom + 13;
                textTop = Math.Max(dashedRect.Top, Math.Min(textTop, dashedRect.Bottom - 18f));

                var textRect = new RectangleF(dashedRect.Left, textTop, dashedRect.Width, 18f);
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
                if (_importMode) _stage = AppStage.ImportHover;
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
            if (_importMode) _stage = AppStage.ImportIdle;
            dropPanel.Invalidate();
        }

        private void DropPanel_DragDrop(object? sender, DragEventArgs e)
        {
            _dropHover = false;
            if (_importMode) _stage = AppStage.ImportIdle;
            dropPanel.Invalidate();

            if (e.Data == null) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
                AddFiles(files);
        }

        private void BtnImportMore_Click(object? sender, EventArgs e)
        {
            if (_stage == AppStage.Transcoding) return;
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                AddFiles(openFileDialog.FileNames);
                if (!_importMode)
                    LayoutRows();
            }
        }

        private void AddFiles(IEnumerable<string> files)
        {
            _images.EnsureRowIconsLoaded();

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
                    EnterNormalModeLayout();
                    _stage = AppStage.ListReady;
                }
                else
                {
                    LayoutRows();
                }
                btnStart.Enabled = _jobs.Any(j => j.StatusLabel.Text is "等待中" or "查看原因" or "失败");
            }
        }

        private const int RowHeightBase = 54;
        private const int RowSpacing = 8;

        private FileJob CreateJob(string inputPath)
        {
            var rowPanel = new UIPanel
            {
                Width = listPanel.ClientSize.Width - 24,
                Height = RowHeightBase,
                Radius = 6,
                FillColor = Color.White,
                RectColor = Color.FromArgb(220, 220, 220)
            };

            var picFile = new PictureBox
            {
                Left = 10,
                Top = 10,
                Width = 16,
                Height = 16,
                SizeMode = PictureBoxSizeMode.StretchImage,
                Image = _images.IcoFile
            };

            var lblName = new UILabel
            {
                Left = picFile.Right + 8,
                Top = 8,
                Width = rowPanel.Width - 220,
                Height = 18,
                Text = $"{Path.GetFileName(inputPath)}",
                Font = new Font("微软雅黑", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 63, 65)
            };

            var bar = new UIProcessBar
            {
                Left = 10,
                Top = lblName.Bottom + 6,
                Width = rowPanel.Width - 40,
                Height = 12, // 调高以提升可见度
                Maximum = 100,
                Value = 0,
                Visible = false,
                StyleCustomMode = true,
                FillColor = ProgressBarFillColor,   // 背景
                ForeColor = ProgressBarForeColor,   // 前景（进度色）
                RectColor = Color.Transparent        
            };
            // 兼容可能存在的 ProcessColor 属性（不同版本 Sunny.UI）
            TrySetProcessColor(bar, ProgressBarForeColor);

            var lblPercent = new UILabel
            {
                Left = bar.Right - 40,
                Top = bar.Top - 1,
                Width = 40,
                Height = 14,
                Text = "0%",
                Font = new Font("微软雅黑", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 63, 65),
                Visible = false,
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };

            var picStatus = new PictureBox
            {
                Width = 16,
                Height = 16,
                Top = 12,
                SizeMode = PictureBoxSizeMode.StretchImage
            };

            var lblStatus = new UILabel
            {
                Top = 10,
                Width = 80,
                Height = 18,
                Text = "等待中",
                Font = new Font("微软雅黑", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Cursor = Cursors.Default
            };

            rowPanel.Controls.Add(picFile);
            rowPanel.Controls.Add(lblName);
            rowPanel.Controls.Add(bar);
            rowPanel.Controls.Add(lblPercent);
            rowPanel.Controls.Add(picStatus);
            rowPanel.Controls.Add(lblStatus);

            var job = new FileJob
            {
                InputPath = inputPath,
                OutputPath = "",
                Row = rowPanel,
                LeftIcon = picFile,
                NameLabel = lblName,
                Bar = bar,
                PercentLabel = lblPercent,
                StatusIcon = picStatus,
                StatusLabel = lblStatus
            };

            lblStatus.Click += (_, __) =>
            {
                if (!string.IsNullOrEmpty(job.ErrorMessage) && lblStatus.Text == "查看原因")
                    UIMessageBox.Show(job.ErrorMessage, "失败原因");
            };

            ApplyStatusStyle(job, "等待中");
            return job;
        }

        private static void TrySetProcessColor(UIProcessBar bar, Color color)
        {
            var prop = bar.GetType().GetProperty("ProcessColor");
            if (prop != null && prop.CanWrite)
            {
                try { prop.SetValue(bar, color); } catch { }
            }
        }

        private void AddRow(FileJob job)
        {
            listPanel.Controls.Add(job.Row);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (!_uiReady) return;
            LayoutContent();
            if (!_importMode) LayoutRows();
            else CenterImportLayout();
        }

        private void LayoutRows()
        {
            if (listPanel is null || listPanel.IsDisposed) return;

            int y = RowSpacing;
            int innerRightPadding = 8;

            foreach (var job in _jobs)
            {
                var row = job.Row;
                row.Left = 8;
                row.Top = y;
                row.Width = listPanel.ClientSize.Width - 16;

                // 右侧状态标签定位（保持宽度）
                job.StatusLabel.Left = row.Width - innerRightPadding - job.StatusLabel.Width;
                job.StatusIcon.Left = job.StatusLabel.Left - 4 - job.StatusIcon.Width;

                // 进度条：从文件图标左边（10）开始到状态图标前留 8px
                int barLeft = job.LeftIcon.Left; // 10
                int barRightLimit = job.StatusIcon.Left - 8;
                int barWidth = Math.Max(50, barRightLimit - barLeft);

                job.Bar.Left = barLeft;
                job.Bar.Width = barWidth;

                // 文件名宽度：从名称左到进度条右（或状态图标左 - 间距）
                int nameRightLimit = barRightLimit;
                job.NameLabel.Left = job.LeftIcon.Right + 8;
                job.NameLabel.Width = Math.Max(40, nameRightLimit - job.NameLabel.Left);

                // 进度条纵向：在文件名下方
                job.Bar.Top = job.NameLabel.Bottom + 6;

                // 百分比放在进度条右端内部（不挤出）
                job.PercentLabel.Left = job.Bar.Left + job.Bar.Width - job.PercentLabel.Width;
                job.PercentLabel.Top = job.Bar.Top - 1;

                // 行高度根据进度条动态调整（底部留 8px）
                int rowNeeded = job.Bar.Top + job.Bar.Height + 8;
                row.Height = Math.Max(RowHeightBase, rowNeeded);

                y += row.Height + RowSpacing;
            }

            listPanel.AutoScrollMinSize = new Size(0, Math.Max(0, y));
        }

        private async void BtnStart_Click(object? sender, EventArgs e)
        {
            if (_stage == AppStage.Transcoding)
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    _cts.Cancel();
                    btnStart.Enabled = false;
                    btnStart.Text = "停止中...";
                }
                return;
            }

            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            if (!File.Exists(ffmpegPath))
            {
                UIMessageBox.Show("未找到 ffmpeg.exe，请放到程序目录。", "提示");
                return;
            }

            // 仅首轮创建，后续复用（直到程序重启或你自行调用 ClearJobsAndList 重置）
            if (string.IsNullOrEmpty(_currentBatchDir) || !Directory.Exists(_currentBatchDir))
                _currentBatchDir = BatchOutput.CreateBatchOutputDir();

            // 为当前待处理的任务生成（或更新）唯一输出文件
            foreach (var job in _jobs.Where(j => j.StatusLabel.Text is "等待中" or "查看原因" or "失败"))
            {
                var name = Path.GetFileNameWithoutExtension(job.InputPath);
                var baseName = $"{name}_1080p";
                job.OutputPath = GetUniqueOutputPath(_currentBatchDir!, baseName, ".mp4");
            }

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _stage = AppStage.Transcoding;
            btnStart.Text = "停止";
            btnStart.FillColor = Color.FromArgb(220, 38, 38);
            btnStart.FillHoverColor = ControlPaint.Light(Color.FromArgb(220, 38, 38));
            btnStart.FillPressColor = Color.FromArgb(220, 38, 38);
            btnStart.Enabled = true;
            btnImportMore.Enabled = false;
            btnUpload.Enabled = false;

            var targets = _jobs.Where(j => j.StatusLabel.Text is "等待中" or "查看原因" or "失败").ToList();
            if (targets.Count == 0)
            {
                ResetHeaderAfterTranscode();
            }
            else
            {
                try
                {
                    var degree = Math.Max(1, _maxParallel);
                    await RunLimitedConcurrencyAsync(targets, degree, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    UIMessageBox.Show($"转码过程中出现错误：{ex.Message}", "错误");
                }
                finally
                {
                    _cts?.Dispose();
                    _cts = null;
                    ResetHeaderAfterTranscode();
                }
            }
        }

        // 生成不重复的输出文件路径（目录内存在则递增后缀）
        private static string GetUniqueOutputPath(string dir, string baseName, string ext)
        {
            var path = Path.Combine(dir, baseName + ext);
            if (!File.Exists(path))
                return path;

            int i = 1;
            while (true)
            {
                var candidate = Path.Combine(dir, $"{baseName}_{i}{ext}");
                if (!File.Exists(candidate))
                    return candidate;
                i++;
            }
        }

        // 新增：固定 N 个工人并行执行，避免调度阶段抛取消异常
        private Task RunLimitedConcurrencyAsync(List<FileJob> jobs, int degree, CancellationToken token)
        {
            if (jobs.Count == 0 || degree <= 0)
                return Task.CompletedTask;

            var queue = new System.Collections.Concurrent.ConcurrentQueue<FileJob>(jobs);
            var workers = new List<Task>(degree);

            for (int w = 0; w < degree; w++)
            {
                workers.Add(Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (!queue.TryDequeue(out var job))
                            break;

                        try
                        {
                            await TranscodeOne(job, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            job.ErrorMessage = ex.ToString();
                            SafeUI(() => ApplyStatusStyle(job, "查看原因"));
                        }
                    }
                }));
            }

            return Task.WhenAll(workers);
        }

        private void ResetHeaderAfterTranscode()
        {
            if (InvokeRequired)
            {
                Invoke((Action)ResetHeaderAfterTranscode);
                return;
            }

            var allDone = _jobs.All(j => j.StatusLabel.Text == "转码成功");
            _stage = allDone ? AppStage.AllDone : AppStage.ListReady;

            btnStart.Text = "开始转码";
            btnStart.FillColor = Color.FromArgb(2, 132, 199);
            btnStart.FillHoverColor = ControlPaint.Light(Color.FromArgb(2, 132, 199));
            btnStart.FillPressColor = Color.FromArgb(2, 132, 199);
            btnStart.Enabled = _jobs.Any(j => j.StatusLabel.Text is "等待中" or "查看原因" or "失败");

            btnImportMore.Enabled = true;
            btnUpload.Enabled = true;
            dropPanel.Invalidate();
        }

        private async Task TranscodeOne(FileJob job, CancellationToken token)
        {
            if (job.StatusLabel.Text == "转码成功") return;

            var finalPath = job.OutputPath;
            // 临时文件：保证最后扩展仍是 .mp4
            var dir = Path.GetDirectoryName(finalPath)!;
            var fileNoExt = Path.GetFileNameWithoutExtension(finalPath);
            var workPath = Path.Combine(dir, fileNoExt + ".__partial__.mp4");

            SafeUI(() =>
            {
                ApplyStatusStyle(job, "转码中");
                job.Bar.Value = 0;
                job.Bar.Visible = true;
                job.PercentLabel.Text = "0%";
                job.PercentLabel.Visible = true;
                // 确保颜色（某些主题切换后）
                job.Bar.ForeColor = ProgressBarForeColor;
                TrySetProcessColor(job.Bar, ProgressBarForeColor);
            });

            try
            {
                try { if (File.Exists(workPath)) File.Delete(workPath); } catch { }

                await _transcode.TranscodeAsync(
                    job.InputPath,
                    workPath,
                    token,
                    percent =>
                    {
                        var p = Math.Max(0, Math.Min(100, percent));
                        SafeUI(() => UpdateRow(job, progress: p));
                    });

                try
                {
                    if (File.Exists(finalPath))
                        File.Delete(finalPath);
                    File.Move(workPath, finalPath);
                }
                catch (Exception mvEx)
                {
                    throw new IOException($"输出文件重命名失败: {mvEx.Message}", mvEx);
                }

                SafeUI(() =>
                {
                    UpdateRow(job, progress: 100, status: "转码成功", success: true);
                    job.Bar.Visible = false;
                    job.PercentLabel.Visible = false;
                });
            }
            catch (OperationCanceledException)
            {
                try { if (File.Exists(workPath)) File.Delete(workPath); } catch { }
                SafeUI(() =>
                {
                    ApplyStatusStyle(job, "等待中");
                    job.Bar.Visible = false;
                    job.PercentLabel.Visible = false;
                });
            }
            catch (Exception ex)
            {
                try { if (File.Exists(workPath)) File.Delete(workPath); } catch { }
                job.ErrorMessage = ex.ToString();
                SafeUI(() => ApplyStatusStyle(job, "查看原因"));
            }
        }

        private void UpdateRow(FileJob job, int? progress = null, string? status = null, bool success = false)
        {
            if (IsDisposed) return;

            if (progress.HasValue)
            {
                var value = Math.Min(job.Bar.Maximum, Math.Max(0, progress.Value));
                if (job.Bar.Value != value)
                {
                    job.Bar.Value = value;
                    job.Bar.ForeColor = ProgressBarForeColor; // 保持颜色
                    TrySetProcessColor(job.Bar, ProgressBarForeColor);
                    job.Bar.Invalidate();
                }
                job.PercentLabel.Text = $"{value}%";
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
            job.Row.RectColor = Color.FromArgb(220, 220, 220);
            job.NameLabel.ForeColor = Color.FromArgb(60, 63, 65);

            switch (state)
            {
                case "转码中":
                    job.StatusLabel.Text = "转码中";
                    job.StatusLabel.ForeColor = Color.FromArgb(139, 92, 246);
                    job.StatusLabel.Cursor = Cursors.Default;
                    job.StatusIcon.Image = _images.IcoRunning;

                    job.Bar.Visible = true;
                    job.PercentLabel.Visible = true;
                    job.Bar.ForeColor = ProgressBarForeColor;
                    TrySetProcessColor(job.Bar, ProgressBarForeColor);
                    break;

                case "转码成功":
                    job.StatusLabel.Text = "转码成功";
                    job.StatusLabel.ForeColor = Color.FromArgb(40, 167, 69);
                    job.StatusLabel.Cursor = Cursors.Default;
                    job.StatusIcon.Image = _images.IcoSuccess;
                    job.Bar.Visible = false;
                    job.PercentLabel.Visible = false;
                    job.Row.RectColor = Color.FromArgb(190, 230, 200);
                    break;

                case "查看原因":
                case "失败":
                    job.StatusLabel.Text = "查看原因";
                    job.StatusLabel.ForeColor = Color.FromArgb(14, 165, 233);
                    job.StatusLabel.Cursor = Cursors.Hand;
                    job.StatusIcon.Image = _images.IcoError;
                    job.Bar.Visible = false;
                    job.PercentLabel.Visible = false;
                    job.NameLabel.ForeColor = Color.FromArgb(239, 68, 68);
                    break;

                case "等待中":
                default:
                    job.StatusLabel.Text = "等待中";
                    job.StatusLabel.ForeColor = Color.FromArgb(148, 163, 184);
                    job.StatusLabel.Cursor = Cursors.Default;
                    job.StatusIcon.Image = _images.IcoWaiting;
                    job.Bar.Visible = false;
                    job.PercentLabel.Visible = false;
                    break;
            }
        }

        private async Task ShowUploadMenuAsync()
        {
            var targets = await _uploadProvider.GetTargetsAsync();

            uploadMenu.Items.Clear();
            foreach (var item in targets)
            {
                var it = new ToolStripMenuItem(item);
                it.Click += (_, __) => UIMessageBox.Show($"选择上传到：{item}\n（此处调用上传接口）", "上传");
                uploadMenu.Items.Add(it);
            }
            uploadMenu.Show(btnUpload, new Point(btnUpload.Width - uploadMenu.Width, btnUpload.Height));
        }

        private void ClearJobsAndList()
        {
            foreach (var job in _jobs)
            {
                try
                {
                    if (!job.Row.IsDisposed)
                    {
                        listPanel.Controls.Remove(job.Row);
                        job.Row.Dispose();
                    }
                }
                catch { }
            }
            _jobs.Clear();
            listPanel.Controls.Clear();

            btnStart.Enabled = false;
            btnStart.Text = "开始转码";

            _currentBatchDir = null;

            LayoutRows();
        }
    }
}
