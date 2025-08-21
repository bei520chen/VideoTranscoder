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

            // 表头（32 高）- 放置继续导入/开始转码
            headerPanel = new UIPanel
            {
                Left = 0,
                Top = 0,
                Width = ClientSize.Width,
                Height = 32,
                FillColor = Color.White,
                RectColor = Color.Transparent
            };
            // 导入模式下隐藏表头（不显示“继续导入/开始转码”）
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
            // 右对齐
            btnStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnStart.Click += BtnStart_Click;

            headerPanel.Controls.Add(btnImportMore);
            headerPanel.Controls.Add(btnStart);

            // 导入模式拖拽面板（保持不变）
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

            // 列表 + 底部区（保持容器高 468，不改变拖拽区）
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

            // 底部按钮条（位置紧跟列表底部）
            bottomPanel = new UIPanel
            {
                Left = 0,
                Width = contentPanel.Width,
                Height = 64,
                Top = 248, // 先占位，LayoutContent 会重算
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
                FillColor = Color.White,                // 正常背景
                FillHoverColor = Color.White,           // 悬停背景 = 正常背景
                FillPressColor = Color.White,           // 按下背景
                RectColor = Color.FromArgb(203, 213, 225),
                RectHoverColor = Color.FromArgb(203, 213, 225), // 悬停边框 = 普通边框
                ForeColor = Color.FromArgb(51, 65, 85),         // 字体颜色
                ForeHoverColor = Color.FromArgb(51, 65, 85),    // 悬停字体颜色
                ForePressColor = Color.FromArgb(51, 65, 85)     // 按下字体颜色
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
            // 右上对齐到底部条
            btnUpload.Left = bottomPanel.Width - btnUpload.Width - 8;
            btnUpload.Top = 12;
            btnUpload.Anchor = AnchorStyles.Right | AnchorStyles.Top;

            btnUpload.MouseEnter += async (_, __) => await ShowUploadMenuAsync();
            btnUpload.Click += async (_, __) => await ShowUploadMenuAsync();

            bottomPanel.Controls.Add(btnOpenFolder);
            bottomPanel.Controls.Add(btnUpload);

            // 文件列表：固定高度 248，超出出现滚动条
            listPanel = new UIPanel
            {
                Left = 0,
                Top = 0,
                Width = contentPanel.Width,
                Height = 248, // 固定 248
                Radius = 6,
                FillColor = Color.White,
                RectColor = Color.FromArgb(220, 220, 220),
                AutoScroll = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right // 不跟随底部拉伸
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

            // 保持拖拽区域不变
            dropPanel.Left = 0;
            dropPanel.Top = 32;
            dropPanel.Width = ClientSize.Width;
            dropPanel.Height = 468;

            // 列表容器
            contentPanel.Left = 0;
            contentPanel.Top = 32; // 保证在 headerPanel 下方
            contentPanel.Width = ClientSize.Width;
            contentPanel.Height = 468;

            // 列表填满底部按钮上方空间
            listPanel.Left = 0;
            listPanel.Top = 0;
            listPanel.Width = contentPanel.Width;
            listPanel.Height = Math.Max(0, contentPanel.Height - bottomPanel.Height);
            listPanel.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            listPanel.AutoScroll = true; // 确保开启

            // 底部条紧贴列表底部
            bottomPanel.Width = contentPanel.Width;
            bottomPanel.Top = listPanel.Bottom;

            // 表头按钮右对齐
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
            if (_stage == AppStage.Transcoding) return; // 转码中不可导入
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                ClearJobsAndList();
                AddFiles(openFileDialog.FileNames);
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

        private FileJob CreateJob(string inputPath)
        {
            var rowPanel = new UIPanel
            {
                Width = listPanel.ClientSize.Width - 24,
                Height = 36, // 每行 36px
                Radius = 6,
                FillColor = Color.White,
                RectColor = Color.FromArgb(220, 220, 220)
            };

            var picFile = new PictureBox
            {
                Left = 10,
                Top = 10, // 居中 16x16
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
                Left = lblName.Left,
                Top = rowPanel.Height - 10, // 靠近底部
                Width = rowPanel.Width - 220,
                Height = 8, // 压缩为 8px
                Maximum = 100,
                Value = 0,
                Visible = false,
                StyleCustomMode = true,
                FillColor = Color.FromArgb(229, 231, 235),
                ForeColor = Color.FromArgb(34, 197, 94),
                RectColor = Color.Transparent
            };

            var lblPercent = new UILabel
            {
                Left = bar.Right + 10,
                Top = bar.Top - 1,
                Width = 60,
                Height = 16,
                Text = "0%",
                Font = new Font("微软雅黑", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 63, 65),
                Visible = false
            };

            var picStatus = new PictureBox
            {
                Width = 16,
                Height = 16,
                Top = 10,
                SizeMode = PictureBoxSizeMode.StretchImage
            };

            var lblStatus = new UILabel
            {
                Top = 8,
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

            int y = 8; // 注意：不要再用 AutoScrollPosition.Y 抵消
            foreach (var job in _jobs)
            {
                job.Row.Left = 8;
                job.Row.Top = y;
                job.Row.Width = listPanel.ClientSize.Width - 16;

                job.NameLabel.Width = job.Row.Width - 220;
                job.Bar.Left = job.NameLabel.Left;
                job.Bar.Width = job.Row.Width - 220;

                int rightPadding = 8;
                job.StatusLabel.Left = job.Row.Width - rightPadding - job.StatusLabel.Width;
                job.StatusIcon.Left = job.StatusLabel.Left - 4 - job.StatusIcon.Width;

                job.PercentLabel.Left = job.Bar.Right + 10;

                y += job.Row.Height + 8;
            }

            // 设置滚动最小尺寸以触发滚动条
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

            _currentBatchDir = BatchOutput.CreateBatchOutputDir();
            foreach (var job in _jobs.Where(j => j.StatusLabel.Text is "等待中" or "查看原因" or "失败"))
            {
                var name = Path.GetFileNameWithoutExtension(job.InputPath);
                job.OutputPath = Path.Combine(_currentBatchDir, $"{name}_1080p.mp4");
            }

            _cts = new CancellationTokenSource();
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
                return;
            }

            var sem = new SemaphoreSlim(Math.Max(1, _maxParallel));
            var running = new List<Task>();
            foreach (var job in targets)
            {
                await sem.WaitAsync(_cts.Token).ConfigureAwait(false);
                var t = Task.Run(async () =>
                {
                    try
                    {
                        await TranscodeOne(job, _cts.Token);
                    }
                    finally
                    {
                        sem.Release();
                    }
                }, _cts.Token);
                running.Add(t);
            }

            try
            {
                await Task.WhenAll(running);
            }
            catch (OperationCanceledException)
            {
                // 用户取消
            }
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

            SafeUI(() =>
            {
                ApplyStatusStyle(job, "转码中");
                job.Bar.Value = 0;
                job.Bar.Visible = true;
                job.PercentLabel.Text = "0%";
                job.PercentLabel.Visible = true;
            });

            try
            {
                await _transcode.TranscodeAsync(
                    job.InputPath,
                    job.OutputPath,
                    token,
                    percent =>
                    {
                        var p = Math.Max(0, Math.Min(100, percent));
                        SafeUI(() => UpdateRow(job, progress: p));
                    });

                SafeUI(() =>
                {
                    UpdateRow(job, progress: 100, status: "转码成功", success: true);
                    job.Bar.Visible = false;
                    job.PercentLabel.Visible = false;
                });
            }
            catch (OperationCanceledException)
            {
                SafeUI(() =>
                {
                    ApplyStatusStyle(job, "等待中");
                    job.Bar.Visible = false;
                    job.PercentLabel.Visible = false;
                });
            }
            catch (Exception ex)
            {
                job.ErrorMessage = ex.ToString();
                SafeUI(() => ApplyStatusStyle(job, "查看原因"));
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
