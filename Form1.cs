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
            Text = "视频转换工具";
            Width = 720;
            Height = 520;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            // 顶部按钮
            btnImportMore = new UIButton
            {
                Left = 40,
                Top = 28,
                Width = 120,
                Height = 32,
                Text = "继续导入"
            };
            btnStart = new UIButton
            {
                Left = btnImportMore.Right + 16,
                Top = 28,
                Width = 120,
                Height = 32,
                Text = "开始转码",
                Enabled = false
            };
            btnImportMore.Click += BtnImportMore_Click;
            btnStart.Click += BtnStart_Click;

            openFileDialog = new OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.avi;*.mkv;*.mov;*.flv;*.wmv",
                Multiselect = true
            };
            saveFileDialog = new SaveFileDialog { Filter = "MP4文件|*.mp4" };

            // 拖拽区域（仅用于提示与放置）
            dropPanel = new UIPanel
            {
                Left = 40,
                Top = 80,
                Width = 630,
                Height = 100,
                Radius = 6,
                FillColor = Color.White,
                RectColor = Color.FromArgb(180, 180, 180)
            };
            dropPanel.AllowDrop = true;
            dropPanel.DragEnter += DropPanel_DragEnter;
            dropPanel.DragDrop += DropPanel_DragDrop;
            dropPanel.Paint += DropPanel_Paint;

            // 文件列表区域（滚动容器）
            listPanel = new UIPanel
            {
                Left = 40,
                Top = dropPanel.Bottom + 16,
                Width = 630,
                Height = 320,
                Radius = 6,
                FillColor = Color.White,
                RectColor = Color.FromArgb(220, 220, 220),
                AutoScroll = true
            };

            Controls.Add(btnImportMore);
            Controls.Add(btnStart);
            Controls.Add(dropPanel);
            Controls.Add(listPanel);
        }

        private void DropPanel_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var pen = new Pen(Color.FromArgb(150, 150, 150), 2f) { DashStyle = DashStyle.Dash };
            var rect = new Rectangle(4, 4, dropPanel.Width - 8, dropPanel.Height - 8);
            g.DrawRectangle(pen, rect);

            var tip = "将视频文件拖拽到此处（可多选）";
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var tipBrush = new SolidBrush(Color.FromArgb(110, 110, 110));
            g.DrawString(tip, new Font("微软雅黑", 12F, FontStyle.Bold), tipBrush, rect, fmt);
        }

        private void DropPanel_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void DropPanel_DragDrop(object? sender, DragEventArgs e)
        {
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
                LayoutRows();
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
            if (listPanel is null || listPanel.IsDisposed) return; // 防御未初始化
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
