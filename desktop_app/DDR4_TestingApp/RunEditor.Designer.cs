namespace DDR4_TestingApp
{
    partial class RunEditor
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
            splitContainer2 = new SplitContainer();
            label1 = new Label();
            groupBox18 = new GroupBox();
            runBrowser = new TreeView();
            groupBox19 = new GroupBox();
            groupBox9 = new GroupBox();
            createdTimestamp = new TextBox();
            groupBox10 = new GroupBox();
            modifiedTimestamp = new TextBox();
            groupBox13 = new GroupBox();
            groupBox17 = new GroupBox();
            lastUpdated = new TextBox();
            groupBox16 = new GroupBox();
            sortBy = new ComboBox();
            invertSort = new CheckBox();
            groupBox15 = new GroupBox();
            groupBy = new ComboBox();
            splitContainer3 = new SplitContainer();
            syncStatus = new Button();
            submitRun = new Button();
            deleteRun = new Button();
            runNumber = new Label();
            groupBox21 = new GroupBox();
            runNumberEditor = new NumericUpDown();
            groupBox27 = new GroupBox();
            label7 = new Label();
            runFluence = new TextBox();
            postLock = new Button();
            primaryLock = new Button();
            prelimaryLock = new Button();
            dynamicBox = new GroupBox();
            treeView1 = new TreeView();
            groupBox23 = new GroupBox();
            primaryVerify = new Button();
            groupBox14 = new GroupBox();
            primaryWrite = new Button();
            groupBox22 = new GroupBox();
            groupBox29 = new GroupBox();
            expectedStage = new TextBox();
            requestOperation = new Button();
            groupBox26 = new GroupBox();
            runOperation = new ComboBox();
            groupBox25 = new GroupBox();
            runStage = new ComboBox();
            postBox = new GroupBox();
            postRecords = new TreeView();
            preliminaryBox = new GroupBox();
            preliminaryRecords = new TreeView();
            groupBox20 = new GroupBox();
            groupBox2 = new GroupBox();
            label3 = new Label();
            runEnergy = new TextBox();
            groupBox28 = new GroupBox();
            label10 = new Label();
            textBox1 = new TextBox();
            groupBox3 = new GroupBox();
            label4 = new Label();
            runSODIMM = new TextBox();
            groupBox7 = new GroupBox();
            label8 = new Label();
            runAirgap = new TextBox();
            groupBox5 = new GroupBox();
            label5 = new Label();
            runAngle = new TextBox();
            groupBox4 = new GroupBox();
            label9 = new Label();
            runIon = new TextBox();
            groupBox6 = new GroupBox();
            label6 = new Label();
            runFlux = new TextBox();
            groupBox1 = new GroupBox();
            label2 = new Label();
            runLET = new TextBox();
            groupBox8 = new GroupBox();
            runDescription = new RichTextBox();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer2).BeginInit();
            splitContainer2.Panel1.SuspendLayout();
            splitContainer2.Panel2.SuspendLayout();
            splitContainer2.SuspendLayout();
            groupBox18.SuspendLayout();
            groupBox19.SuspendLayout();
            groupBox9.SuspendLayout();
            groupBox10.SuspendLayout();
            groupBox13.SuspendLayout();
            groupBox17.SuspendLayout();
            groupBox16.SuspendLayout();
            groupBox15.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer3).BeginInit();
            splitContainer3.Panel1.SuspendLayout();
            splitContainer3.Panel2.SuspendLayout();
            splitContainer3.SuspendLayout();
            groupBox21.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)runNumberEditor).BeginInit();
            groupBox27.SuspendLayout();
            dynamicBox.SuspendLayout();
            groupBox23.SuspendLayout();
            groupBox14.SuspendLayout();
            groupBox22.SuspendLayout();
            groupBox29.SuspendLayout();
            groupBox26.SuspendLayout();
            groupBox25.SuspendLayout();
            postBox.SuspendLayout();
            preliminaryBox.SuspendLayout();
            groupBox20.SuspendLayout();
            groupBox2.SuspendLayout();
            groupBox28.SuspendLayout();
            groupBox3.SuspendLayout();
            groupBox7.SuspendLayout();
            groupBox5.SuspendLayout();
            groupBox4.SuspendLayout();
            groupBox6.SuspendLayout();
            groupBox1.SuspendLayout();
            groupBox8.SuspendLayout();
            SuspendLayout();
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 0);
            splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(splitContainer2);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(splitContainer3);
            splitContainer1.Size = new Size(1273, 822);
            splitContainer1.SplitterDistance = 591;
            splitContainer1.TabIndex = 0;
            // 
            // splitContainer2
            // 
            splitContainer2.Dock = DockStyle.Fill;
            splitContainer2.Location = new Point(0, 0);
            splitContainer2.Name = "splitContainer2";
            splitContainer2.Orientation = Orientation.Horizontal;
            // 
            // splitContainer2.Panel1
            // 
            splitContainer2.Panel1.BackColor = SystemColors.ControlDark;
            splitContainer2.Panel1.Controls.Add(label1);
            // 
            // splitContainer2.Panel2
            // 
            splitContainer2.Panel2.Controls.Add(groupBox18);
            splitContainer2.Panel2.Controls.Add(groupBox13);
            splitContainer2.Size = new Size(591, 822);
            splitContainer2.SplitterDistance = 48;
            splitContainer2.TabIndex = 0;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(139, 28);
            label1.TabIndex = 0;
            label1.Text = "Run Manager";
            // 
            // groupBox18
            // 
            groupBox18.Controls.Add(runBrowser);
            groupBox18.Location = new Point(12, 102);
            groupBox18.Name = "groupBox18";
            groupBox18.Size = new Size(565, 656);
            groupBox18.TabIndex = 4;
            groupBox18.TabStop = false;
            groupBox18.Text = "Run Browser";
            // 
            // runBrowser
            // 
            runBrowser.Location = new Point(6, 22);
            runBrowser.Name = "runBrowser";
            runBrowser.Size = new Size(547, 628);
            runBrowser.TabIndex = 1;
            // 
            // groupBox19
            // 
            groupBox19.Controls.Add(groupBox9);
            groupBox19.Controls.Add(groupBox10);
            groupBox19.Location = new Point(11, 322);
            groupBox19.Name = "groupBox19";
            groupBox19.Size = new Size(368, 80);
            groupBox19.TabIndex = 10;
            groupBox19.TabStop = false;
            groupBox19.Text = "Timestamps";
            // 
            // groupBox9
            // 
            groupBox9.Controls.Add(createdTimestamp);
            groupBox9.Location = new Point(6, 17);
            groupBox9.Name = "groupBox9";
            groupBox9.Size = new Size(151, 57);
            groupBox9.TabIndex = 8;
            groupBox9.TabStop = false;
            groupBox9.Text = "Created ";
            // 
            // createdTimestamp
            // 
            createdTimestamp.Location = new Point(6, 22);
            createdTimestamp.Name = "createdTimestamp";
            createdTimestamp.ReadOnly = true;
            createdTimestamp.Size = new Size(139, 23);
            createdTimestamp.TabIndex = 5;
            // 
            // groupBox10
            // 
            groupBox10.Controls.Add(modifiedTimestamp);
            groupBox10.Location = new Point(163, 17);
            groupBox10.Name = "groupBox10";
            groupBox10.Size = new Size(189, 57);
            groupBox10.TabIndex = 9;
            groupBox10.TabStop = false;
            groupBox10.Text = "Modified";
            // 
            // modifiedTimestamp
            // 
            modifiedTimestamp.Location = new Point(6, 22);
            modifiedTimestamp.Name = "modifiedTimestamp";
            modifiedTimestamp.ReadOnly = true;
            modifiedTimestamp.Size = new Size(175, 23);
            modifiedTimestamp.TabIndex = 5;
            // 
            // groupBox13
            // 
            groupBox13.Controls.Add(groupBox17);
            groupBox13.Controls.Add(groupBox16);
            groupBox13.Controls.Add(groupBox15);
            groupBox13.Location = new Point(12, 8);
            groupBox13.Name = "groupBox13";
            groupBox13.Size = new Size(565, 87);
            groupBox13.TabIndex = 0;
            groupBox13.TabStop = false;
            groupBox13.Text = "Browser Options";
            // 
            // groupBox17
            // 
            groupBox17.Controls.Add(lastUpdated);
            groupBox17.Location = new Point(402, 23);
            groupBox17.Name = "groupBox17";
            groupBox17.Size = new Size(157, 54);
            groupBox17.TabIndex = 3;
            groupBox17.TabStop = false;
            groupBox17.Text = "Last Updated";
            // 
            // lastUpdated
            // 
            lastUpdated.Location = new Point(6, 22);
            lastUpdated.Name = "lastUpdated";
            lastUpdated.Size = new Size(145, 23);
            lastUpdated.TabIndex = 4;
            // 
            // groupBox16
            // 
            groupBox16.Controls.Add(sortBy);
            groupBox16.Controls.Add(invertSort);
            groupBox16.Location = new Point(158, 23);
            groupBox16.Name = "groupBox16";
            groupBox16.Size = new Size(238, 53);
            groupBox16.TabIndex = 2;
            groupBox16.TabStop = false;
            groupBox16.Text = "Sort By";
            // 
            // sortBy
            // 
            sortBy.FormattingEnabled = true;
            sortBy.Items.AddRange(new object[] { "ID", "SODIMM", "ION", "LET" });
            sortBy.Location = new Point(6, 22);
            sortBy.Name = "sortBy";
            sortBy.Size = new Size(121, 23);
            sortBy.TabIndex = 1;
            // 
            // invertSort
            // 
            invertSort.AutoSize = true;
            invertSort.Location = new Point(142, 24);
            invertSort.Name = "invertSort";
            invertSort.Size = new Size(80, 19);
            invertSort.TabIndex = 3;
            invertSort.Text = "Invert Sort";
            invertSort.UseVisualStyleBackColor = true;
            // 
            // groupBox15
            // 
            groupBox15.Controls.Add(groupBy);
            groupBox15.Location = new Point(14, 22);
            groupBox15.Name = "groupBox15";
            groupBox15.Size = new Size(138, 54);
            groupBox15.TabIndex = 1;
            groupBox15.TabStop = false;
            groupBox15.Text = "Group By";
            // 
            // groupBy
            // 
            groupBy.FormattingEnabled = true;
            groupBy.Items.AddRange(new object[] { "ID", "SODIMM", "ION", "LET" });
            groupBy.Location = new Point(6, 22);
            groupBy.Name = "groupBy";
            groupBy.Size = new Size(121, 23);
            groupBy.TabIndex = 1;
            // 
            // splitContainer3
            // 
            splitContainer3.Location = new Point(3, 3);
            splitContainer3.Name = "splitContainer3";
            splitContainer3.Orientation = Orientation.Horizontal;
            // 
            // splitContainer3.Panel1
            // 
            splitContainer3.Panel1.BackColor = Color.FromArgb(255, 128, 128);
            splitContainer3.Panel1.Controls.Add(syncStatus);
            splitContainer3.Panel1.Controls.Add(submitRun);
            splitContainer3.Panel1.Controls.Add(deleteRun);
            splitContainer3.Panel1.Controls.Add(runNumber);
            // 
            // splitContainer3.Panel2
            // 
            splitContainer3.Panel2.Controls.Add(groupBox21);
            splitContainer3.Panel2.Controls.Add(groupBox19);
            splitContainer3.Panel2.Controls.Add(groupBox27);
            splitContainer3.Panel2.Controls.Add(postLock);
            splitContainer3.Panel2.Controls.Add(primaryLock);
            splitContainer3.Panel2.Controls.Add(prelimaryLock);
            splitContainer3.Panel2.Controls.Add(dynamicBox);
            splitContainer3.Panel2.Controls.Add(groupBox23);
            splitContainer3.Panel2.Controls.Add(groupBox14);
            splitContainer3.Panel2.Controls.Add(groupBox22);
            splitContainer3.Panel2.Controls.Add(postBox);
            splitContainer3.Panel2.Controls.Add(preliminaryBox);
            splitContainer3.Panel2.Controls.Add(groupBox20);
            splitContainer3.Panel2.Controls.Add(groupBox8);
            splitContainer3.Size = new Size(675, 819);
            splitContainer3.SplitterDistance = 46;
            splitContainer3.TabIndex = 10;
            // 
            // syncStatus
            // 
            syncStatus.BackColor = Color.FromArgb(128, 255, 128);
            syncStatus.Enabled = false;
            syncStatus.Location = new Point(289, 3);
            syncStatus.Name = "syncStatus";
            syncStatus.Size = new Size(122, 37);
            syncStatus.TabIndex = 4;
            syncStatus.Text = "SYNCED";
            syncStatus.UseVisualStyleBackColor = false;
            // 
            // submitRun
            // 
            submitRun.Location = new Point(417, 3);
            submitRun.Name = "submitRun";
            submitRun.Size = new Size(122, 37);
            submitRun.TabIndex = 3;
            submitRun.Text = "Submit Run";
            submitRun.UseVisualStyleBackColor = true;
            // 
            // deleteRun
            // 
            deleteRun.Location = new Point(545, 3);
            deleteRun.Name = "deleteRun";
            deleteRun.Size = new Size(122, 37);
            deleteRun.TabIndex = 2;
            deleteRun.Text = "Delete Run";
            deleteRun.UseVisualStyleBackColor = true;
            // 
            // runNumber
            // 
            runNumber.AutoSize = true;
            runNumber.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            runNumber.Location = new Point(5, 6);
            runNumber.Name = "runNumber";
            runNumber.Size = new Size(79, 28);
            runNumber.TabIndex = 1;
            runNumber.Text = "Run #2";
            // 
            // groupBox21
            // 
            groupBox21.Controls.Add(runNumberEditor);
            groupBox21.Location = new Point(385, 183);
            groupBox21.Name = "groupBox21";
            groupBox21.Size = new Size(98, 57);
            groupBox21.TabIndex = 5;
            groupBox21.TabStop = false;
            groupBox21.Text = "Run #";
            // 
            // runNumberEditor
            // 
            runNumberEditor.Location = new Point(12, 23);
            runNumberEditor.Name = "runNumberEditor";
            runNumberEditor.Size = new Size(77, 23);
            runNumberEditor.TabIndex = 3;
            // 
            // groupBox27
            // 
            groupBox27.Controls.Add(label7);
            groupBox27.Controls.Add(runFluence);
            groupBox27.Location = new Point(489, 183);
            groupBox27.Name = "groupBox27";
            groupBox27.Size = new Size(174, 54);
            groupBox27.TabIndex = 9;
            groupBox27.TabStop = false;
            groupBox27.Text = "Fluence";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(100, 28);
            label7.Name = "label7";
            label7.Size = new Size(61, 15);
            label7.TabIndex = 4;
            label7.Text = "# particles";
            // 
            // runFluence
            // 
            runFluence.Location = new Point(13, 22);
            runFluence.Name = "runFluence";
            runFluence.Size = new Size(78, 23);
            runFluence.TabIndex = 2;
            // 
            // postLock
            // 
            postLock.BackColor = Color.Yellow;
            postLock.Enabled = false;
            postLock.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            postLock.Location = new Point(435, 720);
            postLock.Name = "postLock";
            postLock.Size = new Size(228, 37);
            postLock.TabIndex = 17;
            postLock.Text = "PENDING";
            postLock.UseVisualStyleBackColor = false;
            // 
            // primaryLock
            // 
            primaryLock.BackColor = Color.Lime;
            primaryLock.Enabled = false;
            primaryLock.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            primaryLock.Location = new Point(223, 720);
            primaryLock.Name = "primaryLock";
            primaryLock.Size = new Size(206, 37);
            primaryLock.TabIndex = 16;
            primaryLock.Text = "OPEN";
            primaryLock.UseVisualStyleBackColor = false;
            // 
            // prelimaryLock
            // 
            prelimaryLock.BackColor = Color.Red;
            prelimaryLock.Enabled = false;
            prelimaryLock.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            prelimaryLock.Location = new Point(11, 720);
            prelimaryLock.Name = "prelimaryLock";
            prelimaryLock.Size = new Size(206, 37);
            prelimaryLock.TabIndex = 15;
            prelimaryLock.Text = "LOCKED";
            prelimaryLock.UseVisualStyleBackColor = false;
            // 
            // dynamicBox
            // 
            dynamicBox.Controls.Add(treeView1);
            dynamicBox.Location = new Point(227, 560);
            dynamicBox.Name = "dynamicBox";
            dynamicBox.Size = new Size(200, 154);
            dynamicBox.TabIndex = 14;
            dynamicBox.TabStop = false;
            dynamicBox.Text = "Primary Dynamic";
            // 
            // treeView1
            // 
            treeView1.Location = new Point(6, 22);
            treeView1.Name = "treeView1";
            treeView1.Size = new Size(190, 126);
            treeView1.TabIndex = 3;
            // 
            // groupBox23
            // 
            groupBox23.Controls.Add(primaryVerify);
            groupBox23.Location = new Point(227, 485);
            groupBox23.Name = "groupBox23";
            groupBox23.Size = new Size(200, 69);
            groupBox23.TabIndex = 13;
            groupBox23.TabStop = false;
            groupBox23.Text = "Primary Verify";
            // 
            // primaryVerify
            // 
            primaryVerify.BackColor = Color.FromArgb(128, 255, 128);
            primaryVerify.Location = new Point(8, 22);
            primaryVerify.Name = "primaryVerify";
            primaryVerify.Size = new Size(186, 37);
            primaryVerify.TabIndex = 0;
            primaryVerify.Text = "Performed";
            primaryVerify.UseVisualStyleBackColor = false;
            // 
            // groupBox14
            // 
            groupBox14.Controls.Add(primaryWrite);
            groupBox14.Location = new Point(227, 408);
            groupBox14.Name = "groupBox14";
            groupBox14.Size = new Size(200, 71);
            groupBox14.TabIndex = 12;
            groupBox14.TabStop = false;
            groupBox14.Text = "Primary Write";
            // 
            // primaryWrite
            // 
            primaryWrite.BackColor = Color.FromArgb(255, 128, 128);
            primaryWrite.Location = new Point(8, 22);
            primaryWrite.Name = "primaryWrite";
            primaryWrite.Size = new Size(186, 39);
            primaryWrite.TabIndex = 0;
            primaryWrite.Text = "Not Performed";
            primaryWrite.UseVisualStyleBackColor = false;
            // 
            // groupBox22
            // 
            groupBox22.Controls.Add(groupBox29);
            groupBox22.Controls.Add(requestOperation);
            groupBox22.Controls.Add(groupBox26);
            groupBox22.Controls.Add(groupBox25);
            groupBox22.Location = new Point(385, 243);
            groupBox22.Name = "groupBox22";
            groupBox22.Size = new Size(282, 159);
            groupBox22.TabIndex = 11;
            groupBox22.TabStop = false;
            groupBox22.Text = "Controls";
            // 
            // groupBox29
            // 
            groupBox29.Controls.Add(expectedStage);
            groupBox29.Location = new Point(155, 81);
            groupBox29.Name = "groupBox29";
            groupBox29.Size = new Size(117, 53);
            groupBox29.TabIndex = 3;
            groupBox29.TabStop = false;
            groupBox29.Text = "Expected Stage";
            // 
            // expectedStage
            // 
            expectedStage.Enabled = false;
            expectedStage.Location = new Point(6, 22);
            expectedStage.Name = "expectedStage";
            expectedStage.Size = new Size(105, 23);
            expectedStage.TabIndex = 5;
            // 
            // requestOperation
            // 
            requestOperation.Location = new Point(155, 22);
            requestOperation.Name = "requestOperation";
            requestOperation.Size = new Size(117, 49);
            requestOperation.TabIndex = 3;
            requestOperation.Text = "Request";
            requestOperation.UseVisualStyleBackColor = true;
            // 
            // groupBox26
            // 
            groupBox26.Controls.Add(runOperation);
            groupBox26.Location = new Point(15, 81);
            groupBox26.Name = "groupBox26";
            groupBox26.Size = new Size(134, 53);
            groupBox26.TabIndex = 2;
            groupBox26.TabStop = false;
            groupBox26.Text = "Operation";
            // 
            // runOperation
            // 
            runOperation.FormattingEnabled = true;
            runOperation.Location = new Point(7, 22);
            runOperation.Name = "runOperation";
            runOperation.Size = new Size(121, 23);
            runOperation.TabIndex = 0;
            // 
            // groupBox25
            // 
            groupBox25.Controls.Add(runStage);
            groupBox25.Location = new Point(15, 22);
            groupBox25.Name = "groupBox25";
            groupBox25.Size = new Size(134, 53);
            groupBox25.TabIndex = 1;
            groupBox25.TabStop = false;
            groupBox25.Text = "Stage";
            // 
            // runStage
            // 
            runStage.FormattingEnabled = true;
            runStage.Location = new Point(7, 22);
            runStage.Name = "runStage";
            runStage.Size = new Size(121, 23);
            runStage.TabIndex = 0;
            // 
            // postBox
            // 
            postBox.Controls.Add(postRecords);
            postBox.Location = new Point(433, 408);
            postBox.Name = "postBox";
            postBox.Size = new Size(230, 306);
            postBox.TabIndex = 10;
            postBox.TabStop = false;
            postBox.Text = "Post-run Records";
            // 
            // postRecords
            // 
            postRecords.Location = new Point(6, 22);
            postRecords.Name = "postRecords";
            postRecords.Size = new Size(214, 278);
            postRecords.TabIndex = 3;
            // 
            // preliminaryBox
            // 
            preliminaryBox.Controls.Add(preliminaryRecords);
            preliminaryBox.Location = new Point(11, 408);
            preliminaryBox.Name = "preliminaryBox";
            preliminaryBox.Size = new Size(206, 306);
            preliminaryBox.TabIndex = 9;
            preliminaryBox.TabStop = false;
            preliminaryBox.Text = "Preliminary Records";
            // 
            // preliminaryRecords
            // 
            preliminaryRecords.Location = new Point(6, 22);
            preliminaryRecords.Name = "preliminaryRecords";
            preliminaryRecords.Size = new Size(190, 278);
            preliminaryRecords.TabIndex = 2;
            // 
            // groupBox20
            // 
            groupBox20.Controls.Add(groupBox2);
            groupBox20.Controls.Add(groupBox28);
            groupBox20.Controls.Add(groupBox3);
            groupBox20.Controls.Add(groupBox7);
            groupBox20.Controls.Add(groupBox5);
            groupBox20.Controls.Add(groupBox4);
            groupBox20.Controls.Add(groupBox6);
            groupBox20.Controls.Add(groupBox1);
            groupBox20.Location = new Point(11, 10);
            groupBox20.Name = "groupBox20";
            groupBox20.Size = new Size(656, 164);
            groupBox20.TabIndex = 11;
            groupBox20.TabStop = false;
            groupBox20.Text = "Run Parameters";
            // 
            // groupBox2
            // 
            groupBox2.Controls.Add(label3);
            groupBox2.Controls.Add(runEnergy);
            groupBox2.Location = new Point(6, 20);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(170, 57);
            groupBox2.TabIndex = 4;
            groupBox2.TabStop = false;
            groupBox2.Text = "Energy";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(112, 25);
            label3.Name = "label3";
            label3.Size = new Size(39, 15);
            label3.TabIndex = 3;
            label3.Text = "AMeV";
            // 
            // runEnergy
            // 
            runEnergy.Location = new Point(6, 22);
            runEnergy.Name = "runEnergy";
            runEnergy.Size = new Size(100, 23);
            runEnergy.TabIndex = 2;
            // 
            // groupBox28
            // 
            groupBox28.Controls.Add(label10);
            groupBox28.Controls.Add(textBox1);
            groupBox28.Location = new Point(572, 91);
            groupBox28.Name = "groupBox28";
            groupBox28.Size = new Size(80, 57);
            groupBox28.TabIndex = 10;
            groupBox28.TabStop = false;
            groupBox28.Text = "Temp";
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(59, 25);
            label10.Name = "label10";
            label10.Size = new Size(15, 15);
            label10.TabIndex = 4;
            label10.Text = "C";
            // 
            // textBox1
            // 
            textBox1.Location = new Point(6, 22);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(49, 23);
            textBox1.TabIndex = 2;
            // 
            // groupBox3
            // 
            groupBox3.Controls.Add(label4);
            groupBox3.Controls.Add(runSODIMM);
            groupBox3.Location = new Point(572, 22);
            groupBox3.Name = "groupBox3";
            groupBox3.Size = new Size(80, 57);
            groupBox3.TabIndex = 5;
            groupBox3.TabStop = false;
            groupBox3.Text = "SODIMM";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(112, 25);
            label4.Name = "label4";
            label4.Size = new Size(14, 15);
            label4.TabIndex = 3;
            label4.Text = "#";
            // 
            // runSODIMM
            // 
            runSODIMM.Location = new Point(6, 22);
            runSODIMM.Name = "runSODIMM";
            runSODIMM.Size = new Size(68, 23);
            runSODIMM.TabIndex = 2;
            // 
            // groupBox7
            // 
            groupBox7.Controls.Add(label8);
            groupBox7.Controls.Add(runAirgap);
            groupBox7.Location = new Point(182, 22);
            groupBox7.Name = "groupBox7";
            groupBox7.Size = new Size(162, 57);
            groupBox7.TabIndex = 9;
            groupBox7.TabStop = false;
            groupBox7.Text = "Air Gap";
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(112, 25);
            label8.Name = "label8";
            label8.Size = new Size(29, 15);
            label8.TabIndex = 5;
            label8.Text = "mm";
            // 
            // runAirgap
            // 
            runAirgap.Location = new Point(6, 22);
            runAirgap.Name = "runAirgap";
            runAirgap.Size = new Size(100, 23);
            runAirgap.TabIndex = 2;
            // 
            // groupBox5
            // 
            groupBox5.Controls.Add(label5);
            groupBox5.Controls.Add(runAngle);
            groupBox5.Location = new Point(182, 91);
            groupBox5.Name = "groupBox5";
            groupBox5.Size = new Size(162, 57);
            groupBox5.TabIndex = 7;
            groupBox5.TabStop = false;
            groupBox5.Text = "Angle";
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(112, 25);
            label5.Name = "label5";
            label5.Size = new Size(48, 15);
            label5.TabIndex = 4;
            label5.Text = "degrees";
            // 
            // runAngle
            // 
            runAngle.Location = new Point(6, 22);
            runAngle.Name = "runAngle";
            runAngle.Size = new Size(100, 23);
            runAngle.TabIndex = 2;
            // 
            // groupBox4
            // 
            groupBox4.Controls.Add(label9);
            groupBox4.Controls.Add(runIon);
            groupBox4.Location = new Point(6, 91);
            groupBox4.Name = "groupBox4";
            groupBox4.Size = new Size(170, 57);
            groupBox4.TabIndex = 6;
            groupBox4.TabStop = false;
            groupBox4.Text = "Ion";
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(112, 25);
            label9.Name = "label9";
            label9.Size = new Size(57, 15);
            label9.TabIndex = 4;
            label9.Text = "Sym-Iso#";
            // 
            // runIon
            // 
            runIon.Location = new Point(6, 22);
            runIon.Name = "runIon";
            runIon.Size = new Size(100, 23);
            runIon.TabIndex = 2;
            // 
            // groupBox6
            // 
            groupBox6.Controls.Add(label6);
            groupBox6.Controls.Add(runFlux);
            groupBox6.Location = new Point(350, 91);
            groupBox6.Name = "groupBox6";
            groupBox6.Size = new Size(212, 57);
            groupBox6.TabIndex = 8;
            groupBox6.TabStop = false;
            groupBox6.Text = "Flux";
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(96, 25);
            label6.Name = "label6";
            label6.Size = new Size(105, 15);
            label6.TabIndex = 4;
            label6.Text = "particles/(s*cm^2)";
            // 
            // runFlux
            // 
            runFlux.Location = new Point(6, 22);
            runFlux.Name = "runFlux";
            runFlux.Size = new Size(84, 23);
            runFlux.TabIndex = 2;
            // 
            // groupBox1
            // 
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(runLET);
            groupBox1.Location = new Point(350, 22);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(212, 57);
            groupBox1.TabIndex = 0;
            groupBox1.TabStop = false;
            groupBox1.Text = "LET";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(112, 25);
            label2.Name = "label2";
            label2.Size = new Size(78, 15);
            label2.TabIndex = 3;
            label2.Text = "MeV·cm²/mg";
            // 
            // runLET
            // 
            runLET.Location = new Point(6, 22);
            runLET.Name = "runLET";
            runLET.Size = new Size(100, 23);
            runLET.TabIndex = 2;
            // 
            // groupBox8
            // 
            groupBox8.Controls.Add(runDescription);
            groupBox8.Location = new Point(11, 180);
            groupBox8.Name = "groupBox8";
            groupBox8.Size = new Size(368, 138);
            groupBox8.TabIndex = 9;
            groupBox8.TabStop = false;
            groupBox8.Text = "Description";
            // 
            // runDescription
            // 
            runDescription.Location = new Point(6, 25);
            runDescription.Name = "runDescription";
            runDescription.Size = new Size(356, 105);
            runDescription.TabIndex = 0;
            runDescription.Text = "";
            // 
            // RunEditor
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            ClientSize = new Size(1273, 822);
            Controls.Add(splitContainer1);
            Name = "RunEditor";
            Text = "RunEditor";
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            splitContainer2.Panel1.ResumeLayout(false);
            splitContainer2.Panel1.PerformLayout();
            splitContainer2.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer2).EndInit();
            splitContainer2.ResumeLayout(false);
            groupBox18.ResumeLayout(false);
            groupBox19.ResumeLayout(false);
            groupBox9.ResumeLayout(false);
            groupBox9.PerformLayout();
            groupBox10.ResumeLayout(false);
            groupBox10.PerformLayout();
            groupBox13.ResumeLayout(false);
            groupBox17.ResumeLayout(false);
            groupBox17.PerformLayout();
            groupBox16.ResumeLayout(false);
            groupBox16.PerformLayout();
            groupBox15.ResumeLayout(false);
            splitContainer3.Panel1.ResumeLayout(false);
            splitContainer3.Panel1.PerformLayout();
            splitContainer3.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer3).EndInit();
            splitContainer3.ResumeLayout(false);
            groupBox21.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)runNumberEditor).EndInit();
            groupBox27.ResumeLayout(false);
            groupBox27.PerformLayout();
            dynamicBox.ResumeLayout(false);
            groupBox23.ResumeLayout(false);
            groupBox14.ResumeLayout(false);
            groupBox22.ResumeLayout(false);
            groupBox29.ResumeLayout(false);
            groupBox29.PerformLayout();
            groupBox26.ResumeLayout(false);
            groupBox25.ResumeLayout(false);
            postBox.ResumeLayout(false);
            preliminaryBox.ResumeLayout(false);
            groupBox20.ResumeLayout(false);
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            groupBox28.ResumeLayout(false);
            groupBox28.PerformLayout();
            groupBox3.ResumeLayout(false);
            groupBox3.PerformLayout();
            groupBox7.ResumeLayout(false);
            groupBox7.PerformLayout();
            groupBox5.ResumeLayout(false);
            groupBox5.PerformLayout();
            groupBox4.ResumeLayout(false);
            groupBox4.PerformLayout();
            groupBox6.ResumeLayout(false);
            groupBox6.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox8.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private SplitContainer splitContainer1;
        private GroupBox groupBox1;
        private GroupBox groupBox3;
        private Label label4;
        private TextBox runSODIMM;
        private GroupBox groupBox2;
        private Label label3;
        private TextBox runEnergy;
        private Label label2;
        private TextBox runLET;
        private GroupBox groupBox10;
        private TextBox modifiedTimestamp;
        private GroupBox groupBox9;
        private TextBox createdTimestamp;
        private GroupBox groupBox8;
        private RichTextBox runDescription;
        private GroupBox groupBox7;
        private Label label8;
        private TextBox runAirgap;
        private GroupBox groupBox6;
        private Label label6;
        private TextBox runFlux;
        private GroupBox groupBox5;
        private Label label5;
        private TextBox runAngle;
        private GroupBox groupBox4;
        private TextBox runIon;
        private SplitContainer splitContainer2;
        private Label label1;
        private GroupBox groupBox13;
        private CheckBox invertSort;
        private GroupBox groupBox16;
        private ComboBox sortBy;
        private GroupBox groupBox15;
        private ComboBox groupBy;
        private GroupBox groupBox18;
        private TreeView runBrowser;
        private GroupBox groupBox17;
        private TextBox lastUpdated;
        private SplitContainer splitContainer3;
        private Label runNumber;
        private Button submitRun;
        private Button deleteRun;
        private GroupBox groupBox20;
        private Label label9;
        private GroupBox groupBox19;
        private Button syncStatus;
        private GroupBox groupBox21;
        private GroupBox preliminaryBox;
        private GroupBox groupBox22;
        private GroupBox postBox;
        private GroupBox dynamicBox;
        private GroupBox groupBox23;
        private Button primaryVerify;
        private GroupBox groupBox14;
        private Button primaryWrite;
        private Button requestOperation;
        private GroupBox groupBox26;
        private ComboBox runOperation;
        private GroupBox groupBox25;
        private ComboBox runStage;
        private TreeView postRecords;
        private TreeView preliminaryRecords;
        private Button postLock;
        private Button primaryLock;
        private Button prelimaryLock;
        private NumericUpDown runNumberEditor;
        private GroupBox groupBox28;
        private Label label10;
        private TextBox textBox1;
        private GroupBox groupBox27;
        private Label label7;
        private TextBox runFluence;
        private GroupBox groupBox29;
        private TextBox expectedStage;
        private TreeView treeView1;
    }
}