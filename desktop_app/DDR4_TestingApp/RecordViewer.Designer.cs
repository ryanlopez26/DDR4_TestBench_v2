namespace DDR4_TestingApp
{
    partial class RecordViewer
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
            splitContainer1 = new SplitContainer();
            panel1 = new Panel();
            recordID = new Label();
            groupBox1 = new GroupBox();
            endTime = new TextBox();
            groupBox2 = new GroupBox();
            startTime = new TextBox();
            groupBox3 = new GroupBox();
            groupBox4 = new GroupBox();
            duration = new TextBox();
            conclusion_status = new Button();
            groupBox5 = new GroupBox();
            groupBox7 = new GroupBox();
            err_bins_plot = new ScottPlot.WinForms.FormsPlot();
            groupBox6 = new GroupBox();
            adj_err_bins_plot = new ScottPlot.WinForms.FormsPlot();
            groupBox8 = new GroupBox();
            log_plot = new ScottPlot.WinForms.FormsPlot();
            groupBox9 = new GroupBox();
            groupBox11 = new GroupBox();
            correct_bits = new TextBox();
            groupBox12 = new GroupBox();
            incorrect_bits = new TextBox();
            groupBox10 = new GroupBox();
            processed_bits = new TextBox();
            groupBox13 = new GroupBox();
            err_bins_total = new TextBox();
            groupBox14 = new GroupBox();
            groupBox17 = new GroupBox();
            time_to_sefi = new TextBox();
            groupBox15 = new GroupBox();
            exposure_time = new TextBox();
            groupBox16 = new GroupBox();
            requestDeletion = new Button();
            downloadCapture = new Button();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            panel1.SuspendLayout();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox4.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBox7.SuspendLayout();
            groupBox6.SuspendLayout();
            groupBox8.SuspendLayout();
            groupBox9.SuspendLayout();
            groupBox11.SuspendLayout();
            groupBox12.SuspendLayout();
            groupBox10.SuspendLayout();
            groupBox13.SuspendLayout();
            groupBox14.SuspendLayout();
            groupBox17.SuspendLayout();
            groupBox15.SuspendLayout();
            groupBox16.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Enabled = false;
            splitContainer1.Location = new Point(0, 0);
            splitContainer1.Name = "splitContainer1";
            splitContainer1.Orientation = Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(conclusion_status);
            splitContainer1.Panel1.Controls.Add(panel1);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(groupBox16);
            splitContainer1.Panel2.Controls.Add(groupBox14);
            splitContainer1.Panel2.Controls.Add(groupBox9);
            splitContainer1.Panel2.Controls.Add(groupBox8);
            splitContainer1.Panel2.Controls.Add(groupBox5);
            splitContainer1.Panel2.Controls.Add(groupBox3);
            splitContainer1.Size = new Size(1047, 825);
            splitContainer1.SplitterDistance = 55;
            splitContainer1.TabIndex = 0;
            // 
            // panel1
            // 
            panel1.BackColor = SystemColors.InactiveCaption;
            panel1.Controls.Add(recordID);
            panel1.Location = new Point(6, 12);
            panel1.Name = "panel1";
            panel1.Size = new Size(847, 36);
            panel1.TabIndex = 0;
            // 
            // recordID
            // 
            recordID.AutoSize = true;
            recordID.Font = new Font("Segoe UI", 14F);
            recordID.Location = new Point(9, 6);
            recordID.Name = "recordID";
            recordID.Size = new Size(119, 25);
            recordID.TabIndex = 0;
            recordID.Text = "Record XXXX";
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(endTime);
            groupBox1.Location = new Point(203, 22);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(205, 54);
            groupBox1.TabIndex = 0;
            groupBox1.TabStop = false;
            groupBox1.Text = "Ended";
            // 
            // endTime
            // 
            endTime.Location = new Point(6, 22);
            endTime.Name = "endTime";
            endTime.Size = new Size(190, 23);
            endTime.TabIndex = 0;
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(startTime);
            groupBox2.Location = new Point(6, 22);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(191, 54);
            groupBox2.TabIndex = 1;
            groupBox2.TabStop = false;
            groupBox2.Text = "Started";
            // 
            // startTime
            // 
            startTime.Location = new Point(6, 22);
            startTime.Name = "startTime";
            startTime.Size = new Size(176, 23);
            startTime.TabIndex = 0;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(groupBox4);
            groupBox3.Controls.Add(groupBox2);
            groupBox3.Controls.Add(groupBox1);
            groupBox3.Location = new Point(12, 13);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(597, 85);
            groupBox3.TabIndex = 2;
            groupBox3.TabStop = false;
            groupBox3.Text = "Timestamps";
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(duration);
            groupBox4.Location = new Point(414, 22);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(153, 54);
            groupBox4.TabIndex = 1;
            groupBox4.TabStop = false;
            groupBox4.Text = "Duration";
            // 
            // duration
            // 
            duration.Location = new Point(6, 22);
            duration.Name = "duration";
            duration.Size = new Size(139, 23);
            duration.TabIndex = 0;
            // 
            // conclusion_status
            // 
            conclusion_status.BackColor = Color.FromArgb(128, 255, 128);
            conclusion_status.Enabled = false;
            conclusion_status.ForeColor = SystemColors.MenuText;
            conclusion_status.Location = new Point(859, 12);
            conclusion_status.Name = "conclusion_status";
            conclusion_status.Size = new Size(176, 36);
            conclusion_status.TabIndex = 1;
            conclusion_status.Text = "COMPLETED";
            conclusion_status.UseVisualStyleBackColor = false;
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(groupBox6);
            groupBox5.Controls.Add(groupBox7);
            groupBox5.Location = new Point(12, 104);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(597, 276);
            groupBox5.TabIndex = 3;
            groupBox5.TabStop = false;
            groupBox5.Text = "Error Distributions";
            // 
            // groupBox7
            // 
            groupBox7.Controls.Add(err_bins_plot);
            groupBox7.Location = new Point(6, 22);
            groupBox7.Name = "groupBox7";
            groupBox7.Size = new Size(288, 243);
            groupBox7.TabIndex = 1;
            groupBox7.TabStop = false;
            groupBox7.Text = "Bits per Byte";
            // 
            // err_bins_plot
            // 
            err_bins_plot.Location = new Point(6, 22);
            err_bins_plot.Name = "err_bins_plot";
            err_bins_plot.Size = new Size(276, 215);
            err_bins_plot.TabIndex = 2;
            // 
            // groupBox6
            // 
            groupBox6.Controls.Add(adj_err_bins_plot);
            groupBox6.Location = new Point(300, 22);
            groupBox6.Name = "groupBox6";
            groupBox6.Size = new Size(288, 243);
            groupBox6.TabIndex = 3;
            groupBox6.TabStop = false;
            groupBox6.Text = "Bit Groupings";
            // 
            // adj_err_bins_plot
            // 
            adj_err_bins_plot.Location = new Point(6, 22);
            adj_err_bins_plot.Name = "adj_err_bins_plot";
            adj_err_bins_plot.Size = new Size(276, 215);
            adj_err_bins_plot.TabIndex = 2;
            // 
            // groupBox8
            // 
            groupBox8.Controls.Add(log_plot);
            groupBox8.Location = new Point(12, 386);
            groupBox8.Name = "groupBox8";
            groupBox8.Size = new Size(1023, 368);
            groupBox8.TabIndex = 4;
            groupBox8.TabStop = false;
            groupBox8.Text = "Signals Log";
            // 
            // log_plot
            // 
            log_plot.Location = new Point(6, 22);
            log_plot.Name = "log_plot";
            log_plot.Size = new Size(1011, 340);
            log_plot.TabIndex = 2;
            // 
            // groupBox9
            // 
            groupBox9.Controls.Add(groupBox13);
            groupBox9.Controls.Add(groupBox10);
            groupBox9.Controls.Add(groupBox11);
            groupBox9.Controls.Add(groupBox12);
            groupBox9.Location = new Point(615, 139);
            groupBox9.Name = "groupBox9";
            groupBox9.Size = new Size(420, 146);
            groupBox9.TabIndex = 3;
            groupBox9.TabStop = false;
            groupBox9.Text = "Bit Totals";
            // 
            // groupBox11
            // 
            groupBox11.Controls.Add(correct_bits);
            groupBox11.Location = new Point(6, 22);
            groupBox11.Name = "groupBox11";
            groupBox11.Size = new Size(191, 54);
            groupBox11.TabIndex = 1;
            groupBox11.TabStop = false;
            groupBox11.Text = "Correct";
            // 
            // correct_bits
            // 
            correct_bits.Location = new Point(6, 22);
            correct_bits.Name = "correct_bits";
            correct_bits.Size = new Size(176, 23);
            correct_bits.TabIndex = 0;
            // 
            // groupBox12
            // 
            groupBox12.Controls.Add(incorrect_bits);
            groupBox12.Location = new Point(203, 22);
            groupBox12.Name = "groupBox12";
            groupBox12.Size = new Size(205, 54);
            groupBox12.TabIndex = 0;
            groupBox12.TabStop = false;
            groupBox12.Text = "Incorrect";
            // 
            // incorrect_bits
            // 
            incorrect_bits.Location = new Point(6, 22);
            incorrect_bits.Name = "incorrect_bits";
            incorrect_bits.Size = new Size(190, 23);
            incorrect_bits.TabIndex = 0;
            // 
            // groupBox10
            // 
            groupBox10.Controls.Add(processed_bits);
            groupBox10.Location = new Point(6, 82);
            groupBox10.Name = "groupBox10";
            groupBox10.Size = new Size(191, 54);
            groupBox10.TabIndex = 2;
            groupBox10.TabStop = false;
            groupBox10.Text = "Processed";
            // 
            // processed_bits
            // 
            processed_bits.Location = new Point(6, 22);
            processed_bits.Name = "processed_bits";
            processed_bits.Size = new Size(176, 23);
            processed_bits.TabIndex = 0;
            // 
            // groupBox13
            // 
            groupBox13.Controls.Add(err_bins_total);
            groupBox13.Location = new Point(203, 82);
            groupBox13.Name = "groupBox13";
            groupBox13.Size = new Size(205, 54);
            groupBox13.TabIndex = 3;
            groupBox13.TabStop = false;
            groupBox13.Text = "Error Bin Total";
            // 
            // err_bins_total
            // 
            err_bins_total.Location = new Point(6, 22);
            err_bins_total.Name = "err_bins_total";
            err_bins_total.Size = new Size(190, 23);
            err_bins_total.TabIndex = 0;
            // 
            // groupBox14
            // 
            groupBox14.Controls.Add(groupBox15);
            groupBox14.Controls.Add(groupBox17);
            groupBox14.Location = new Point(615, 291);
            groupBox14.Name = "groupBox14";
            groupBox14.Size = new Size(420, 89);
            groupBox14.TabIndex = 4;
            groupBox14.TabStop = false;
            groupBox14.Text = "Beam Metrics";
            // 
            // groupBox17
            // 
            groupBox17.Controls.Add(time_to_sefi);
            groupBox17.Location = new Point(6, 22);
            groupBox17.Name = "groupBox17";
            groupBox17.Size = new Size(191, 54);
            groupBox17.TabIndex = 1;
            groupBox17.TabStop = false;
            groupBox17.Text = "Time to SEFI";
            // 
            // time_to_sefi
            // 
            time_to_sefi.Location = new Point(6, 22);
            time_to_sefi.Name = "time_to_sefi";
            time_to_sefi.Size = new Size(176, 23);
            time_to_sefi.TabIndex = 0;
            // 
            // groupBox15
            // 
            groupBox15.Controls.Add(exposure_time);
            groupBox15.Location = new Point(203, 22);
            groupBox15.Name = "groupBox15";
            groupBox15.Size = new Size(205, 54);
            groupBox15.TabIndex = 2;
            groupBox15.TabStop = false;
            groupBox15.Text = "Beam Time";
            // 
            // exposure_time
            // 
            exposure_time.Location = new Point(6, 22);
            exposure_time.Name = "exposure_time";
            exposure_time.Size = new Size(176, 23);
            exposure_time.TabIndex = 0;
            // 
            // groupBox16
            // 
            groupBox16.Controls.Add(downloadCapture);
            groupBox16.Controls.Add(requestDeletion);
            groupBox16.Location = new Point(615, 13);
            groupBox16.Name = "groupBox16";
            groupBox16.Size = new Size(420, 120);
            groupBox16.TabIndex = 3;
            groupBox16.TabStop = false;
            groupBox16.Text = "Record Management";
            // 
            // requestDeletion
            // 
            requestDeletion.Location = new Point(6, 22);
            requestDeletion.Name = "requestDeletion";
            requestDeletion.Size = new Size(402, 35);
            requestDeletion.TabIndex = 0;
            requestDeletion.Text = "Request Deletion";
            requestDeletion.UseVisualStyleBackColor = true;
            // 
            // downloadCapture
            // 
            downloadCapture.Location = new Point(6, 63);
            downloadCapture.Name = "downloadCapture";
            downloadCapture.Size = new Size(402, 34);
            downloadCapture.TabIndex = 1;
            downloadCapture.Text = "Download Capture";
            downloadCapture.UseVisualStyleBackColor = true;
            // 
            // RecordViewer
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1047, 825);
            Controls.Add(splitContainer1);
            Name = "RecordViewer";
            Text = "RecordViewer";
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox7.ResumeLayout(false);
            groupBox6.ResumeLayout(false);
            groupBox8.ResumeLayout(false);
            groupBox9.ResumeLayout(false);
            groupBox11.ResumeLayout(false);
            groupBox11.PerformLayout();
            groupBox12.ResumeLayout(false);
            groupBox12.PerformLayout();
            groupBox10.ResumeLayout(false);
            groupBox10.PerformLayout();
            groupBox13.ResumeLayout(false);
            groupBox13.PerformLayout();
            groupBox14.ResumeLayout(false);
            groupBox17.ResumeLayout(false);
            groupBox17.PerformLayout();
            groupBox15.ResumeLayout(false);
            groupBox15.PerformLayout();
            groupBox16.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private SplitContainer splitContainer1;
        private Panel panel1;
        private Label recordID;
        private GroupBox groupBox3;
        private GroupBox groupBox4;
        private TextBox duration;
        private GroupBox groupBox2;
        private TextBox startTime;
        private GroupBox groupBox1;
        private TextBox endTime;
        private Button conclusion_status;
        private GroupBox groupBox5;
        private GroupBox groupBox7;
        private GroupBox groupBox6;
        private ScottPlot.WinForms.FormsPlot adj_err_bins_plot;
        private ScottPlot.WinForms.FormsPlot err_bins_plot;
        private GroupBox groupBox9;
        private GroupBox groupBox11;
        private TextBox correct_bits;
        private GroupBox groupBox12;
        private TextBox incorrect_bits;
        private GroupBox groupBox8;
        private ScottPlot.WinForms.FormsPlot log_plot;
        private GroupBox groupBox14;
        private GroupBox groupBox15;
        private TextBox exposure_time;
        private GroupBox groupBox17;
        private TextBox time_to_sefi;
        private GroupBox groupBox13;
        private TextBox err_bins_total;
        private GroupBox groupBox10;
        private TextBox processed_bits;
        private GroupBox groupBox16;
        private Button requestDeletion;
        private Button downloadCapture;
    }
}