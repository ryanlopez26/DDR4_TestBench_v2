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
            groupBox17 = new GroupBox();
            socketStatus = new Label();
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
            groupBox8 = new GroupBox();
            groupBox9 = new GroupBox();
            psStatus = new Button();
            fpgaStatus = new Button();
            plStatus = new Button();
            uiStatus = new Button();
            calbStatus = new Button();
            groupBox10 = new GroupBox();
            groupBox16 = new GroupBox();
            powerStatus = new Button();
            groupBox13 = new GroupBox();
            groupBox15 = new GroupBox();
            relayID_power = new NumericUpDown();
            invertRelay_power = new CheckBox();
            groupBox14 = new GroupBox();
            restartDelay = new NumericUpDown();
            groupBox12 = new GroupBox();
            powerOn = new Button();
            powerAUTO = new Button();
            powerOFF = new Button();
            groupBox11 = new GroupBox();
            contactorStatus = new Button();
            groupBox18 = new GroupBox();
            servicesStatus = new Button();
            beamRequestInfo = new Button();
            beamActiveInfo = new Button();
            groupBox19 = new GroupBox();
            sdram3 = new Button();
            sdram2 = new Button();
            sdram1 = new Button();
            sdram0 = new Button();
            groupBox20 = new GroupBox();
            requestOff = new Button();
            requestReboot = new Button();
            groupBox21 = new GroupBox();
            groupBox24 = new GroupBox();
            beamRequestStatus = new Button();
            beamRelayStatus = new Button();
            groupBox23 = new GroupBox();
            relayID_beam = new NumericUpDown();
            invertRelay_beam = new CheckBox();
            groupBox22 = new GroupBox();
            beamOn = new Button();
            beamAUTO = new Button();
            beamOFF = new Button();
            groupBox25 = new GroupBox();
            pmicVolts = new Label();
            pmicAmps = new Label();
            pmicWatts = new Label();
            pmicNote = new Label();
            groupBox1.SuspendLayout();
            groupBox17.SuspendLayout();
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
            groupBox8.SuspendLayout();
            groupBox9.SuspendLayout();
            groupBox10.SuspendLayout();
            groupBox16.SuspendLayout();
            groupBox13.SuspendLayout();
            groupBox15.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)relayID_power).BeginInit();
            groupBox14.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)restartDelay).BeginInit();
            groupBox12.SuspendLayout();
            groupBox11.SuspendLayout();
            groupBox18.SuspendLayout();
            groupBox19.SuspendLayout();
            groupBox20.SuspendLayout();
            groupBox21.SuspendLayout();
            groupBox24.SuspendLayout();
            groupBox23.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)relayID_beam).BeginInit();
            groupBox22.SuspendLayout();
            groupBox25.SuspendLayout();
            SuspendLayout();
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(groupBox17);
            groupBox1.Controls.Add(groupBox4);
            groupBox1.Controls.Add(groupBox2);
            groupBox1.Controls.Add(CM_connect);
            groupBox1.Location = new Point(297, 302);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(224, 198);
            groupBox1.TabIndex = 1;
            groupBox1.TabStop = false;
            groupBox1.Text = "ZCU Server Connection";
            // 
            // groupBox17
            // 
            groupBox17.Controls.Add(socketStatus);
            groupBox17.Location = new Point(6, 141);
            groupBox17.Name = "groupBox17";
            groupBox17.Size = new Size(202, 51);
            groupBox17.TabIndex = 5;
            groupBox17.TabStop = false;
            groupBox17.Text = "Socket Status";
            // 
            // socketStatus
            // 
            socketStatus.AutoSize = true;
            socketStatus.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            socketStatus.Location = new Point(11, 24);
            socketStatus.Name = "socketStatus";
            socketStatus.Size = new Size(61, 15);
            socketStatus.TabIndex = 4;
            socketStatus.Text = "Unknown";
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
            CM_ip.Text = "172.16.0.2";
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
            groupBox3.Location = new Point(527, 386);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(408, 114);
            groupBox3.TabIndex = 2;
            groupBox3.TabStop = false;
            groupBox3.Text = "About";
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(243, 85);
            label12.Name = "label12";
            label12.Size = new Size(113, 15);
            label12.TabIndex = 12;
            label12.Text = "ryan.lopez@pitt.edu";
            // 
            // pictureBox3
            // 
            pictureBox3.Image = (Image)resources.GetObject("pictureBox3.Image");
            pictureBox3.Location = new Point(316, 16);
            pictureBox3.Name = "pictureBox3";
            pictureBox3.Size = new Size(48, 44);
            pictureBox3.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox3.TabIndex = 11;
            pictureBox3.TabStop = false;
            // 
            // pictureBox2
            // 
            pictureBox2.Image = (Image)resources.GetObject("pictureBox2.Image");
            pictureBox2.Location = new Point(243, 16);
            pictureBox2.Name = "pictureBox2";
            pictureBox2.Size = new Size(46, 48);
            pictureBox2.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox2.TabIndex = 10;
            pictureBox2.TabStop = false;
            // 
            // pictureBox1
            // 
            pictureBox1.Image = (Image)resources.GetObject("pictureBox1.Image");
            pictureBox1.Location = new Point(27, 53);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(179, 51);
            pictureBox1.SizeMode = PictureBoxSizeMode.StretchImage;
            pictureBox1.TabIndex = 10;
            pictureBox1.TabStop = false;
            // 
            // label19
            // 
            label19.AutoSize = true;
            label19.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            label19.Location = new Point(269, 67);
            label19.Name = "label19";
            label19.Size = new Size(70, 15);
            label19.TabIndex = 9;
            label19.Text = "Ryan Lopez";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label3.Location = new Point(20, 23);
            label3.Name = "label3";
            label3.Size = new Size(193, 21);
            label3.TabIndex = 8;
            label3.Text = "DDR4 Test Bench Software";
            // 
            // launchRunEditor
            // 
            launchRunEditor.BackgroundImageLayout = ImageLayout.None;
            launchRunEditor.Location = new Point(10, 82);
            launchRunEditor.Name = "launchRunEditor";
            launchRunEditor.Size = new Size(231, 54);
            launchRunEditor.TabIndex = 3;
            launchRunEditor.Text = "Run Manager";
            launchRunEditor.UseVisualStyleBackColor = true;
            launchRunEditor.Click += launchRunEditor_Click;
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(groupBox7);
            groupBox5.Controls.Add(queueBox);
            groupBox5.Controls.Add(groupBox6);
            groupBox5.Location = new Point(12, 302);
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
            abortButton.BackColor = Color.FromArgb(255, 128, 128);
            abortButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            abortButton.Location = new Point(6, 22);
            abortButton.Name = "abortButton";
            abortButton.Size = new Size(68, 47);
            abortButton.TabIndex = 17;
            abortButton.Text = "ABORT";
            abortButton.UseVisualStyleBackColor = false;
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
            configEditor.Location = new Point(10, 22);
            configEditor.Name = "configEditor";
            configEditor.Size = new Size(231, 54);
            configEditor.TabIndex = 14;
            configEditor.Text = "Configuration Editor";
            configEditor.UseVisualStyleBackColor = true;
            configEditor.Click += configEditor_Click;
            // 
            // launchRecordManager
            // 
            launchRecordManager.Location = new Point(10, 142);
            launchRecordManager.Name = "launchRecordManager";
            launchRecordManager.Size = new Size(231, 54);
            launchRecordManager.TabIndex = 15;
            launchRecordManager.Text = "Record Manager";
            launchRecordManager.UseVisualStyleBackColor = true;
            launchRecordManager.Click += launchRecordManager_Click;
            // 
            // groupBox8
            // 
            groupBox8.Controls.Add(launchRecordManager);
            groupBox8.Controls.Add(configEditor);
            groupBox8.Controls.Add(launchRunEditor);
            groupBox8.Location = new Point(14, 12);
            groupBox8.Name = "groupBox8";
            groupBox8.Size = new Size(251, 199);
            groupBox8.TabIndex = 13;
            groupBox8.TabStop = false;
            groupBox8.Text = "Apps";
            // 
            // groupBox9
            // 
            groupBox9.Controls.Add(psStatus);
            groupBox9.Controls.Add(fpgaStatus);
            groupBox9.Controls.Add(plStatus);
            groupBox9.Controls.Add(uiStatus);
            groupBox9.Controls.Add(calbStatus);
            groupBox9.Location = new Point(527, 303);
            groupBox9.Name = "groupBox9";
            groupBox9.Size = new Size(406, 77);
            groupBox9.TabIndex = 19;
            groupBox9.TabStop = false;
            groupBox9.Text = "System Status";
            // 
            // psStatus
            // 
            psStatus.Enabled = false;
            psStatus.Location = new Point(334, 18);
            psStatus.Name = "psStatus";
            psStatus.Size = new Size(66, 47);
            psStatus.TabIndex = 6;
            psStatus.Text = "POWER SERVICES";
            psStatus.UseVisualStyleBackColor = true;
            // 
            // fpgaStatus
            // 
            fpgaStatus.Enabled = false;
            fpgaStatus.Location = new Point(266, 18);
            fpgaStatus.Name = "fpgaStatus";
            fpgaStatus.Size = new Size(66, 46);
            fpgaStatus.TabIndex = 5;
            fpgaStatus.Text = "FPGA READY";
            fpgaStatus.UseVisualStyleBackColor = true;
            // 
            // plStatus
            // 
            plStatus.Enabled = false;
            plStatus.Location = new Point(198, 18);
            plStatus.Name = "plStatus";
            plStatus.Size = new Size(66, 47);
            plStatus.TabIndex = 4;
            plStatus.Text = "PL";
            plStatus.UseVisualStyleBackColor = true;
            // 
            // uiStatus
            // 
            uiStatus.Enabled = false;
            uiStatus.Location = new Point(127, 18);
            uiStatus.Name = "uiStatus";
            uiStatus.Size = new Size(66, 47);
            uiStatus.TabIndex = 3;
            uiStatus.Text = "UI";
            uiStatus.UseVisualStyleBackColor = true;
            // 
            // calbStatus
            // 
            calbStatus.Enabled = false;
            calbStatus.Location = new Point(6, 18);
            calbStatus.Name = "calbStatus";
            calbStatus.Size = new Size(115, 47);
            calbStatus.TabIndex = 0;
            calbStatus.Text = "CALIBRATED";
            calbStatus.UseVisualStyleBackColor = true;
            // 
            // groupBox10
            // 
            groupBox10.Controls.Add(groupBox16);
            groupBox10.Controls.Add(groupBox13);
            groupBox10.Controls.Add(groupBox12);
            groupBox10.Controls.Add(groupBox11);
            groupBox10.Location = new Point(271, 12);
            groupBox10.Name = "groupBox10";
            groupBox10.Size = new Size(307, 284);
            groupBox10.TabIndex = 19;
            groupBox10.TabStop = false;
            groupBox10.Text = "Power Management";
            // 
            // groupBox16
            // 
            groupBox16.Controls.Add(powerStatus);
            groupBox16.Location = new Point(91, 22);
            groupBox16.Name = "groupBox16";
            groupBox16.Size = new Size(201, 92);
            groupBox16.TabIndex = 19;
            groupBox16.TabStop = false;
            groupBox16.Text = "Power Request";
            // 
            // powerStatus
            // 
            powerStatus.Enabled = false;
            powerStatus.Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 0);
            powerStatus.ForeColor = SystemColors.ControlLightLight;
            powerStatus.Location = new Point(6, 22);
            powerStatus.Name = "powerStatus";
            powerStatus.Size = new Size(182, 61);
            powerStatus.TabIndex = 18;
            powerStatus.Text = "SYSTEM RESTARTING";
            powerStatus.UseVisualStyleBackColor = false;
            // 
            // groupBox13
            // 
            groupBox13.Controls.Add(groupBox15);
            groupBox13.Controls.Add(groupBox14);
            groupBox13.Location = new Point(9, 120);
            groupBox13.Name = "groupBox13";
            groupBox13.Size = new Size(283, 85);
            groupBox13.TabIndex = 20;
            groupBox13.TabStop = false;
            groupBox13.Text = "Contactor Relay";
            // 
            // groupBox15
            // 
            groupBox15.Controls.Add(relayID_power);
            groupBox15.Controls.Add(invertRelay_power);
            groupBox15.Location = new Point(122, 22);
            groupBox15.Name = "groupBox15";
            groupBox15.Size = new Size(155, 57);
            groupBox15.TabIndex = 22;
            groupBox15.TabStop = false;
            groupBox15.Text = "Relay ID";
            // 
            // relayID_power
            // 
            relayID_power.Location = new Point(14, 22);
            relayID_power.Maximum = new decimal(new int[] { 8, 0, 0, 0 });
            relayID_power.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            relayID_power.Name = "relayID_power";
            relayID_power.Size = new Size(72, 23);
            relayID_power.TabIndex = 1;
            relayID_power.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // invertRelay_power
            // 
            invertRelay_power.AutoSize = true;
            invertRelay_power.Location = new Point(92, 23);
            invertRelay_power.Name = "invertRelay_power";
            invertRelay_power.Size = new Size(56, 19);
            invertRelay_power.TabIndex = 0;
            invertRelay_power.Text = "Invert";
            invertRelay_power.UseVisualStyleBackColor = true;
            // 
            // groupBox14
            // 
            groupBox14.Controls.Add(restartDelay);
            groupBox14.Location = new Point(9, 22);
            groupBox14.Name = "groupBox14";
            groupBox14.Size = new Size(102, 57);
            groupBox14.TabIndex = 21;
            groupBox14.TabStop = false;
            groupBox14.Text = "Delay (ms)";
            // 
            // restartDelay
            // 
            restartDelay.Increment = new decimal(new int[] { 500, 0, 0, 0 });
            restartDelay.Location = new Point(14, 22);
            restartDelay.Maximum = new decimal(new int[] { 120000, 0, 0, 0 });
            restartDelay.Minimum = new decimal(new int[] { 100, 0, 0, 0 });
            restartDelay.Name = "restartDelay";
            restartDelay.Size = new Size(72, 23);
            restartDelay.TabIndex = 1;
            restartDelay.Value = new decimal(new int[] { 5000, 0, 0, 0 });
            // 
            // groupBox12
            // 
            groupBox12.Controls.Add(powerOn);
            groupBox12.Controls.Add(powerAUTO);
            groupBox12.Controls.Add(powerOFF);
            groupBox12.Location = new Point(9, 211);
            groupBox12.Name = "groupBox12";
            groupBox12.Size = new Size(283, 67);
            groupBox12.TabIndex = 19;
            groupBox12.TabStop = false;
            groupBox12.Text = "Manual Override";
            // 
            // powerOn
            // 
            powerOn.Location = new Point(191, 22);
            powerOn.Name = "powerOn";
            powerOn.Size = new Size(86, 39);
            powerOn.TabIndex = 2;
            powerOn.Text = "ON";
            powerOn.UseVisualStyleBackColor = true;
            // 
            // powerAUTO
            // 
            powerAUTO.Location = new Point(101, 22);
            powerAUTO.Name = "powerAUTO";
            powerAUTO.Size = new Size(86, 39);
            powerAUTO.TabIndex = 1;
            powerAUTO.Text = "AUTO";
            powerAUTO.UseVisualStyleBackColor = true;
            // 
            // powerOFF
            // 
            powerOFF.Location = new Point(6, 22);
            powerOFF.Name = "powerOFF";
            powerOFF.Size = new Size(89, 39);
            powerOFF.TabIndex = 0;
            powerOFF.Text = "OFF";
            powerOFF.UseVisualStyleBackColor = true;
            // 
            // groupBox11
            // 
            groupBox11.Controls.Add(contactorStatus);
            groupBox11.Location = new Point(9, 22);
            groupBox11.Name = "groupBox11";
            groupBox11.Size = new Size(76, 92);
            groupBox11.TabIndex = 5;
            groupBox11.TabStop = false;
            groupBox11.Text = "Contactor Status";
            // 
            // contactorStatus
            // 
            contactorStatus.Location = new Point(9, 39);
            contactorStatus.Name = "contactorStatus";
            contactorStatus.Size = new Size(61, 44);
            contactorStatus.TabIndex = 18;
            contactorStatus.Text = "IDLE";
            contactorStatus.UseVisualStyleBackColor = false;
            // 
            // groupBox18
            // 
            groupBox18.Controls.Add(servicesStatus);
            groupBox18.Controls.Add(beamRequestInfo);
            groupBox18.Controls.Add(beamActiveInfo);
            groupBox18.Location = new Point(820, 132);
            groupBox18.Name = "groupBox18";
            groupBox18.Size = new Size(115, 164);
            groupBox18.TabIndex = 20;
            groupBox18.TabStop = false;
            groupBox18.Text = "Facility Services";
            // 
            // servicesStatus
            // 
            servicesStatus.Enabled = false;
            servicesStatus.Location = new Point(11, 22);
            servicesStatus.Name = "servicesStatus";
            servicesStatus.Size = new Size(92, 40);
            servicesStatus.TabIndex = 8;
            servicesStatus.Text = "ONLINE";
            servicesStatus.UseVisualStyleBackColor = true;
            // 
            // beamRequestInfo
            // 
            beamRequestInfo.Enabled = false;
            beamRequestInfo.Location = new Point(11, 67);
            beamRequestInfo.Name = "beamRequestInfo";
            beamRequestInfo.Size = new Size(92, 40);
            beamRequestInfo.TabIndex = 7;
            beamRequestInfo.Text = "Beam Request";
            beamRequestInfo.UseVisualStyleBackColor = true;
            // 
            // beamActiveInfo
            // 
            beamActiveInfo.Enabled = false;
            beamActiveInfo.Location = new Point(11, 113);
            beamActiveInfo.Name = "beamActiveInfo";
            beamActiveInfo.Size = new Size(92, 40);
            beamActiveInfo.TabIndex = 6;
            beamActiveInfo.Text = "Beam Active";
            beamActiveInfo.UseVisualStyleBackColor = true;
            // 
            // groupBox19
            // 
            groupBox19.Controls.Add(sdram3);
            groupBox19.Controls.Add(sdram2);
            groupBox19.Controls.Add(sdram1);
            groupBox19.Controls.Add(sdram0);
            groupBox19.Location = new Point(584, 12);
            groupBox19.Name = "groupBox19";
            groupBox19.Size = new Size(279, 114);
            groupBox19.TabIndex = 20;
            groupBox19.TabStop = false;
            groupBox19.Text = "SODIMM Status";
            // 
            // sdram3
            // 
            sdram3.Enabled = false;
            sdram3.Location = new Point(210, 21);
            sdram3.Name = "sdram3";
            sdram3.Size = new Size(61, 77);
            sdram3.TabIndex = 10;
            sdram3.Text = "SDRAM 3";
            sdram3.UseVisualStyleBackColor = true;
            // 
            // sdram2
            // 
            sdram2.Enabled = false;
            sdram2.Location = new Point(143, 22);
            sdram2.Name = "sdram2";
            sdram2.Size = new Size(61, 77);
            sdram2.TabIndex = 9;
            sdram2.Text = "SDRAM 2";
            sdram2.UseVisualStyleBackColor = true;
            // 
            // sdram1
            // 
            sdram1.Enabled = false;
            sdram1.Location = new Point(77, 22);
            sdram1.Name = "sdram1";
            sdram1.Size = new Size(61, 77);
            sdram1.TabIndex = 8;
            sdram1.Text = "SDRAM 1";
            sdram1.UseVisualStyleBackColor = true;
            // 
            // sdram0
            // 
            sdram0.Enabled = false;
            sdram0.Location = new Point(11, 22);
            sdram0.Name = "sdram0";
            sdram0.Size = new Size(61, 77);
            sdram0.TabIndex = 7;
            sdram0.Text = "SDRAM 0";
            sdram0.UseVisualStyleBackColor = true;
            // 
            // groupBox20
            // 
            groupBox20.Controls.Add(requestOff);
            groupBox20.Controls.Add(requestReboot);
            groupBox20.Location = new Point(869, 12);
            groupBox20.Name = "groupBox20";
            groupBox20.Size = new Size(66, 114);
            groupBox20.TabIndex = 19;
            groupBox20.TabStop = false;
            groupBox20.Text = "PWR";
            // 
            // requestOff
            // 
            requestOff.Location = new Point(6, 63);
            requestOff.Name = "requestOff";
            requestOff.Size = new Size(54, 35);
            requestOff.TabIndex = 19;
            requestOff.Text = "Off";
            requestOff.UseVisualStyleBackColor = false;
            // 
            // requestReboot
            // 
            requestReboot.Location = new Point(6, 22);
            requestReboot.Name = "requestReboot";
            requestReboot.Size = new Size(54, 35);
            requestReboot.TabIndex = 18;
            requestReboot.Text = "Reboot";
            requestReboot.UseVisualStyleBackColor = false;
            // 
            // groupBox21
            // 
            groupBox21.Controls.Add(groupBox24);
            groupBox21.Controls.Add(groupBox23);
            groupBox21.Controls.Add(groupBox22);
            groupBox21.Location = new Point(584, 132);
            groupBox21.Name = "groupBox21";
            groupBox21.Size = new Size(230, 164);
            groupBox21.TabIndex = 21;
            groupBox21.TabStop = false;
            groupBox21.Text = "Beam Relay";
            // 
            // groupBox24
            // 
            groupBox24.Controls.Add(beamRequestStatus);
            groupBox24.Controls.Add(beamRelayStatus);
            groupBox24.Location = new Point(112, 22);
            groupBox24.Name = "groupBox24";
            groupBox24.Size = new Size(112, 72);
            groupBox24.TabIndex = 24;
            groupBox24.TabStop = false;
            groupBox24.Text = "Beam Status";
            // 
            // beamRequestStatus
            // 
            beamRequestStatus.Enabled = false;
            beamRequestStatus.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold, GraphicsUnit.Point, 0);
            beamRequestStatus.ForeColor = SystemColors.ControlLightLight;
            beamRequestStatus.Location = new Point(7, 16);
            beamRequestStatus.Name = "beamRequestStatus";
            beamRequestStatus.Size = new Size(98, 24);
            beamRequestStatus.TabIndex = 18;
            beamRequestStatus.Text = "NO LINK";
            beamRequestStatus.UseVisualStyleBackColor = false;
            // 
            // beamRelayStatus
            // 
            beamRelayStatus.Enabled = false;
            beamRelayStatus.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold, GraphicsUnit.Point, 0);
            beamRelayStatus.ForeColor = SystemColors.ControlLightLight;
            beamRelayStatus.Location = new Point(7, 42);
            beamRelayStatus.Name = "beamRelayStatus";
            beamRelayStatus.Size = new Size(98, 24);
            beamRelayStatus.TabIndex = 19;
            beamRelayStatus.Text = "NO RELAY";
            beamRelayStatus.UseVisualStyleBackColor = false;
            // 
            // groupBox23
            // 
            groupBox23.Controls.Add(relayID_beam);
            groupBox23.Controls.Add(invertRelay_beam);
            groupBox23.Location = new Point(6, 22);
            groupBox23.Name = "groupBox23";
            groupBox23.Size = new Size(100, 72);
            groupBox23.TabIndex = 23;
            groupBox23.TabStop = false;
            groupBox23.Text = "Relay";
            // 
            // relayID_beam
            // 
            relayID_beam.Location = new Point(10, 18);
            relayID_beam.Maximum = new decimal(new int[] { 8, 0, 0, 0 });
            relayID_beam.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            relayID_beam.Name = "relayID_beam";
            relayID_beam.Size = new Size(80, 23);
            relayID_beam.TabIndex = 1;
            relayID_beam.Value = new decimal(new int[] { 2, 0, 0, 0 });
            // 
            // invertRelay_beam
            // 
            invertRelay_beam.AutoSize = true;
            invertRelay_beam.Checked = true;
            invertRelay_beam.CheckState = CheckState.Checked;
            invertRelay_beam.Location = new Point(10, 47);
            invertRelay_beam.Name = "invertRelay_beam";
            invertRelay_beam.Size = new Size(56, 19);
            invertRelay_beam.TabIndex = 0;
            invertRelay_beam.Text = "Invert";
            invertRelay_beam.UseVisualStyleBackColor = true;
            // 
            // groupBox22
            // 
            groupBox22.Controls.Add(beamOn);
            groupBox22.Controls.Add(beamAUTO);
            groupBox22.Controls.Add(beamOFF);
            groupBox22.Location = new Point(6, 100);
            groupBox22.Name = "groupBox22";
            groupBox22.Size = new Size(218, 58);
            groupBox22.TabIndex = 20;
            groupBox22.TabStop = false;
            groupBox22.Text = "Manual Override";
            // 
            // beamOn
            // 
            beamOn.Location = new Point(150, 18);
            beamOn.Name = "beamOn";
            beamOn.Size = new Size(62, 32);
            beamOn.TabIndex = 2;
            beamOn.Text = "ON";
            beamOn.UseVisualStyleBackColor = true;
            // 
            // beamAUTO
            // 
            beamAUTO.Location = new Point(78, 18);
            beamAUTO.Name = "beamAUTO";
            beamAUTO.Size = new Size(68, 32);
            beamAUTO.TabIndex = 1;
            beamAUTO.Text = "AUTO";
            beamAUTO.UseVisualStyleBackColor = true;
            // 
            // beamOFF
            // 
            beamOFF.Location = new Point(6, 18);
            beamOFF.Name = "beamOFF";
            beamOFF.Size = new Size(68, 32);
            beamOFF.TabIndex = 0;
            beamOFF.Text = "OFF";
            beamOFF.UseVisualStyleBackColor = true;
            // 
            // groupBox25
            // 
            groupBox25.Controls.Add(pmicVolts);
            groupBox25.Controls.Add(pmicAmps);
            groupBox25.Controls.Add(pmicWatts);
            groupBox25.Controls.Add(pmicNote);
            groupBox25.Location = new Point(14, 215);
            groupBox25.Name = "groupBox25";
            groupBox25.Size = new Size(251, 84);
            groupBox25.TabIndex = 21;
            groupBox25.TabStop = false;
            groupBox25.Text = "PMIC Status";
            // 
            // pmicVolts
            // 
            pmicVolts.BackColor = Color.Black;
            pmicVolts.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
            pmicVolts.ForeColor = Color.Yellow;
            pmicVolts.Location = new Point(8, 22);
            pmicVolts.Name = "pmicVolts";
            pmicVolts.Size = new Size(76, 30);
            pmicVolts.TabIndex = 0;
            pmicVolts.Text = "— V";
            pmicVolts.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // pmicAmps
            // 
            pmicAmps.BackColor = Color.Black;
            pmicAmps.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
            pmicAmps.ForeColor = Color.Yellow;
            pmicAmps.Location = new Point(88, 22);
            pmicAmps.Name = "pmicAmps";
            pmicAmps.Size = new Size(76, 30);
            pmicAmps.TabIndex = 1;
            pmicAmps.Text = "— A";
            pmicAmps.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // pmicWatts
            // 
            pmicWatts.BackColor = Color.Black;
            pmicWatts.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
            pmicWatts.ForeColor = Color.Yellow;
            pmicWatts.Location = new Point(168, 22);
            pmicWatts.Name = "pmicWatts";
            pmicWatts.Size = new Size(76, 30);
            pmicWatts.TabIndex = 2;
            pmicWatts.Text = "— W";
            pmicWatts.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // pmicNote
            // 
            pmicNote.AutoSize = true;
            pmicNote.ForeColor = SystemColors.GrayText;
            pmicNote.Location = new Point(10, 58);
            pmicNote.Name = "pmicNote";
            pmicNote.Size = new Size(84, 15);
            pmicNote.TabIndex = 3;
            pmicNote.Text = "not connected";
            // 
            // Launcher
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(947, 512);
            Controls.Add(groupBox25);
            Controls.Add(groupBox21);
            Controls.Add(groupBox20);
            Controls.Add(groupBox19);
            Controls.Add(groupBox9);
            Controls.Add(groupBox18);
            Controls.Add(groupBox10);
            Controls.Add(groupBox8);
            Controls.Add(groupBox5);
            Controls.Add(groupBox1);
            Controls.Add(groupBox3);
            Name = "Launcher";
            Text = "Launcher";
            groupBox1.ResumeLayout(false);
            groupBox17.ResumeLayout(false);
            groupBox17.PerformLayout();
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
            groupBox8.ResumeLayout(false);
            groupBox9.ResumeLayout(false);
            groupBox10.ResumeLayout(false);
            groupBox16.ResumeLayout(false);
            groupBox13.ResumeLayout(false);
            groupBox15.ResumeLayout(false);
            groupBox15.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)relayID_power).EndInit();
            groupBox14.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)restartDelay).EndInit();
            groupBox12.ResumeLayout(false);
            groupBox11.ResumeLayout(false);
            groupBox18.ResumeLayout(false);
            groupBox19.ResumeLayout(false);
            groupBox20.ResumeLayout(false);
            groupBox21.ResumeLayout(false);
            groupBox24.ResumeLayout(false);
            groupBox23.ResumeLayout(false);
            groupBox23.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)relayID_beam).EndInit();
            groupBox22.ResumeLayout(false);
            groupBox25.ResumeLayout(false);
            groupBox25.PerformLayout();
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
        private GroupBox groupBox8;
        private GroupBox groupBox9;
        private Button psStatus;
        private Button fpgaStatus;
        private Button plStatus;
        private Button uiStatus;
        private Button calbStatus;
        private GroupBox groupBox10;
        private GroupBox groupBox13;
        private CheckBox invertRelay_power;
        private GroupBox groupBox12;
        private Button powerOn;
        private Button powerAUTO;
        private Button powerOFF;
        private GroupBox groupBox11;
        private Button contactorStatus;
        private GroupBox groupBox16;
        private Button powerStatus;
        private GroupBox groupBox15;
        private NumericUpDown relayID_power;
        private GroupBox groupBox14;
        private NumericUpDown restartDelay;
        private GroupBox groupBox17;
        private Label socketStatus;
        private GroupBox groupBox18;
        private Button beamActiveInfo;
        private Button beamRequestInfo;
        private Button servicesStatus;
        private GroupBox groupBox19;
        private Button sdram3;
        private Button sdram2;
        private Button sdram1;
        private Button sdram0;
        private GroupBox groupBox20;
        private Button requestOff;
        private Button requestReboot;
        private GroupBox groupBox21;
        private GroupBox groupBox22;
        private Button beamOn;
        private Button beamAUTO;
        private Button beamOFF;
        private GroupBox groupBox24;
        private Button beamRequestStatus;
        private Button beamRelayStatus;
        private GroupBox groupBox23;
        private NumericUpDown relayID_beam;
        private CheckBox invertRelay_beam;
        private GroupBox groupBox25;
        private Label pmicVolts;
        private Label pmicAmps;
        private Label pmicWatts;
        private Label pmicNote;
    }
}