namespace DDR4_TestingApp
{
    partial class RecordManager
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
            label1 = new Label();
            groupBox2 = new GroupBox();
            allRecords = new TreeView();
            groupBox1 = new GroupBox();
            requestVerify = new Button();
            requestWrite = new Button();
            requestRead = new Button();
            requestDynamic = new Button();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox1.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 0);
            splitContainer1.Name = "splitContainer1";
            splitContainer1.Orientation = Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.BackColor = SystemColors.AppWorkspace;
            splitContainer1.Panel1.Controls.Add(label1);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(groupBox2);
            splitContainer1.Panel2.Controls.Add(groupBox1);
            splitContainer1.Panel2.Paint += splitContainer1_Panel2_Paint;
            splitContainer1.Size = new Size(464, 497);
            splitContainer1.SplitterDistance = 45;
            splitContainer1.TabIndex = 0;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            label1.ForeColor = Color.White;
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(160, 25);
            label1.TabIndex = 0;
            label1.Text = "Record Manager";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(allRecords);
            groupBox2.Location = new Point(12, 80);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(441, 356);
            groupBox2.TabIndex = 2;
            groupBox2.TabStop = false;
            groupBox2.Text = "All Records";
            // 
            // allRecords
            // 
            allRecords.Location = new Point(6, 22);
            allRecords.Name = "allRecords";
            allRecords.Size = new Size(429, 328);
            allRecords.TabIndex = 0;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(requestVerify);
            groupBox1.Controls.Add(requestWrite);
            groupBox1.Controls.Add(requestRead);
            groupBox1.Controls.Add(requestDynamic);
            groupBox1.Location = new Point(12, 3);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(441, 71);
            groupBox1.TabIndex = 1;
            groupBox1.TabStop = false;
            groupBox1.Text = "Manual Requests";
            // 
            // requestVerify
            // 
            requestVerify.Location = new Point(326, 22);
            requestVerify.Name = "requestVerify";
            requestVerify.Size = new Size(97, 43);
            requestVerify.TabIndex = 3;
            requestVerify.Text = "Verify";
            requestVerify.UseVisualStyleBackColor = true;
            // 
            // requestWrite
            // 
            requestWrite.Location = new Point(223, 22);
            requestWrite.Name = "requestWrite";
            requestWrite.Size = new Size(97, 43);
            requestWrite.TabIndex = 2;
            requestWrite.Text = "Write";
            requestWrite.UseVisualStyleBackColor = true;
            // 
            // requestRead
            // 
            requestRead.Location = new Point(120, 22);
            requestRead.Name = "requestRead";
            requestRead.Size = new Size(97, 43);
            requestRead.TabIndex = 1;
            requestRead.Text = "Read";
            requestRead.UseVisualStyleBackColor = true;
            // 
            // requestDynamic
            // 
            requestDynamic.Location = new Point(17, 22);
            requestDynamic.Name = "requestDynamic";
            requestDynamic.Size = new Size(97, 43);
            requestDynamic.TabIndex = 0;
            requestDynamic.Text = "Dynamic";
            requestDynamic.UseVisualStyleBackColor = true;
            // 
            // RecordManager
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(464, 497);
            Controls.Add(splitContainer1);
            Name = "RecordManager";
            Text = "RecordManager";
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel1.PerformLayout();
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            groupBox2.ResumeLayout(false);
            groupBox1.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Splitter splitter1;
        private SplitContainer splitContainer1;
        private Label label1;
        private GroupBox groupBox2;
        private TreeView allRecords;
        private GroupBox groupBox1;
        private Button requestVerify;
        private Button requestWrite;
        private Button requestRead;
        private Button requestDynamic;
    }
}