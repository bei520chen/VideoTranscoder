using FFMpegCore;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using static System.Windows.Forms.AxHost;

namespace VideoConverter
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();

        }

        private TextBox txtInput;
        private TextBox txtOutput;
        private Button btnBrowseInput;
        private Button btnBrowseOutput;
        private Button btnConvert;
        private OpenFileDialog openFileDialog;
        private SaveFileDialog saveFileDialog;
        private ProgressBar progressBar;
        private Label lblPercent; // 声明为类成员

        private void InitializeComponent()
        {
            // 3D 边框样式
            this.txtInput = new TextBox
            {
                Left = 130,
                Top = 60,
                Width = 270,
                Font = new Font("微软雅黑", 10),
                BorderStyle = BorderStyle.FixedSingle // 改为细边框，更简洁
            };
            this.btnBrowseInput = new Button
            {
                Left = 410,
                Top = 57,
                Width = 90,
                Height = 32,
                Text = "选择视频",
                Font = new Font("微软雅黑", 10),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Standard // 3D 按钮
            };
            this.txtOutput = new TextBox
            {
                Left = 130,
                Top = 105,
                Width = 270,
                Font = new Font("微软雅黑", 10),
                BorderStyle = BorderStyle.FixedSingle // 改为细边框，更简洁
            };
            this.btnBrowseOutput = new Button
            {
                Left = 410,
                Top = 102,
                Width = 90,
                Height = 32,
                Text = "输出路径",
                Font = new Font("微软雅黑", 10),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Standard // 3D 按钮
            };
            this.btnConvert = new Button
            {
                Left = 130,
                Top = 155,
                Width = 270,
                Height = 40,
                Text = "转换为1080P 30fps MP4",
                Font = new Font("微软雅黑", 12, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 153, 51),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Popup // 立体感更强
            };

            this.openFileDialog = new OpenFileDialog { Filter = "视频文件|*.mp4;*.avi;*.mkv;*.mov;*.flv;*.wmv" };
            this.saveFileDialog = new SaveFileDialog { Filter = "MP4文件|*.mp4" };

            this.btnBrowseInput.Click += BtnBrowseInput_Click;
            this.btnBrowseOutput.Click += BtnBrowseOutput_Click;
            this.btnConvert.Click += BtnConvert_Click;

            this.Controls.Add(txtInput);
            this.Controls.Add(btnBrowseInput);
            this.Controls.Add(txtOutput);
            this.Controls.Add(btnBrowseOutput);
            this.Controls.Add(btnConvert);

            // 初始化进度条
            this.progressBar = new ProgressBar
            {
                Left = 130,
                Top = 215,
                Width = 270,
                Height = 22,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous, // 平滑进度条
                ForeColor = Color.FromArgb(0, 120, 215),
                BackColor = Color.WhiteSmoke
            };
            this.Controls.Add(progressBar);

            // 添加进度百分比标签
            this.lblPercent = new Label
            {
                Left = this.progressBar.Left + this.progressBar.Width + 10,
                Top = this.progressBar.Top - 2,
                Width = 50,
                Height = 24,
                Text = "0%",
                Font = new Font("微软雅黑", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 120, 215),
                TextAlign = ContentAlignment.MiddleLeft
            };
            this.Controls.Add(lblPercent);

            // 标签和标题去掉3D边框
            var lblInput = new Label
            {
                Left = 20,
                Top = 63,
                Width = 110,
                Text = "输入视频文件：",
                Font = new Font("微软雅黑", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(64, 64, 64),
                BorderStyle = BorderStyle.None // 无边框
            };
            var lblOutput = new Label
            {
                Left = 20,
                Top = 108,
                Width = 110,
                Text = "输出文件路径：",
                Font = new Font("微软雅黑", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(64, 64, 64),
                BorderStyle = BorderStyle.None // 无边框
            };
            this.Controls.Add(lblInput);
            this.Controls.Add(lblOutput);

            var lblTitle = new Label
            {
                Left = 0,
                Top = 10,
                Width = 520,
                Height = 40,
                Text = "视频转换工具",
                Font = new Font("微软雅黑", 14, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 120, 215),
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.None // 无边框
            };
            this.Controls.Add(lblTitle);

            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Text = "视频转换工具";
            this.Width = 530;
            this.Height = 320;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;
        }

        private void BtnBrowseInput_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                txtInput.Text = openFileDialog.FileName;
                string defaultOutput = Path.Combine(Path.GetDirectoryName(openFileDialog.FileName),
                    Path.GetFileNameWithoutExtension(openFileDialog.FileName) + "_1080p.mp4");
                txtOutput.Text = defaultOutput;
            }
        }

        private void BtnBrowseOutput_Click(object sender, EventArgs e)
        {
            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                txtOutput.Text = saveFileDialog.FileName;
            }
        }

        private async void BtnConvert_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtInput.Text) || string.IsNullOrWhiteSpace(txtOutput.Text))
            {
                MessageBox.Show("请选择输入和输出路径！");
                return;
            }

            try
            {
                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    MessageBox.Show("请将 ffmpeg.exe 放在程序目录下的 FFmpeg 文件夹中！");
                    return;
                }
                string vf = "scale='if(lt(iw,ih),1080,-2)':'if(lt(iw,ih),-2,1080)',format=yuv420p";

                // 显示进度条
                progressBar.Visible = true;
                progressBar.Value = 0;

                var conversion = FFMpegArguments
                    .FromFileInput(txtInput.Text)
                    .OutputToFile(txtOutput.Text, true, options => options
                        .WithVideoCodec("libx264")
                        .WithAudioCodec("aac")
                        .WithAudioBitrate(128_000)
                        .WithCustomArgument($"-vf \"{vf}\"")
                        .WithFramerate(30)
                    );

                conversion.NotifyOnProgress(percent =>
                {
                    int percentInt = (int)percent;
                    if (percentInt > 100) percentInt = 100;
                    if (percentInt < 0) percentInt = 0;
                    // 统一用主窗体的InvokeRequired判断
                    if (this.InvokeRequired)
                    {
                        this.Invoke(() =>
                        {
                            progressBar.Value = percentInt;
                            lblPercent.Text = percentInt + "%";
                        });
                    }
                    else
                    {
                        progressBar.Value = percentInt;
                        lblPercent.Text = percentInt + "%";
                    }
                }, TimeSpan.FromMilliseconds(500));

                await conversion.ProcessAsynchronously();

                // 收尾阶段：显示“正在收尾...”并保持进度条100%
                lblPercent.Text = "100%";
                progressBar.Value = 100;

                // 显示收尾提示
                var lblFinishing = new Label
                {
                    Left = progressBar.Left,
                    Top = progressBar.Top + progressBar.Height + 10,
                    Width = 200,
                    Height = 24,
                    Text = "正在收尾，请稍候...",
                    Font = new Font("微软雅黑", 10, FontStyle.Italic),
                    ForeColor = Color.Gray,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                this.Controls.Add(lblFinishing);
                this.Refresh();

                // 可选：延迟一小段时间模拟收尾动画
                await Task.Delay(600);

                // 移除收尾提示
                this.Controls.Remove(lblFinishing);

                MessageBox.Show("转换完成！");
            }
            catch (Exception ex)
            {
                MessageBox.Show("转换失败：" + ex.Message);
            }
            finally
            {
                // 隐藏进度条
                progressBar.Visible = false;
                progressBar.Value = 0;
            }
        }
    }
}
