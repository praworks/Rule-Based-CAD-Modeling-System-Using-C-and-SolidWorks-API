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
            this.bottomRow = new System.Windows.Forms.FlowLayoutPanel();
            this.thumbsRow = new System.Windows.Forms.FlowLayoutPanel();
            this.btnThumbUp = new System.Windows.Forms.Button();
            this.btnThumbDown = new System.Windows.Forms.Button();
            this.lblRealTimeStatus = new System.Windows.Forms.Label();
            this.build = new System.Windows.Forms.Button();
            this.prompt = new System.Windows.Forms.TextBox();
            this.presetRow = new System.Windows.Forms.Panel();
            this.shapePreset = new System.Windows.Forms.ComboBox();
            this.presetLabel = new System.Windows.Forms.Label();
            this.lblVersion = new System.Windows.Forms.Label();
            this.lblModified = new System.Windows.Forms.Label();
            this.contentTableLayout = new System.Windows.Forms.TableLayoutPanel();
            this.rootTableLayout.SuspendLayout();
            this.bottomRow.SuspendLayout();
            this.thumbsRow.SuspendLayout();
            this.presetRow.SuspendLayout();
            this.contentTableLayout.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnStatus
            // 
            this.btnStatus.Location = new System.Drawing.Point(570, 12);
            this.btnStatus.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.btnStatus.Name = "btnStatus";
            this.btnStatus.Size = new System.Drawing.Size(160, 38);
            this.btnStatus.TabIndex = 1;
            this.btnStatus.Text = "Status";
            this.btnStatus.UseVisualStyleBackColor = true;
            // 
            // btnHistory
            // 
            this.btnHistory.Location = new System.Drawing.Point(742, 12);
            this.btnHistory.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.btnHistory.Name = "btnHistory";
            this.btnHistory.Size = new System.Drawing.Size(160, 38);
            this.btnHistory.TabIndex = 2;
            this.btnHistory.Text = "History";
            this.btnHistory.UseVisualStyleBackColor = true;
            // 
            // btnSettings
            // 
            this.btnSettings.Location = new System.Drawing.Point(398, 12);
            this.btnSettings.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.btnSettings.Name = "btnSettings";
            this.btnSettings.Size = new System.Drawing.Size(160, 38);
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
            this.rootTableLayout.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.rootTableLayout.Name = "rootTableLayout";
            this.rootTableLayout.Padding = new System.Windows.Forms.Padding(12, 12, 12, 12);
            this.rootTableLayout.RowCount = 2;
            this.rootTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 62F));
            this.rootTableLayout.Size = new System.Drawing.Size(944, 1085);
            this.rootTableLayout.TabIndex = 0;
            // 
            
            // 
            // bottomRow
            // 
            this.bottomRow.Controls.Add(this.btnHistory);
            this.bottomRow.Controls.Add(this.btnStatus);
            this.bottomRow.Controls.Add(this.btnSettings);
            this.bottomRow.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.bottomRow.Location = new System.Drawing.Point(18, 1017);
            this.bottomRow.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.bottomRow.Name = "bottomRow";
            this.bottomRow.Padding = new System.Windows.Forms.Padding(0, 6, 0, 0);
            this.bottomRow.Size = new System.Drawing.Size(908, 50);
            this.bottomRow.TabIndex = 1;
            this.bottomRow.WrapContents = false;
            // 
            // thumbsRow
            // 
            this.thumbsRow.Controls.Add(this.btnThumbUp);
            this.thumbsRow.Controls.Add(this.btnThumbDown);
            this.thumbsRow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.thumbsRow.Location = new System.Drawing.Point(3, 183);
            this.thumbsRow.Name = "thumbsRow";
            this.thumbsRow.Padding = new System.Windows.Forms.Padding(6, 2, 6, 2);
            this.thumbsRow.Size = new System.Drawing.Size(448, 87);
            this.thumbsRow.TabIndex = 5;
            this.thumbsRow.WrapContents = false;
            // 
            // btnThumbUp
            // 
            this.btnThumbUp.Location = new System.Drawing.Point(6, 2);
            this.btnThumbUp.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            this.btnThumbUp.Name = "btnThumbUp";
            this.btnThumbUp.Size = new System.Drawing.Size(30, 30);
            this.btnThumbUp.TabIndex = 0;
            this.btnThumbUp.Text = "👍";
            this.btnThumbUp.UseVisualStyleBackColor = true;
            // 
            // btnThumbDown
            // 
            this.btnThumbDown.Location = new System.Drawing.Point(47, 5);
            this.btnThumbDown.Name = "btnThumbDown";
            this.btnThumbDown.Size = new System.Drawing.Size(30, 30);
            this.btnThumbDown.TabIndex = 1;
            this.btnThumbDown.Text = "👎";
            this.btnThumbDown.UseVisualStyleBackColor = true;
            // 
            // lblRealTimeStatus
            // 
            this.lblRealTimeStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblRealTimeStatus.ForeColor = System.Drawing.Color.DimGray;
            this.lblRealTimeStatus.Location = new System.Drawing.Point(3, 120);
            this.lblRealTimeStatus.Name = "lblRealTimeStatus";
            this.lblRealTimeStatus.Padding = new System.Windows.Forms.Padding(6, 4, 6, 4);
            this.lblRealTimeStatus.Size = new System.Drawing.Size(448, 30);
            this.lblRealTimeStatus.TabIndex = 3;
            this.lblRealTimeStatus.Text = "Ready";
            this.lblRealTimeStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // build
            // 
            this.build.Dock = System.Windows.Forms.DockStyle.Fill;
            this.build.Location = new System.Drawing.Point(3, 91);
            this.build.Name = "build";
            this.build.Size = new System.Drawing.Size(448, 26);
            this.build.TabIndex = 2;
            this.build.Text = "Build Model";
            this.build.UseVisualStyleBackColor = true;
            // 
            // prompt
            // 
            this.prompt.Dock = System.Windows.Forms.DockStyle.Fill;
            this.prompt.Location = new System.Drawing.Point(3, 31);
            this.prompt.Multiline = true;
            this.prompt.Name = "prompt";
            this.prompt.Size = new System.Drawing.Size(448, 54);
            this.prompt.TabIndex = 1;
            // 
            // presetRow
            // 
            this.presetRow.Controls.Add(this.shapePreset);
            this.presetRow.Controls.Add(this.presetLabel);
            this.presetRow.Controls.Add(this.lblVersion);
            this.presetRow.Controls.Add(this.lblModified);
            this.presetRow.Dock = System.Windows.Forms.DockStyle.Fill;
            this.presetRow.Location = new System.Drawing.Point(3, 3);
            this.presetRow.Name = "presetRow";
            this.presetRow.Size = new System.Drawing.Size(448, 22);
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
            this.shapePreset.Location = new System.Drawing.Point(86, 0);
            this.shapePreset.Name = "shapePreset";
            this.shapePreset.Size = new System.Drawing.Size(220, 33);
            this.shapePreset.TabIndex = 0;
            // 
            // presetLabel
            // 
            this.presetLabel.AutoSize = true;
            this.presetLabel.Dock = System.Windows.Forms.DockStyle.Left;
            this.presetLabel.Location = new System.Drawing.Point(0, 0);
            this.presetLabel.Name = "presetLabel";
            this.presetLabel.Padding = new System.Windows.Forms.Padding(0, 6, 6, 0);
            this.presetLabel.Size = new System.Drawing.Size(86, 31);
            this.presetLabel.TabIndex = 1;
            this.presetLabel.Text = "Preset:";
            // 
            // lblVersion
            // 
            this.lblVersion.AutoSize = true;
            this.lblVersion.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblVersion.ForeColor = System.Drawing.Color.DimGray;
            this.lblVersion.Location = new System.Drawing.Point(216, 0);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Padding = new System.Windows.Forms.Padding(0, 6, 6, 0);
            this.lblVersion.Size = new System.Drawing.Size(41, 31);
            this.lblVersion.TabIndex = 2;
            this.lblVersion.Text = "v?";
            // 
            // lblModified
            // 
            this.lblModified.AutoSize = true;
            this.lblModified.Dock = System.Windows.Forms.DockStyle.Right;
            this.lblModified.ForeColor = System.Drawing.Color.OrangeRed;
            this.lblModified.Location = new System.Drawing.Point(257, 0);
            this.lblModified.Name = "lblModified";
            this.lblModified.Padding = new System.Windows.Forms.Padding(0, 6, 6, 0);
            this.lblModified.Size = new System.Drawing.Size(191, 31);
            this.lblModified.TabIndex = 3;
            this.lblModified.Text = "Unsaved changes";
            this.lblModified.Visible = false;
            // 
            // contentTableLayout
            // 
            this.contentTableLayout.ColumnCount = 1;
            this.contentTableLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.contentTableLayout.Controls.Add(this.presetRow, 0, 0);
            this.contentTableLayout.Controls.Add(this.prompt, 0, 1);
            this.contentTableLayout.Controls.Add(this.build, 0, 2);
            this.contentTableLayout.Controls.Add(this.lblRealTimeStatus, 0, 3);
            this.contentTableLayout.Controls.Add(this.thumbsRow, 0, 5);
            this.contentTableLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentTableLayout.Location = new System.Drawing.Point(9, 9);
            this.contentTableLayout.Name = "contentTableLayout";
            this.contentTableLayout.RowCount = 7;
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 28F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 30F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 93F));
            this.contentTableLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.contentTableLayout.Size = new System.Drawing.Size(454, 514);
            this.contentTableLayout.TabIndex = 0;
            // 
            // TextToCADTaskpane
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 25F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.rootTableLayout);
            this.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.Name = "TextToCADTaskpane";
            this.Size = new System.Drawing.Size(944, 1085);
            this.rootTableLayout.ResumeLayout(false);
            this.bottomRow.ResumeLayout(false);
            this.thumbsRow.ResumeLayout(false);
            this.presetRow.ResumeLayout(false);
            this.presetRow.PerformLayout();
            this.contentTableLayout.ResumeLayout(false);
            this.contentTableLayout.PerformLayout();
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
    }
}
