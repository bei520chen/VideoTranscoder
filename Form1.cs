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

        private void InitializeComponent()
        {
            this.txtInput = new TextBox { Left = 20, Top = 20, Width = 300 };
            this.btnBrowseInput = new Button { Left = 330, Top = 20, Width = 80, Text = "选择视频" };
            this.txtOutput = new TextBox { Left = 20, Top = 60, Width = 300 };
            this.btnBrowseOutput = new Button { Left = 330, Top = 60, Width = 80, Text = "输出路径" };
            this.btnConvert = new Button { Left = 20, Top = 100, Width = 390, Text = "转换为1080P 30fps MP4" };

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

            this.Text = "视频转换工具";
            this.Width = 450;
            this.Height = 200;
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

                await FFMpegArguments
                    .FromFileInput(txtInput.Text)
                    .OutputToFile(txtOutput.Text, true, options => options
                        .WithVideoCodec("libx264")
                         .WithAudioCodec("aac")
                         .WithAudioBitrate(128_000)
                        .WithCustomArgument($"-vf \"{vf}\"")
                        .WithFramerate(30)
                    )
                    .ProcessAsynchronously();


                MessageBox.Show("转换完成！");
            }
            catch (Exception ex)
            {
                MessageBox.Show("转换失败：" + ex.Message);
            }
        }
    }
}
