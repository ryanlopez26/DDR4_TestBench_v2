namespace DDR4_TestingApp
{
    partial class ConfigEditor
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
            groupBox45 = new GroupBox();
            blockFactor = new ComboBox();
            groupBox49 = new GroupBox();
            samplingInfo = new RichTextBox();
            label5 = new Label();
            groupBox6 = new GroupBox();
            addrInfo = new RichTextBox();
            blockSize = new ComboBox();
            label6 = new Label();
            selectionSize = new ComboBox();
            label7 = new Label();
            groupBox1 = new GroupBox();
            groupBox2 = new GroupBox();
            groupBox3 = new GroupBox();
            dataPattern = new ComboBox();
            dataSeed = new TextBox();
            regenSeed = new Button();
            groupBox4 = new GroupBox();
            groupBox7 = new GroupBox();
            flashFPGA = new Button();
            fpgaImages = new ComboBox();
            groupBox8 = new GroupBox();
            currentImage = new TextBox();
            groupBox5 = new GroupBox();
            sefiThresholdSlider = new TrackBar();
            sefiThreshold = new TextBox();
            groupBox9 = new GroupBox();
            enableAutoSEFIMode = new CheckBox();
            sendConfig = new Button();
            groupBox10 = new GroupBox();
            syncStatus = new Button();
            groupBox11 = new GroupBox();
            lastSync = new TextBox();
            groupBox45.SuspendLayout();
            groupBox49.SuspendLayout();
            groupBox6.SuspendLayout();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox4.SuspendLayout();
            groupBox7.SuspendLayout();
            groupBox8.SuspendLayout();
            groupBox5.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)sefiThresholdSlider).BeginInit();
            groupBox9.SuspendLayout();
            groupBox10.SuspendLayout();
            groupBox11.SuspendLayout();
            SuspendLayout();
            // 
            // groupBox45
            // 
            groupBox45.Controls.Add(blockFactor);
            groupBox45.Controls.Add(groupBox49);
            groupBox45.Controls.Add(label5);
            groupBox45.Controls.Add(blockSize);
            groupBox45.Controls.Add(label6);
            groupBox45.Controls.Add(selectionSize);
            groupBox45.Controls.Add(label7);
            groupBox45.Location = new Point(12, 12);
            groupBox45.Name = "groupBox45";
            groupBox45.Size = new Size(399, 329);
            groupBox45.TabIndex = 7;
            groupBox45.TabStop = false;
            groupBox45.Text = "Memory Sampling";
            // 
            // blockFactor
            // 
            blockFactor.FormattingEnabled = true;
            blockFactor.Items.AddRange(new object[] { "1", "2", "4", "8", "16", "32", "64", "128" });
            blockFactor.Location = new Point(115, 85);
            blockFactor.Name = "blockFactor";
            blockFactor.Size = new Size(76, 23);
            blockFactor.TabIndex = 29;
            // 
            // groupBox49
            // 
            groupBox49.Controls.Add(samplingInfo);
            groupBox49.Location = new Point(5, 114);
            groupBox49.Name = "groupBox49";
            groupBox49.Size = new Size(387, 204);
            groupBox49.TabIndex = 28;
            groupBox49.TabStop = false;
            groupBox49.Text = "Sampling Information";
            // 
            // samplingInfo
            // 
            samplingInfo.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            samplingInfo.Location = new Point(6, 22);
            samplingInfo.Name = "samplingInfo";
            samplingInfo.Size = new Size(375, 176);
            samplingInfo.TabIndex = 1;
            samplingInfo.Text = "";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(11, 88);
            label5.Name = "label5";
            label5.Size = new Size(75, 15);
            label5.TabIndex = 26;
            label5.Text = "Block Factor:";
            // 
            // groupBox6
            // 
            groupBox6.Controls.Add(addrInfo);
            groupBox6.Location = new Point(12, 347);
            groupBox6.Name = "groupBox6";
            groupBox6.Size = new Size(399, 140);
            groupBox6.TabIndex = 5;
            groupBox6.TabStop = false;
            groupBox6.Text = "Address Information";
            // 
            // addrInfo
            // 
            addrInfo.Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point, 0);
            addrInfo.Location = new Point(10, 19);
            addrInfo.Name = "addrInfo";
            addrInfo.Size = new Size(376, 115);
            addrInfo.TabIndex = 2;
            addrInfo.Text = "";
            // 
            // blockSize
            // 
            blockSize.FormattingEnabled = true;
            blockSize.Items.AddRange(new object[] { "4 KB", "64 KB", "1 MB", "16 MB", "256 MB", "512 MB", "1 GB" });
            blockSize.Location = new Point(115, 52);
            blockSize.Name = "blockSize";
            blockSize.Size = new Size(76, 23);
            blockSize.TabIndex = 25;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(12, 55);
            label6.Name = "label6";
            label6.Size = new Size(62, 15);
            label6.TabIndex = 24;
            label6.Text = "Block Size:";
            // 
            // selectionSize
            // 
            selectionSize.FormattingEnabled = true;
            selectionSize.Items.AddRange(new object[] { "4 KB", "64 KB", "1 MB", "16 MB", "256 MB", "512 MB", "1 GB" });
            selectionSize.Location = new Point(115, 20);
            selectionSize.Name = "selectionSize";
            selectionSize.Size = new Size(76, 23);
            selectionSize.TabIndex = 23;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(12, 23);
            label7.Name = "label7";
            label7.Size = new Size(81, 15);
            label7.TabIndex = 22;
            label7.Text = "Selection Size:";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(groupBox3);
            groupBox1.Controls.Add(groupBox2);
            groupBox1.Location = new Point(417, 12);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(371, 88);
            groupBox1.TabIndex = 30;
            groupBox1.TabStop = false;
            groupBox1.Text = "Data Setup";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(regenSeed);
            groupBox2.Controls.Add(dataSeed);
            groupBox2.Location = new Point(143, 20);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(222, 55);
            groupBox2.TabIndex = 31;
            groupBox2.TabStop = false;
            groupBox2.Text = "Seed";
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(dataPattern);
            groupBox3.Location = new Point(6, 20);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(131, 55);
            groupBox3.TabIndex = 32;
            groupBox3.TabStop = false;
            groupBox3.Text = "Pattern";
            // 
            // dataPattern
            // 
            dataPattern.FormattingEnabled = true;
            dataPattern.Items.AddRange(new object[] { "ONES", "ZEROS", "PRNG" });
            dataPattern.Location = new Point(6, 22);
            dataPattern.Name = "dataPattern";
            dataPattern.Size = new Size(118, 23);
            dataPattern.TabIndex = 30;
            // 
            // dataSeed
            // 
            dataSeed.Location = new Point(6, 22);
            dataSeed.Name = "dataSeed";
            dataSeed.Size = new Size(98, 23);
            dataSeed.TabIndex = 32;
            dataSeed.Text = "00000000";
            // 
            // regenSeed
            // 
            regenSeed.Font = new Font("Segoe UI", 9F);
            regenSeed.Location = new Point(110, 19);
            regenSeed.Name = "regenSeed";
            regenSeed.Size = new Size(106, 27);
            regenSeed.TabIndex = 31;
            regenSeed.Text = "Regenerate";
            regenSeed.UseVisualStyleBackColor = true;
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(groupBox8);
            groupBox4.Controls.Add(groupBox7);
            groupBox4.Location = new Point(417, 106);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(371, 147);
            groupBox4.TabIndex = 33;
            groupBox4.TabStop = false;
            groupBox4.Text = "FPGA";
            // 
            // groupBox7
            // 
            groupBox7.Controls.Add(flashFPGA);
            groupBox7.Controls.Add(fpgaImages);
            groupBox7.Location = new Point(12, 20);
            groupBox7.Name = "groupBox7";
            groupBox7.Size = new Size(353, 55);
            groupBox7.TabIndex = 31;
            groupBox7.TabStop = false;
            groupBox7.Text = "Selected Image";
            // 
            // flashFPGA
            // 
            flashFPGA.Font = new Font("Segoe UI", 9F);
            flashFPGA.Location = new Point(257, 17);
            flashFPGA.Name = "flashFPGA";
            flashFPGA.Size = new Size(90, 27);
            flashFPGA.TabIndex = 31;
            flashFPGA.Text = "Flash";
            flashFPGA.UseVisualStyleBackColor = true;
            // 
            // fpgaImages
            // 
            fpgaImages.FormattingEnabled = true;
            fpgaImages.Items.AddRange(new object[] { "Image1", "Image2", "Image3", "Image4" });
            fpgaImages.Location = new Point(7, 21);
            fpgaImages.Name = "fpgaImages";
            fpgaImages.Size = new Size(244, 23);
            fpgaImages.TabIndex = 31;
            // 
            // groupBox8
            // 
            groupBox8.Controls.Add(currentImage);
            groupBox8.Location = new Point(12, 81);
            groupBox8.Name = "groupBox8";
            groupBox8.Size = new Size(353, 55);
            groupBox8.TabIndex = 32;
            groupBox8.TabStop = false;
            groupBox8.Text = "Currently Loaded Image";
            // 
            // currentImage
            // 
            currentImage.Location = new Point(6, 22);
            currentImage.Name = "currentImage";
            currentImage.ReadOnly = true;
            currentImage.Size = new Size(341, 23);
            currentImage.TabIndex = 33;
            currentImage.Text = "None";
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(enableAutoSEFIMode);
            groupBox5.Controls.Add(sefiThreshold);
            groupBox5.Controls.Add(sefiThresholdSlider);
            groupBox5.Location = new Point(417, 259);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(371, 88);
            groupBox5.TabIndex = 33;
            groupBox5.TabStop = false;
            groupBox5.Text = "Dynamic Test";
            // 
            // sefiThresholdSlider
            // 
            sefiThresholdSlider.Location = new Point(12, 22);
            sefiThresholdSlider.Name = "sefiThresholdSlider";
            sefiThresholdSlider.Size = new Size(249, 45);
            sefiThresholdSlider.TabIndex = 0;
            // 
            // sefiThreshold
            // 
            sefiThreshold.Location = new Point(267, 22);
            sefiThreshold.Name = "sefiThreshold";
            sefiThreshold.Size = new Size(98, 23);
            sefiThreshold.TabIndex = 33;
            sefiThreshold.Text = "0.0";
            sefiThreshold.TextChanged += sefiThreshold_TextChanged;
            // 
            // groupBox9
            // 
            groupBox9.Controls.Add(groupBox11);
            groupBox9.Controls.Add(groupBox10);
            groupBox9.Controls.Add(sendConfig);
            groupBox9.Location = new Point(417, 353);
            groupBox9.Name = "groupBox9";
            groupBox9.Size = new Size(377, 134);
            groupBox9.TabIndex = 34;
            groupBox9.TabStop = false;
            groupBox9.Text = "Config Manager";
            // 
            // enableAutoSEFIMode
            // 
            enableAutoSEFIMode.AutoSize = true;
            enableAutoSEFIMode.Location = new Point(273, 52);
            enableAutoSEFIMode.Name = "enableAutoSEFIMode";
            enableAutoSEFIMode.Size = new Size(88, 19);
            enableAutoSEFIMode.TabIndex = 34;
            enableAutoSEFIMode.Text = "AUTO Reset";
            enableAutoSEFIMode.UseVisualStyleBackColor = true;
            // 
            // sendConfig
            // 
            sendConfig.Location = new Point(6, 87);
            sendConfig.Name = "sendConfig";
            sendConfig.Size = new Size(365, 41);
            sendConfig.TabIndex = 0;
            sendConfig.Text = "Update Configuration";
            sendConfig.UseVisualStyleBackColor = true;
            // 
            // groupBox10
            // 
            groupBox10.Controls.Add(syncStatus);
            groupBox10.Location = new Point(6, 26);
            groupBox10.Name = "groupBox10";
            groupBox10.Size = new Size(131, 55);
            groupBox10.TabIndex = 33;
            groupBox10.TabStop = false;
            groupBox10.Text = "Sync Status";
            // 
            // syncStatus
            // 
            syncStatus.Enabled = false;
            syncStatus.Location = new Point(6, 22);
            syncStatus.Name = "syncStatus";
            syncStatus.Size = new Size(118, 23);
            syncStatus.TabIndex = 0;
            syncStatus.Text = "OUT OF DATE";
            syncStatus.UseVisualStyleBackColor = true;
            // 
            // groupBox11
            // 
            groupBox11.Controls.Add(lastSync);
            groupBox11.Location = new Point(143, 26);
            groupBox11.Name = "groupBox11";
            groupBox11.Size = new Size(228, 55);
            groupBox11.TabIndex = 34;
            groupBox11.TabStop = false;
            groupBox11.Text = "Last Sync";
            // 
            // lastSync
            // 
            lastSync.Location = new Point(8, 22);
            lastSync.Name = "lastSync";
            lastSync.ReadOnly = true;
            lastSync.Size = new Size(210, 23);
            lastSync.TabIndex = 35;
            lastSync.Text = "None";
            // 
            // ConfigEditor
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 497);
            Controls.Add(groupBox9);
            Controls.Add(groupBox5);
            Controls.Add(groupBox4);
            Controls.Add(groupBox1);
            Controls.Add(groupBox45);
            Controls.Add(groupBox6);
            Name = "ConfigEditor";
            Text = "ConfigEditor";
            groupBox45.ResumeLayout(false);
            groupBox45.PerformLayout();
            groupBox49.ResumeLayout(false);
            groupBox6.ResumeLayout(false);
            groupBox1.ResumeLayout(false);
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox4.ResumeLayout(false);
            groupBox7.ResumeLayout(false);
            groupBox8.ResumeLayout(false);
            groupBox8.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox5.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)sefiThresholdSlider).EndInit();
            groupBox9.ResumeLayout(false);
            groupBox10.ResumeLayout(false);
            groupBox11.ResumeLayout(false);
            groupBox11.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private GroupBox groupBox45;
        private ComboBox blockFactor;
        private GroupBox groupBox49;
        private RichTextBox samplingInfo;
        private Label label5;
        private ComboBox blockSize;
        private Label label6;
        private ComboBox selectionSize;
        private Label label7;
        private GroupBox groupBox6;
        private RichTextBox addrInfo;
        private GroupBox groupBox1;
        private GroupBox groupBox3;
        private ComboBox dataPattern;
        private GroupBox groupBox2;
        private Button regenSeed;
        private TextBox dataSeed;
        private GroupBox groupBox4;
        private ComboBox comboBox1;
        private GroupBox groupBox7;
        private Button flashFPGA;
        private GroupBox groupBox8;
        private TextBox currentImage;
        private ComboBox fpgaImages;
        private GroupBox groupBox5;
        private TextBox sefiThreshold;
        private TrackBar sefiThresholdSlider;
        private CheckBox enableAutoSEFIMode;
        private GroupBox groupBox9;
        private GroupBox groupBox11;
        private TextBox lastSync;
        private GroupBox groupBox10;
        private Button syncStatus;
        private Button sendConfig;
    }
}