namespace DDR4_TestingApp
{
    partial class Launcher
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Launcher));
            groupBox1 = new GroupBox();
            groupBox4 = new GroupBox();
            CM_port = new TextBox();
            groupBox2 = new GroupBox();
            CM_ip = new TextBox();
            CM_connect = new Button();
            groupBox3 = new GroupBox();
            label12 = new Label();
            pictureBox3 = new PictureBox();
            pictureBox2 = new PictureBox();
            pictureBox1 = new PictureBox();
            label19 = new Label();
            label3 = new Label();
            launchRunEditor = new Button();
            groupBox5 = new GroupBox();
            groupBox7 = new GroupBox();
            idleStatus = new Button();
            abortButton = new Button();
            queueBox = new GroupBox();
            queueSize = new Label();
            groupBox6 = new GroupBox();
            taskName = new Label();
            taskProgress = new ProgressBar();
            configEditor = new Button();
            launchRecordManager = new Button();
            groupBox1.SuspendLayout();
            groupBox4.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox3.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox3).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).BeginInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            groupBox5.SuspendLayout();
            groupBox7.SuspendLayout();
            queueBox.SuspendLayout();
            groupBox6.SuspendLayout();
            SuspendLayout();
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(groupBox4);
            groupBox1.Controls.Add(groupBox2);
            groupBox1.Controls.Add(CM_connect);
            groupBox1.Location = new Point(297, 240);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(214, 198);
            groupBox1.TabIndex = 1;
            groupBox1.TabStop = false;
            groupBox1.Text = "ZCU Server Connection";
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(CM_port);
            groupBox4.Location = new Point(129, 27);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(79, 59);
            groupBox4.TabIndex = 5;
            groupBox4.TabStop = false;
            groupBox4.Text = "Port";
            // 
            // CM_port
            // 
            CM_port.Location = new Point(11, 22);
            CM_port.Name = "CM_port";
            CM_port.Size = new Size(57, 23);
            CM_port.TabIndex = 1;
            CM_port.Text = "8080";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(CM_ip);
            groupBox2.Location = new Point(6, 27);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(117, 59);
            groupBox2.TabIndex = 4;
            groupBox2.TabStop = false;
            groupBox2.Text = "IP Address";
            // 
            // CM_ip
            // 
            CM_ip.Location = new Point(11, 22);
            CM_ip.Name = "CM_ip";
            CM_ip.Size = new Size(97, 23);
            CM_ip.TabIndex = 1;
            CM_ip.Text = "127.0.0.1";
            // 
            // CM_connect
            // 
            CM_connect.Location = new Point(6, 92);
            CM_connect.Name = "CM_connect";
            CM_connect.Size = new Size(202, 43);
            CM_connect.TabIndex = 1;
            CM_connect.Text = "Connect";
            CM_connect.UseVisualStyleBackColor = true;
            CM_connect.Click += CM_connect_Click;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(label12);
            groupBox3.Controls.Add(pictureBox3);
            groupBox3.Controls.Add(pictureBox2);
            groupBox3.Controls.Add(pictureBox1);
            groupBox3.Controls.Add(label19);
            groupBox3.Controls.Add(label3);
            groupBox3.Location = new Point(517, 240);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(271, 198);
            groupBox3.TabIndex = 2;
            groupBox3.TabStop = false;
            groupBox3.Text = "About";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(36, 92);
            label12.Name = "label12";
            label12.Size = new Size(113, 15);
            label12.TabIndex = 12;
            label12.Text = "ryan.lopez@pitt.edu";
            // 
            // pictureBox3
            // 
            pictureBox3.Image = (Image)resources.GetObject("pictureBox3.Image");
            pictureBox3.Location = new Point(201, 126);
            pictureBox3.Name = "pictureBox3";
            pictureBox3.Size = new Size(63, 66);
            pictureBox3.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox3.TabIndex = 11;
            pictureBox3.TabStop = false;
            // 
            // pictureBox2
            // 
            pictureBox2.Image = (Image)resources.GetObject("pictureBox2.Image");
            pictureBox2.Location = new Point(201, 59);
            pictureBox2.Name = "pictureBox2";
            pictureBox2.Size = new Size(63, 61);
            pictureBox2.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox2.TabIndex = 10;
            pictureBox2.TabStop = false;
            // 
            // pictureBox1
            // 
            pictureBox1.Image = (Image)resources.GetObject("pictureBox1.Image");
            pictureBox1.Location = new Point(9, 137);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(190, 55);
            pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox1.TabIndex = 10;
            pictureBox1.TabStop = false;
            // 
            // label19
            // 
            label19.AutoSize = true;
            label19.Location = new Point(36, 71);
            label19.Name = "label19";
            label19.Size = new Size(125, 15);
            label19.TabIndex = 9;
            label19.Text = "Written by Ryan Lopez";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label3.Location = new Point(53, 25);
            label3.Name = "label3";
            label3.Size = new Size(193, 21);
            label3.TabIndex = 8;
            label3.Text = "DDR4 Test Bench Software";
            // 
            // launchRunEditor
            // 
            launchRunEditor.Location = new Point(18, 12);
            launchRunEditor.Name = "launchRunEditor";
            launchRunEditor.Size = new Size(144, 104);
            launchRunEditor.TabIndex = 3;
            launchRunEditor.Text = "Lunch Run Editor";
            launchRunEditor.UseVisualStyleBackColor = true;
            launchRunEditor.Click += launchRunEditor_Click;
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(groupBox7);
            groupBox5.Controls.Add(queueBox);
            groupBox5.Controls.Add(groupBox6);
            groupBox5.Location = new Point(12, 240);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(279, 198);
            groupBox5.TabIndex = 13;
            groupBox5.TabStop = false;
            groupBox5.Text = "Task Engine";
            // 
            // groupBox7
            // 
            groupBox7.Controls.Add(idleStatus);
            groupBox7.Controls.Add(abortButton);
            groupBox7.Location = new Point(98, 28);
            groupBox7.Name = "groupBox7";
            groupBox7.Size = new Size(172, 79);
            groupBox7.TabIndex = 16;
            groupBox7.TabStop = false;
            groupBox7.Text = "Processing State";
            // 
            // idleStatus
            // 
            idleStatus.Location = new Point(80, 22);
            idleStatus.Name = "idleStatus";
            idleStatus.Size = new Size(86, 47);
            idleStatus.TabIndex = 16;
            idleStatus.Text = "IDLE";
            idleStatus.UseVisualStyleBackColor = false;
            // 
            // abortButton
            // 
            abortButton.Location = new Point(6, 22);
            abortButton.Name = "abortButton";
            abortButton.Size = new Size(68, 47);
            abortButton.TabIndex = 17;
            abortButton.Text = "IDLE";
            abortButton.UseVisualStyleBackColor = true;
            // 
            // queueBox
            // 
            queueBox.Controls.Add(queueSize);
            queueBox.Location = new Point(6, 28);
            queueBox.Name = "queueBox";
            queueBox.Size = new Size(86, 79);
            queueBox.TabIndex = 15;
            queueBox.TabStop = false;
            queueBox.Text = "Queue";
            // 
            // queueSize
            // 
            queueSize.AutoSize = true;
            queueSize.Location = new Point(18, 34);
            queueSize.Name = "queueSize";
            queueSize.Size = new Size(44, 15);
            queueSize.TabIndex = 3;
            queueSize.Text = "0 Tasks";
            // 
            // groupBox6
            // 
            groupBox6.Controls.Add(taskName);
            groupBox6.Controls.Add(taskProgress);
            groupBox6.Location = new Point(6, 113);
            groupBox6.Name = "groupBox6";
            groupBox6.Size = new Size(264, 79);
            groupBox6.TabIndex = 14;
            groupBox6.TabStop = false;
            groupBox6.Text = "Current Task";
            // 
            // taskName
            // 
            taskName.AutoSize = true;
            taskName.Location = new Point(6, 24);
            taskName.Name = "taskName";
            taskName.Size = new Size(38, 15);
            taskName.TabIndex = 2;
            taskName.Text = "label2";
            // 
            // taskProgress
            // 
            taskProgress.Location = new Point(6, 44);
            taskProgress.Name = "taskProgress";
            taskProgress.Size = new Size(252, 23);
            taskProgress.TabIndex = 1;
            // 
            // configEditor
            // 
            configEditor.Location = new Point(168, 12);
            configEditor.Name = "configEditor";
            configEditor.Size = new Size(144, 104);
            configEditor.TabIndex = 14;
            configEditor.Text = "Config Editor";
            configEditor.UseVisualStyleBackColor = true;
            configEditor.Click += configEditor_Click;
            // 
            // launchRecordManager
            // 
            launchRecordManager.Location = new Point(318, 12);
            launchRecordManager.Name = "launchRecordManager";
            launchRecordManager.Size = new Size(144, 104);
            launchRecordManager.TabIndex = 15;
            launchRecordManager.Text = "Record Manager";
            launchRecordManager.UseVisualStyleBackColor = true;
            launchRecordManager.Click += launchRecordManager_Click;
            // 
            // Launcher
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(launchRecordManager);
            Controls.Add(configEditor);
            Controls.Add(groupBox5);
            Controls.Add(launchRunEditor);
            Controls.Add(groupBox1);
            Controls.Add(groupBox3);
            Name = "Launcher";
            Text = "Launcher";
            groupBox1.ResumeLayout(false);
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)pictureBox3).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).EndInit();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            groupBox5.ResumeLayout(false);
            groupBox7.ResumeLayout(false);
            queueBox.ResumeLayout(false);
            queueBox.PerformLayout();
            groupBox6.ResumeLayout(false);
            groupBox6.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private GroupBox groupBox1;
        private GroupBox groupBox4;
        private TextBox CM_port;
        private GroupBox groupBox2;
        private TextBox CM_ip;
        private Button CM_connect;
        private GroupBox groupBox3;
        private Label label12;
        private PictureBox pictureBox3;
        private PictureBox pictureBox2;
        private PictureBox pictureBox1;
        private Label label19;
        private Label label3;
        private Button launchRunEditor;
        private GroupBox groupBox5;
        private GroupBox groupBox6;
        private Label taskName;
        private ProgressBar taskProgress;
        private Button idleStatus;
        private GroupBox queueBox;
        private GroupBox groupBox7;
        private Button abortButton;
        private Label queueSize;
        private Button configEditor;
        private Button launchRecordManager;
    }
}