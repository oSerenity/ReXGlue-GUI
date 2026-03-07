namespace ReXGlue_GUI
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Label labelInfo;
        private System.Windows.Forms.Label labelBaseFolder;
        private System.Windows.Forms.TextBox textBoxBaseFolder;
        private System.Windows.Forms.Button buttonBrowse;
        private System.Windows.Forms.Label labelRexsdk;
        private System.Windows.Forms.TextBox textBoxRexsdk;
        private System.Windows.Forms.Label labelPathEntry;
        private System.Windows.Forms.TextBox textBoxPathEntry;
        private System.Windows.Forms.Button buttonApply;
        private System.Windows.Forms.Button buttonClose;

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.Label labelNewRoot;
        private System.Windows.Forms.TextBox textBoxNewProjectRoot;
        private System.Windows.Forms.Button buttonNewBrowse;
        private System.Windows.Forms.Label labelAppName;
        private System.Windows.Forms.TextBox textBoxAppName;
        private System.Windows.Forms.Button buttonInitProject;
        private System.Windows.Forms.TabPage tabPage3;
        private System.Windows.Forms.TabPage tabPage4;
        private System.Windows.Forms.TabPage tabPage5;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            labelInfo = new Label();
            labelBaseFolder = new Label();
            textBoxBaseFolder = new TextBox();
            buttonBrowse = new Button();
            labelRexsdk = new Label();
            textBoxRexsdk = new TextBox();
            labelPathEntry = new Label();
            textBoxPathEntry = new TextBox();
            buttonApply = new Button();
            buttonClose = new Button();
            tabControl1 = new TabControl();
            tabPage1 = new TabPage();
            tabPage2 = new TabPage();
            labelNewRoot = new Label();
            textBoxNewProjectRoot = new TextBox();
            buttonNewBrowse = new Button();
            labelAppName = new Label();
            textBoxAppName = new TextBox();
            buttonInitProject = new Button();
            tabPage3 = new TabPage();
            tabPage4 = new TabPage();
            tabPage5 = new TabPage();
            tabControl1.SuspendLayout();
            tabPage1.SuspendLayout();
            SuspendLayout();
            // 
            // labelInfo
            // 
            labelInfo.Location = new Point(6, 12);
            labelInfo.Name = "labelInfo";
            labelInfo.Size = new Size(710, 36);
            labelInfo.TabIndex = 0;
            labelInfo.Text = "Select your base folder. The tool will create/update the user variable REXSDK and add the Release folder to your user Path.";
            // 
            // labelBaseFolder
            // 
            labelBaseFolder.AutoSize = true;
            labelBaseFolder.Location = new Point(6, 62);
            labelBaseFolder.Name = "labelBaseFolder";
            labelBaseFolder.Size = new Size(115, 15);
            labelBaseFolder.TabIndex = 1;
            labelBaseFolder.Text = "Selected base folder:";
            // 
            // textBoxBaseFolder
            // 
            textBoxBaseFolder.Location = new Point(6, 87);
            textBoxBaseFolder.Name = "textBoxBaseFolder";
            textBoxBaseFolder.Size = new Size(590, 23);
            textBoxBaseFolder.TabIndex = 2;
            textBoxBaseFolder.TextChanged += textBoxBaseFolder_TextChanged;
            // 
            // buttonBrowse
            // 
            buttonBrowse.Location = new Point(606, 85);
            buttonBrowse.Name = "buttonBrowse";
            buttonBrowse.Size = new Size(100, 28);
            buttonBrowse.TabIndex = 3;
            buttonBrowse.Text = "Browse...";
            buttonBrowse.UseVisualStyleBackColor = true;
            buttonBrowse.Click += buttonBrowse_Click;
            // 
            // labelRexsdk
            // 
            labelRexsdk.AutoSize = true;
            labelRexsdk.Location = new Point(6, 132);
            labelRexsdk.Name = "labelRexsdk";
            labelRexsdk.Size = new Size(120, 15);
            labelRexsdk.TabIndex = 4;
            labelRexsdk.Text = "REXSDK will be set to:";
            // 
            // textBoxRexsdk
            // 
            textBoxRexsdk.Location = new Point(6, 154);
            textBoxRexsdk.Name = "textBoxRexsdk";
            textBoxRexsdk.ReadOnly = true;
            textBoxRexsdk.Size = new Size(700, 23);
            textBoxRexsdk.TabIndex = 5;
            // 
            // labelPathEntry
            // 
            labelPathEntry.AutoSize = true;
            labelPathEntry.Location = new Point(6, 190);
            labelPathEntry.Name = "labelPathEntry";
            labelPathEntry.Size = new Size(162, 15);
            labelPathEntry.TabIndex = 6;
            labelPathEntry.Text = "This Path entry will be added:";
            // 
            // textBoxPathEntry
            // 
            textBoxPathEntry.Location = new Point(6, 212);
            textBoxPathEntry.Name = "textBoxPathEntry";
            textBoxPathEntry.ReadOnly = true;
            textBoxPathEntry.Size = new Size(700, 23);
            textBoxPathEntry.TabIndex = 7;
            // 
            // buttonApply
            // 
            buttonApply.Location = new Point(526, 257);
            buttonApply.Name = "buttonApply";
            buttonApply.Size = new Size(85, 30);
            buttonApply.TabIndex = 8;
            buttonApply.Text = "Apply";
            buttonApply.UseVisualStyleBackColor = true;
            buttonApply.Click += buttonApply_Click;
            // 
            // buttonClose
            // 
            buttonClose.Location = new Point(621, 257);
            buttonClose.Name = "buttonClose";
            buttonClose.Size = new Size(85, 30);
            buttonClose.TabIndex = 9;
            buttonClose.Text = "Close";
            buttonClose.UseVisualStyleBackColor = true;
            buttonClose.Click += buttonClose_Click;
            // 
            // tabControl1
            // 
            tabControl1.Controls.Add(tabPage2);
            tabControl1.Controls.Add(tabPage3);
            tabControl1.Controls.Add(tabPage4);
            tabControl1.Controls.Add(tabPage5);
            tabControl1.Dock = DockStyle.Fill;
            tabControl1.Location = new Point(0, 0);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new Size(1119, 455);
            tabControl1.TabIndex = 10;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(labelInfo);
            tabPage1.Controls.Add(buttonClose);
            tabPage1.Controls.Add(labelBaseFolder);
            tabPage1.Controls.Add(buttonApply);
            tabPage1.Controls.Add(textBoxBaseFolder);
            tabPage1.Controls.Add(textBoxPathEntry);
            tabPage1.Controls.Add(buttonBrowse);
            tabPage1.Controls.Add(labelPathEntry);
            tabPage1.Controls.Add(labelRexsdk);
            tabPage1.Controls.Add(textBoxRexsdk);
            tabPage1.Location = new Point(4, 24);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(1111, 427);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "Environment Variable Tool";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // tabPage2
            // 
            tabPage2.Location = new Point(4, 24);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(1111, 427);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "New Project";
            tabPage2.UseVisualStyleBackColor = true;

            // labelNewRoot
            labelNewRoot.AutoSize = true;
            labelNewRoot.Location = new Point(6, 12);
            labelNewRoot.Name = "labelNewRoot";
            labelNewRoot.Size = new Size(65, 15);
            labelNewRoot.TabIndex = 0;
            labelNewRoot.Text = "Root Folder:";

            // textBoxNewProjectRoot
            textBoxNewProjectRoot.Location = new Point(6, 34);
            textBoxNewProjectRoot.Name = "textBoxNewProjectRoot";
            textBoxNewProjectRoot.Size = new Size(590, 23);
            textBoxNewProjectRoot.TabIndex = 1;

            // buttonNewBrowse
            buttonNewBrowse.Location = new Point(606, 32);
            buttonNewBrowse.Name = "buttonNewBrowse";
            buttonNewBrowse.Size = new Size(100, 28);
            buttonNewBrowse.TabIndex = 2;
            buttonNewBrowse.Text = "Browse...";
            buttonNewBrowse.UseVisualStyleBackColor = true;
            buttonNewBrowse.Click += buttonNewBrowse_Click;

            // labelAppName
            labelAppName.AutoSize = true;
            labelAppName.Location = new Point(6, 72);
            labelAppName.Name = "labelAppName";
            labelAppName.Size = new Size(98, 15);
            labelAppName.TabIndex = 3;
            labelAppName.Text = "Application Name:";

            // textBoxAppName
            textBoxAppName.Location = new Point(6, 94);
            textBoxAppName.Name = "textBoxAppName";
            textBoxAppName.Size = new Size(590, 23);
            textBoxAppName.TabIndex = 4;

            // buttonInitProject
            buttonInitProject.Location = new Point(6, 130);
            buttonInitProject.Name = "buttonInitProject";
            buttonInitProject.Size = new Size(150, 30);
            buttonInitProject.TabIndex = 5;
            buttonInitProject.Text = "Initialize Project";
            buttonInitProject.UseVisualStyleBackColor = true;
            buttonInitProject.Click += buttonInitProject_Click;

            tabPage2.Controls.Add(labelNewRoot);
            tabPage2.Controls.Add(textBoxNewProjectRoot);
            tabPage2.Controls.Add(buttonNewBrowse);
            tabPage2.Controls.Add(labelAppName);
            tabPage2.Controls.Add(textBoxAppName);
            tabPage2.Controls.Add(buttonInitProject);
            // 
            // tabPage3
            // 
            tabPage3.Location = new Point(4, 24);
            tabPage3.Name = "tabPage3";
            tabPage3.Padding = new Padding(3);
            tabPage3.Size = new Size(1111, 427);
            tabPage3.TabIndex = 2;
            tabPage3.Text = "Code Generation";
            tabPage3.UseVisualStyleBackColor = true;
            // 
            // tabPage4
            // 
            tabPage4.Location = new Point(4, 24);
            tabPage4.Name = "tabPage4";
            tabPage4.Padding = new Padding(3);
            tabPage4.Size = new Size(1111, 427);
            tabPage4.TabIndex = 3;
            tabPage4.Text = "Address Parser";
            tabPage4.UseVisualStyleBackColor = true;
            // 
            // tabPage5
            // 
            tabPage5.Location = new Point(4, 24);
            tabPage5.Name = "tabPage5";
            tabPage5.Padding = new Padding(3);
            tabPage5.Size = new Size(1111, 427);
            tabPage5.TabIndex = 4;
            tabPage5.Text = "Output";
            tabPage5.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1119, 455);
            Controls.Add(tabControl1);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "REXGlue GUI";
            tabControl1.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage1.PerformLayout();
            ResumeLayout(false);
        }
    }
}