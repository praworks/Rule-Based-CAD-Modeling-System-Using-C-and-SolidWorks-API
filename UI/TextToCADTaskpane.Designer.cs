namespace AICAD.UI
{
    partial class TextToCADTaskpane
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
            if (disposing)
            {
                _client?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnStatus = new System.Windows.Forms.Button();
            this.btnHistory = new System.Windows.Forms.Button();
            this.btnSettings = new System.Windows.Forms.Button();
            this.rootTableLayout = new System.Windows.Forms.TableLayoutPanel();
            this.contentTableLayout = new System.Windows.Forms.TableLayoutPanel();
            this.presetRow = new System.Windows.Forms.Panel();
            this.shapePreset = new System.Windows.Forms.ComboBox();
            this.presetLabel = new System.Windows.Forms.Label();
            this.lblVersion = new System.Windows.Forms.Label();
            this.lblModified = new System.Windows.Forms.Label();
            this.prompt = new System.Windows.Forms.TextBox();
            this.build = new System.Windows.Forms.Button();
            this.lblRealTimeStatus = new System.Windows.Forms.Label();
            this.log = new System.Windows.Forms.Label();
            this.thumbsRow = new System.Windows.Forms.FlowLayoutPanel();
            this.btnThumbUp = new System.Windows.Forms.Button();
            this.btnThumbDown = new System.Windows.Forms.Button();
            this.bottomRow = new System.Windows.Forms.FlowLayoutPanel();
            this.rootTableLayout.SuspendLayout();
            this.contentTableLayout.SuspendLayout();
            this.presetRow.SuspendLayout();
            this.thumbsRow.SuspendLayout();
            this.bottomRow.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnStatus
            // 
            this.btnStatus.Location = new System.Drawing.Point(285, 6);
            this.btnStatus.Name = "btnStatus";
            this.btnStatus.Size = new System.Drawing.Size(80, 20);
            this.btnStatus.TabIndex = 1;
            this.btnStatus.Text = "Status";
            this.btnStatus.UseVisualStyleBackColor = true;
            // 
            // btnHistory
            // 
            this.btnHistory.Location = new System.Drawing.Point(371, 6);
            this.btnHistory.Name = "btnHistory";
            this.btnHistory.Size = new System.Drawing.Size(80, 20);
            this.btnHistory.TabIndex = 2;
            this.btnHistory.Text = "History";
            this.btnHistory.UseVisualStyleBackColor = true;
            // 
            // btnSettings
            // 
            this.btnSettings.Location = new System.Drawing.Point(199, 6);
            this.btnSettings.Name = "btnSettings";
            this.btnSettings.Size = new System.Drawing.Size(80, 20);
            this.btnSettings.TabIndex = 0;
            this.btnSettings.Text = "Settings";
            this.btnSettings.UseVisualStyleBackColor = true;
            // 
            // rootTableLayout
            // 
            this.rootTableLayout.ColumnCount = 1;
            this.rootTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.rootTableLayout.Controls.Add(this.contentTableLayout, 0, 0);
            this.rootTableLayout.Controls.Add(this.bottomRow, 0, 1);
            this.rootTableLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootTableLayout.Location = new System.Drawing.Point(0, 0);
            this.rootTableLayout.Name = "rootTableLayout";
            this.rootTableLayout.Padding = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.rootTableLayout.RowCount = 2;
            this.rootTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.rootTableLayout.Size = new System.Drawing.Size(472, 564);
            this.rootTableLayout.TabIndex = 0;
            // 
            // contentTableLayout
            // 
            this.contentTableLayout.ColumnCount = 1;
            this.contentTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.contentTableLayout.Controls.Add(this.presetRow, 0, 0);
            this.contentTableLayout.Controls.Add(this.prompt, 0, 1);
            this.contentTableLayout.Controls.Add(this.build, 0, 2);
            this.contentTableLayout.Controls.Add(this.lblRealTimeStatus, 0, 3);
            this.contentTableLayout.Controls.Add(this.log, 0, 4);
            this.contentTableLayout.Controls.Add(this.thumbsRow, 0, 5);
            this.contentTableLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentTableLayout.Location = new System.Drawing.Point(8, 8);
            this.contentTableLayout.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.contentTableLayout.Name = "contentTableLayout";
            this.contentTableLayout.RowCount = 7;
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 15F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 31F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 17F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 16F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 16F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.contentTableLayout.Size = new System.Drawing.Size(456, 516);
            this.contentTableLayout.TabIndex = 0;
            // 
            // presetRow
            // 
            this.presetRow.Controls.Add(this.shapePreset);
            this.presetRow.Controls.Add(this.presetLabel);
            this.presetRow.Controls.Add(this.lblVersion);
            this.presetRow.Controls.Add(this.lblModified);
            this.presetRow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.presetRow.Location = new System.Drawing.Point(2, 2);
            this.presetRow.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.presetRow.Name = "presetRow";
            this.presetRow.Size = new System.Drawing.Size(452, 11);
            this.presetRow.TabIndex = 0;
            // 
            // shapePreset
            // 
            this.shapePreset.Dock = System.Windows.Forms.DockStyle.Left;
            this.shapePreset.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.shapePreset.Items.AddRange(new object[] {
            "— none —",
            "Box 100x50x25 mm",
            "Cylinder Ø40 x 80 mm",
            "Box 10x10x10 mm"});
            this.shapePreset.Location = new System.Drawing.Point(43, 0);
            this.shapePreset.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.shapePreset.Name = "shapePreset";
            this.shapePreset.Size = new System.Drawing.Size(112, 21);
            this.shapePreset.TabIndex = 0;
            // 
            // presetLabel
            // 
            this.presetLabel.AutoSize = true;
            this.presetLabel.Dock = System.Windows.Forms.DockStyle.Left;
            this.presetLabel.Location = new System.Drawing.Point(0, 0);
            this.presetLabel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.presetLabel.Name = "presetLabel";
            this.presetLabel.Padding = new System.Windows.Forms.Padding(0, 3, 3, 0);
            this.presetLabel.Size = new System.Drawing.Size(43, 16);
            this.presetLabel.TabIndex = 1;
            this.presetLabel.Text = "Preset:";
            // 
            // lblVersion
            // 
            this.lblVersion.AutoSize = true;
            this.lblVersion.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblVersion.ForeColor = System.Drawing.Color.DimGray;
            this.lblVersion.Location = new System.Drawing.Point(333, 0);
            this.lblVersion.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Padding = new System.Windows.Forms.Padding(0, 3, 3, 0);
            this.lblVersion.Size = new System.Drawing.Size(22, 16);
            this.lblVersion.TabIndex = 2;
            this.lblVersion.Text = "v?";
            // 
            // lblModified
            // 
            this.lblModified.AutoSize = true;
            this.lblModified.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblModified.ForeColor = System.Drawing.Color.OrangeRed;
            this.lblModified.Location = new System.Drawing.Point(355, 0);
            this.lblModified.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblModified.Name = "lblModified";
            this.lblModified.Padding = new System.Windows.Forms.Padding(0, 3, 3, 0);
            this.lblModified.Size = new System.Drawing.Size(97, 16);
            this.lblModified.TabIndex = 3;
            this.lblModified.Text = "Unsaved changes";
            this.lblModified.Visible = false;
            // 
            // prompt
            // 
            this.prompt.Dock = System.Windows.Forms.DockStyle.Fill;
            this.prompt.Location = new System.Drawing.Point(2, 17);
            this.prompt.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.prompt.Multiline = true;
            this.prompt.Name = "prompt";
            this.prompt.Size = new System.Drawing.Size(452, 27);
            this.prompt.TabIndex = 1;
            // 
            // build
            // 
            this.build.Dock = System.Windows.Forms.DockStyle.Fill;
            this.build.Location = new System.Drawing.Point(2, 48);
            this.build.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.build.Name = "build";
            this.build.Size = new System.Drawing.Size(452, 13);
            this.build.TabIndex = 2;
            this.build.Text = "Build Model";
            this.build.UseVisualStyleBackColor = true;
            // 
            // lblRealTimeStatus
            // 
            this.lblRealTimeStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRealTimeStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblRealTimeStatus.Location = new System.Drawing.Point(2, 63);
            this.lblRealTimeStatus.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblRealTimeStatus.Name = "lblRealTimeStatus";
            this.lblRealTimeStatus.Padding = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.lblRealTimeStatus.Size = new System.Drawing.Size(452, 16);
            this.lblRealTimeStatus.TabIndex = 3;
            this.lblRealTimeStatus.Text = "Ready";
            this.lblRealTimeStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // log
            // 
            this.log.Dock = System.Windows.Forms.DockStyle.Fill;
            this.log.ForeColor = System.Drawing.Color.DimGray;
            this.log.Location = new System.Drawing.Point(2, 79);
            this.log.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.log.Name = "log";
            this.log.Padding = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.log.Size = new System.Drawing.Size(452, 16);
            this.log.TabIndex = 4;
            this.log.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // thumbsRow
            // 
            this.thumbsRow.Controls.Add(this.btnThumbUp);
            this.thumbsRow.Controls.Add(this.btnThumbDown);
            this.thumbsRow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.thumbsRow.Location = new System.Drawing.Point(2, 97);
            this.thumbsRow.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.thumbsRow.Name = "thumbsRow";
            this.thumbsRow.Padding = new System.Windows.Forms.Padding(3, 1, 3, 1);
            this.thumbsRow.Size = new System.Drawing.Size(452, 44);
            this.thumbsRow.TabIndex = 5;
            this.thumbsRow.WrapContents = false;
            // 
            // btnThumbUp
            // 
            this.btnThumbUp.Location = new System.Drawing.Point(3, 1);
            this.btnThumbUp.Margin = new System.Windows.Forms.Padding(0, 0, 4, 0);
            this.btnThumbUp.Name = "btnThumbUp";
            this.btnThumbUp.Size = new System.Drawing.Size(15, 16);
            this.btnThumbUp.TabIndex = 0;
            this.btnThumbUp.Text = "👍";
            this.btnThumbUp.UseVisualStyleBackColor = true;
            // 
            // btnThumbDown
            // 
            this.btnThumbDown.Location = new System.Drawing.Point(24, 3);
            this.btnThumbDown.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.btnThumbDown.Name = "btnThumbDown";
            this.btnThumbDown.Size = new System.Drawing.Size(15, 16);
            this.btnThumbDown.TabIndex = 1;
            this.btnThumbDown.Text = "👎";
            this.btnThumbDown.UseVisualStyleBackColor = true;
            // 
            // bottomRow
            // 
            this.bottomRow.Controls.Add(this.btnHistory);
            this.bottomRow.Controls.Add(this.btnStatus);
            this.bottomRow.Controls.Add(this.btnSettings);
            this.bottomRow.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.bottomRow.Location = new System.Drawing.Point(9, 529);
            this.bottomRow.Name = "bottomRow";
            this.bottomRow.Padding = new System.Windows.Forms.Padding(0, 3, 0, 0);
            this.bottomRow.Size = new System.Drawing.Size(454, 26);
            this.bottomRow.TabIndex = 1;
            this.bottomRow.WrapContents = false;
            // 
            // TextToCADTaskpane
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.rootTableLayout);
            this.Name = "TextToCADTaskpane";
            this.Size = new System.Drawing.Size(472, 564);
            this.rootTableLayout.ResumeLayout(false);
            this.contentTableLayout.ResumeLayout(false);
            this.contentTableLayout.PerformLayout();
            this.presetRow.ResumeLayout(false);
            this.presetRow.PerformLayout();
            this.thumbsRow.ResumeLayout(false);
            this.bottomRow.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Button btnStatus;
        private System.Windows.Forms.Button btnHistory;
        private System.Windows.Forms.Button btnSettings;
        private System.Windows.Forms.TableLayoutPanel rootTableLayout;
        private System.Windows.Forms.FlowLayoutPanel bottomRow;
        private System.Windows.Forms.TableLayoutPanel contentTableLayout;
        private System.Windows.Forms.Panel presetRow;
        private System.Windows.Forms.ComboBox shapePreset;
        private System.Windows.Forms.Label presetLabel;
        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.Label lblModified;
        private System.Windows.Forms.TextBox prompt;
        private System.Windows.Forms.Button build;
        private System.Windows.Forms.Label lblRealTimeStatus;
        private System.Windows.Forms.FlowLayoutPanel thumbsRow;
        private System.Windows.Forms.Button btnThumbUp;
        private System.Windows.Forms.Button btnThumbDown;
        private System.Windows.Forms.Label log;
    }
}
