using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MFCServer1
{
    public partial class InspectionDetailForm : Form
    {
        public InspectionDetailForm(ServerMonitor.InspectionRecord rec)
        {
            InitializeComponent();

            // 값 채우기
            lblTimeVal.Text = rec.Time.ToString("yyyy-MM-dd HH:mm:ss");
            lblResultVal.Text = rec.Result;
            lblReasonVal.Text = rec.Reason;
            lblTopPathVal.Text = rec.TopPath;
            lblSidePathVal.Text = rec.SidePath;

            // 결과 강조 색상만 변경 (Regular 유지)
            if (rec.Result.Contains("비정상") || rec.Result.Contains("에러"))
            {
                lblResultVal.ForeColor = Color.Red;
            }
            else if (rec.Result.Contains("정상"))
            {
                lblResultVal.ForeColor = Color.Green;
            }
            else
            {
                lblResultVal.ForeColor = Color.Black;
            }

            // 이미지 로드
            if (!string.IsNullOrEmpty(rec.TopPath) && File.Exists(rec.TopPath))
            {
                try { picTopLarge.Image = LoadImageNoLock(rec.TopPath); } catch { }
            }

            if (!string.IsNullOrEmpty(rec.SidePath) && File.Exists(rec.SidePath))
            {
                try { picSideLarge.Image = LoadImageNoLock(rec.SidePath); } catch { }
            }

            // 닫기 버튼은 여기서만 연결 → 디자이너는 이 코드 안 돈다
            btnClose.Click += BtnClose_Click;
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private Image LoadImageNoLock(string path)
        {
            using (var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
            {
                var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;
                return Image.FromStream(ms);
            }
        }
    }
}
