namespace Crawler
{
    partial class ACrawlerWin
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.startButton = new System.Windows.Forms.Button();
            this.textWindow = new System.Windows.Forms.RichTextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.UrlChecking = new System.Windows.Forms.Label();
            this.downloadSeedButton = new System.Windows.Forms.Button();
            this.threadProgressText = new System.Windows.Forms.RichTextBox();
            this.editTopicsButton = new System.Windows.Forms.Button();
            this.generateTrainingSetButton = new System.Windows.Forms.Button();
            this.label2 = new System.Windows.Forms.Label();
            this.pagesPerTopicNUD = new System.Windows.Forms.NumericUpDown();
            this.editBlacklistButton = new System.Windows.Forms.Button();
            this.trainTestButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.pagesPerTopicNUD)).BeginInit();
            this.SuspendLayout();
            // 
            // startButton
            // 
            this.startButton.Location = new System.Drawing.Point(12, 180);
            this.startButton.Name = "startButton";
            this.startButton.Size = new System.Drawing.Size(158, 23);
            this.startButton.TabIndex = 1;
            this.startButton.Text = "Start/Resume Crawling";
            this.startButton.UseVisualStyleBackColor = true;
            this.startButton.Click += new System.EventHandler(this.LoadButton_Click);
            // 
            // textWindow
            // 
            this.textWindow.Location = new System.Drawing.Point(187, 35);
            this.textWindow.Name = "textWindow";
            this.textWindow.ReadOnly = true;
            this.textWindow.Size = new System.Drawing.Size(506, 442);
            this.textWindow.TabIndex = 2;
            this.textWindow.Text = "";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(705, 13);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(67, 13);
            this.label1.TabIndex = 5;
            this.label1.Text = "Topic Status";
            // 
            // UrlChecking
            // 
            this.UrlChecking.AutoSize = true;
            this.UrlChecking.Location = new System.Drawing.Point(184, 13);
            this.UrlChecking.Name = "UrlChecking";
            this.UrlChecking.Size = new System.Drawing.Size(20, 13);
            this.UrlChecking.TabIndex = 6;
            this.UrlChecking.Text = "Url";
            // 
            // downloadSeedButton
            // 
            this.downloadSeedButton.Location = new System.Drawing.Point(12, 122);
            this.downloadSeedButton.Name = "downloadSeedButton";
            this.downloadSeedButton.Size = new System.Drawing.Size(158, 23);
            this.downloadSeedButton.TabIndex = 7;
            this.downloadSeedButton.Text = "Download Seed Documents";
            this.downloadSeedButton.UseVisualStyleBackColor = true;
            this.downloadSeedButton.Click += new System.EventHandler(this.DownloadSeedButton_Click);
            // 
            // threadProgressText
            // 
            this.threadProgressText.Location = new System.Drawing.Point(699, 64);
            this.threadProgressText.Name = "threadProgressText";
            this.threadProgressText.ReadOnly = true;
            this.threadProgressText.Size = new System.Drawing.Size(393, 413);
            this.threadProgressText.TabIndex = 8;
            this.threadProgressText.Text = "";
            // 
            // editTopicsButton
            // 
            this.editTopicsButton.Location = new System.Drawing.Point(12, 35);
            this.editTopicsButton.Name = "editTopicsButton";
            this.editTopicsButton.Size = new System.Drawing.Size(158, 23);
            this.editTopicsButton.TabIndex = 9;
            this.editTopicsButton.Text = "Edit topics";
            this.editTopicsButton.UseVisualStyleBackColor = true;
            this.editTopicsButton.Click += new System.EventHandler(this.EditTopicsButton_Click);
            // 
            // generateTrainingSetButton
            // 
            this.generateTrainingSetButton.Location = new System.Drawing.Point(12, 93);
            this.generateTrainingSetButton.Name = "generateTrainingSetButton";
            this.generateTrainingSetButton.Size = new System.Drawing.Size(158, 23);
            this.generateTrainingSetButton.TabIndex = 10;
            this.generateTrainingSetButton.Text = "Generate training sets";
            this.generateTrainingSetButton.UseVisualStyleBackColor = true;
            this.generateTrainingSetButton.Click += new System.EventHandler(this.GenerateTrainingSetButton_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(705, 40);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(196, 13);
            this.label2.TabIndex = 11;
            this.label2.Text = "Number of pages to download per topic:";
            // 
            // pagesPerTopicNUD
            // 
            this.pagesPerTopicNUD.Location = new System.Drawing.Point(907, 36);
            this.pagesPerTopicNUD.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.pagesPerTopicNUD.Name = "pagesPerTopicNUD";
            this.pagesPerTopicNUD.Size = new System.Drawing.Size(120, 20);
            this.pagesPerTopicNUD.TabIndex = 12;
            this.pagesPerTopicNUD.Value = new decimal(new int[] {
            500,
            0,
            0,
            0});
            // 
            // editBlacklistButton
            // 
            this.editBlacklistButton.Location = new System.Drawing.Point(12, 64);
            this.editBlacklistButton.Name = "editBlacklistButton";
            this.editBlacklistButton.Size = new System.Drawing.Size(158, 23);
            this.editBlacklistButton.TabIndex = 13;
            this.editBlacklistButton.Text = "Edit blacklist";
            this.editBlacklistButton.UseVisualStyleBackColor = true;
            this.editBlacklistButton.Click += new System.EventHandler(this.EditBlacklistButton_Click);
            // 
            // trainTestButton
            // 
            this.trainTestButton.Location = new System.Drawing.Point(12, 151);
            this.trainTestButton.Name = "trainTestButton";
            this.trainTestButton.Size = new System.Drawing.Size(158, 23);
            this.trainTestButton.TabIndex = 14;
            this.trainTestButton.Text = "Train + Test (optional)";
            this.trainTestButton.UseVisualStyleBackColor = true;
            this.trainTestButton.Click += new System.EventHandler(this.TrainTestButton_Click);
            // 
            // ACrawlerWin
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1101, 485);
            this.Controls.Add(this.trainTestButton);
            this.Controls.Add(this.editBlacklistButton);
            this.Controls.Add(this.pagesPerTopicNUD);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.generateTrainingSetButton);
            this.Controls.Add(this.editTopicsButton);
            this.Controls.Add(this.threadProgressText);
            this.Controls.Add(this.downloadSeedButton);
            this.Controls.Add(this.UrlChecking);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textWindow);
            this.Controls.Add(this.startButton);
            this.Name = "ACrawlerWin";
            this.Text = "ACrawler";
            ((System.ComponentModel.ISupportInitialize)(this.pagesPerTopicNUD)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button startButton;
        private System.Windows.Forms.RichTextBox textWindow;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label UrlChecking;
        private System.Windows.Forms.Button downloadSeedButton;
        private System.Windows.Forms.RichTextBox threadProgressText;
        private System.Windows.Forms.Button editTopicsButton;
        private System.Windows.Forms.Button generateTrainingSetButton;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.NumericUpDown pagesPerTopicNUD;
        private System.Windows.Forms.Button editBlacklistButton;
        private System.Windows.Forms.Button trainTestButton;
    }
}

