using System;
using System.Windows.Forms;

namespace VideoConverterWinForms
{
    public class MainForm : Form
    {
        private TextBox txtInput;
        private Button btnBrowse;
        private TextBox txtOutput;
        private Button btnSaveAs;
        private Button btnConvert;
        private ProgressBar progressBar1;
        private TextBox txtLog;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.txtInput = new TextBox();
            this.btnBrowse = new Button();
            this.txtOutput = new TextBox();
            this.btnSaveAs = new Button();
            this.btnConvert = new Button();
            this.progressBar1 = new ProgressBar();
            this.txtLog = new TextBox();

            // 窗体属性
            this.Text = "视频转换工具";
            this.Width = 600;
            this.Height = 400;
            this.StartPosition = FormStartPosition.CenterScreen;

            // txtInput
            this.txtInput.Location = new System.Drawing.Point(20, 20);
            this.txtInput.Width = 400;
            this.txtInput.ReadOnly = true;

            // btnBrowse
            this.btnBrowse.Text = "选择视频";
            this.btnBrowse.Location = new System.Drawing.Point(430, 18);
            this.btnBrowse.Width = 100;

            // txtOutput
            this.txtOutput.Location = new System.Drawing.Point(20, 60);
            this.txtOutput.Width = 400;
            this.txtOutput.ReadOnly = true;

            // btnSaveAs
            this.btnSaveAs.Text = "保存为";
            this.btnSaveAs.Location = new System.Drawing.Point(430, 58);
            this.btnSaveAs.Width = 100;

            // btnConvert
            this.btnConvert.Text = "开始转换";
            this.btnConvert.Location = new System.Drawing.Point(20, 100);
            this.btnConvert.Width = 510;

            // progressBar1
            this.progressBar1.Location = new System.Drawing.Point(20, 140);
            this.progressBar1.Width = 510;

            // txtLog
            this.txtLog.Location = new System.Drawing.Point(20, 180);
            this.txtLog.Width = 510;
            this.txtLog.Height = 150;
            this.txtLog.Multiline = true;
            this.txtLog.ScrollBars = ScrollBars.Vertical;

            // 添加控件
            this.Controls.Add(this.txtInput);
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.txtOutput);
            this.Controls.Add(this.btnSaveAs);
            this.Controls.Add(this.btnConvert);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.txtLog);
        }
    }
}
