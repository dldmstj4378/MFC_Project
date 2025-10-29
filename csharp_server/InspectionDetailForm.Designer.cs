using System.Windows.Forms;
using System.Drawing;

namespace MFCServer1
{
    partial class InspectionDetailForm
    {
        private System.ComponentModel.IContainer components = null;

        private Label lblHeader;

        private Label lblTime;
        private Label lblResult;
        private Label lblReason;
        private Label lblTopPath;
        private Label lblSidePath;

        private Label lblTimeVal;
        private Label lblResultVal;
        private Label lblReasonVal;
        private Label lblTopPathVal;
        private Label lblSidePathVal;

        private PictureBox picTopLarge;
        private PictureBox picSideLarge;

        private Button btnClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblHeader = new System.Windows.Forms.Label();
            this.btnClose = new System.Windows.Forms.Button();
            this.lblTime = new System.Windows.Forms.Label();
            this.lblResult = new System.Windows.Forms.Label();
            this.lblReason = new System.Windows.Forms.Label();
            this.lblTopPath = new System.Windows.Forms.Label();
            this.lblSidePath = new System.Windows.Forms.Label();
            this.lblTimeVal = new System.Windows.Forms.Label();
            this.lblResultVal = new System.Windows.Forms.Label();
            this.lblReasonVal = new System.Windows.Forms.Label();
            this.lblTopPathVal = new System.Windows.Forms.Label();
            this.lblSidePathVal = new System.Windows.Forms.Label();
            this.picTopLarge = new System.Windows.Forms.PictureBox();
            this.picSideLarge = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.picTopLarge)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picSideLarge)).BeginInit();
            this.SuspendLayout();
            // 
            // lblHeader
            // 
            this.lblHeader.Font = new System.Drawing.Font("맑은 고딕", 10F, System.Drawing.FontStyle.Bold);
            this.lblHeader.ForeColor = System.Drawing.Color.Black;
            this.lblHeader.Location = new System.Drawing.Point(10, 8);
            this.lblHeader.Name = "lblHeader";
            this.lblHeader.Size = new System.Drawing.Size(200, 24);
            this.lblHeader.TabIndex = 0;
            this.lblHeader.Text = "검사 상세";
            this.lblHeader.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // btnClose
            // 
            this.btnClose.Location = new System.Drawing.Point(900, 10);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(80, 28);
            this.btnClose.TabIndex = 1;
            this.btnClose.Text = "닫기";
            this.btnClose.UseVisualStyleBackColor = true;
            // 
            // lblTime
            // 
            this.lblTime.Font = new System.Drawing.Font("맑은 고딕", 9F);
            this.lblTime.ForeColor = System.Drawing.Color.Black;
            this.lblTime.Location = new System.Drawing.Point(20, 40);
            this.lblTime.Name = "lblTime";
            this.lblTime.Size = new System.Drawing.Size(75, 20);
            this.lblTime.TabIndex = 2;
            this.lblTime.Text = "검사 시간:";
            this.lblTime.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblResult
            // 
            this.lblResult.Font = new System.Drawing.Font("맑은 고딕", 9F);
            this.lblResult.ForeColor = System.Drawing.Color.Black;
            this.lblResult.Location = new System.Drawing.Point(20, 65);
            this.lblResult.Name = "lblResult";
            this.lblResult.Size = new System.Drawing.Size(75, 20);
            this.lblResult.TabIndex = 4;
            this.lblResult.Text = "최종 결과:";
            this.lblResult.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblReason
            // 
            this.lblReason.Font = new System.Drawing.Font("맑은 고딕", 9F);
            this.lblReason.ForeColor = System.Drawing.Color.Black;
            this.lblReason.Location = new System.Drawing.Point(20, 90);
            this.lblReason.Name = "lblReason";
            this.lblReason.Size = new System.Drawing.Size(75, 20);
            this.lblReason.TabIndex = 6;
            this.lblReason.Text = "사유:";
            this.lblReason.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblTopPath
            // 
            this.lblTopPath.Font = new System.Drawing.Font("맑은 고딕", 9F);
            this.lblTopPath.ForeColor = System.Drawing.Color.Black;
            this.lblTopPath.Location = new System.Drawing.Point(20, 135);
            this.lblTopPath.Name = "lblTopPath";
            this.lblTopPath.Size = new System.Drawing.Size(75, 20);
            this.lblTopPath.TabIndex = 8;
            this.lblTopPath.Text = "TOP 경로:";
            this.lblTopPath.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblSidePath
            // 
            this.lblSidePath.Font = new System.Drawing.Font("맑은 고딕", 9F);
            this.lblSidePath.ForeColor = System.Drawing.Color.Black;
            this.lblSidePath.Location = new System.Drawing.Point(20, 160);
            this.lblSidePath.Name = "lblSidePath";
            this.lblSidePath.Size = new System.Drawing.Size(75, 20);
            this.lblSidePath.TabIndex = 10;
            this.lblSidePath.Text = "SIDE 경로:";
            this.lblSidePath.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // lblTimeVal
            // 
            this.lblTimeVal.Font = new System.Drawing.Font("맑은 고딕", 9F);
            this.lblTimeVal.ForeColor = System.Drawing.Color.Black;
            this.lblTimeVal.Location = new System.Drawing.Point(100, 40);
            this.lblTimeVal.Name = "lblTimeVal";
            this.lblTimeVal.Size = new System.Drawing.Size(300, 20);
            this.lblTimeVal.TabIndex = 3;
            // 
            // lblResultVal
            // 
            this.lblResultVal.Font = new System.Drawing.Font("맑은 고딕", 9F);
            this.lblResultVal.ForeColor = System.Drawing.Color.Black;
            this.lblResultVal.Location = new System.Drawing.Point(100, 65);
            this.lblResultVal.Name = "lblResultVal";
            this.lblResultVal.Size = new System.Drawing.Size(300, 20);
            this.lblResultVal.TabIndex = 5;
            // 
            // lblReasonVal
            // 
            this.lblReasonVal.AutoEllipsis = true;
            this.lblReasonVal.Font = new System.Drawing.Font("맑은 고딕", 9F);
            this.lblReasonVal.ForeColor = System.Drawing.Color.Black;
            this.lblReasonVal.Location = new System.Drawing.Point(100, 90);
            this.lblReasonVal.Name = "lblReasonVal";
            this.lblReasonVal.Size = new System.Drawing.Size(430, 35);
            this.lblReasonVal.TabIndex = 7;
            // 
            // lblTopPathVal
            // 
            this.lblTopPathVal.AutoEllipsis = true;
            this.lblTopPathVal.Font = new System.Drawing.Font("맑은 고딕", 8.5F);
            this.lblTopPathVal.ForeColor = System.Drawing.Color.DimGray;
            this.lblTopPathVal.Location = new System.Drawing.Point(100, 135);
            this.lblTopPathVal.Name = "lblTopPathVal";
            this.lblTopPathVal.Size = new System.Drawing.Size(430, 18);
            this.lblTopPathVal.TabIndex = 9;
            // 
            // lblSidePathVal
            // 
            this.lblSidePathVal.AutoEllipsis = true;
            this.lblSidePathVal.Font = new System.Drawing.Font("맑은 고딕", 8.5F);
            this.lblSidePathVal.ForeColor = System.Drawing.Color.DimGray;
            this.lblSidePathVal.Location = new System.Drawing.Point(100, 160);
            this.lblSidePathVal.Name = "lblSidePathVal";
            this.lblSidePathVal.Size = new System.Drawing.Size(430, 18);
            this.lblSidePathVal.TabIndex = 11;
            // 
            // picTopLarge
            // 
            this.picTopLarge.BackColor = System.Drawing.Color.Black;
            this.picTopLarge.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picTopLarge.Location = new System.Drawing.Point(15, 200);
            this.picTopLarge.Name = "picTopLarge";
            this.picTopLarge.Size = new System.Drawing.Size(460, 360);
            this.picTopLarge.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picTopLarge.TabIndex = 12;
            this.picTopLarge.TabStop = false;
            // 
            // picSideLarge
            // 
            this.picSideLarge.BackColor = System.Drawing.Color.Black;
            this.picSideLarge.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picSideLarge.Location = new System.Drawing.Point(510, 200);
            this.picSideLarge.Name = "picSideLarge";
            this.picSideLarge.Size = new System.Drawing.Size(460, 360);
            this.picSideLarge.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.picSideLarge.TabIndex = 13;
            this.picSideLarge.TabStop = false;
            // 
            // InspectionDetailForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.WhiteSmoke;
            this.ClientSize = new System.Drawing.Size(1000, 600);
            this.Controls.Add(this.lblHeader);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.lblTime);
            this.Controls.Add(this.lblTimeVal);
            this.Controls.Add(this.lblResult);
            this.Controls.Add(this.lblResultVal);
            this.Controls.Add(this.lblReason);
            this.Controls.Add(this.lblReasonVal);
            this.Controls.Add(this.lblTopPath);
            this.Controls.Add(this.lblTopPathVal);
            this.Controls.Add(this.lblSidePath);
            this.Controls.Add(this.lblSidePathVal);
            this.Controls.Add(this.picTopLarge);
            this.Controls.Add(this.picSideLarge);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "InspectionDetailForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "검사 상세";
            ((System.ComponentModel.ISupportInitialize)(this.picTopLarge)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picSideLarge)).EndInit();
            this.ResumeLayout(false);

        }
    }
}
