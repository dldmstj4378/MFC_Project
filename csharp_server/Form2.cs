using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MFCServer1
{
    public partial class Form2 : Form
    {
        private Timer _uiTimer;
        private List<ServerMonitor.InspectionRecord> _logsTabDayRaw = new List<ServerMonitor.InspectionRecord>();
        private List<object> _lastRealtimeViewRows = new List<object>();

        public Form2()
        {
            InitializeComponent();

            // 실시간 모니터
            _uiTimer = new Timer();
            _uiTimer.Interval = 1000;
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            dataGridLogs.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var rec = RowToRecordFromRealtime(e.RowIndex);
                if (rec == null) return;
                using (var dlg = new InspectionDetailForm(rec))
                    dlg.ShowDialog(this);
            };

            // 생산현황
            btnRefreshStats.Click += (s, e) =>
            {
                var dayLogs = DatabaseService.GetDailyInspections(dtStatsDate.Value.Date);
                UpdateStatsCharts(dayLogs); // 이제 dayLogs를 직접 넣는다
            };

            // 생산로그
            btnRefreshLogs.Click += (s, e) =>
            {
                _logsTabDayRaw = DatabaseService.GetDailyInspections(dtLogsDate.Value.Date);
                RefreshLogsGrid();
            };
            rdoFilterAll.CheckedChanged += (s, e) => { if (rdoFilterAll.Checked) RefreshLogsGrid(); };
            rdoFilterOK.CheckedChanged += (s, e) => { if (rdoFilterOK.Checked) RefreshLogsGrid(); };
            rdoFilterNG.CheckedChanged += (s, e) => { if (rdoFilterNG.Checked) RefreshLogsGrid(); };

            dgvLogs.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var rec = RowToRecordFromLogsTab(e.RowIndex);
                if (rec == null) return;
                using (var dlg = new InspectionDetailForm(rec))
                    dlg.ShowDialog(this);
            };
        }

        // ─────────────────────────────
        // 탭1 실시간 모니터
        // ─────────────────────────────
        private void UiTimer_Tick(object sender, EventArgs e)
        {
            // DB에서 최신 N개 가져온다
            var latestLogs = DatabaseService
                .GetRecentInspections(200)
                .OrderByDescending(x => x.Time)
                .ToList();

            // KPI
            int total = latestLogs.Count;
            int okCount = latestLogs.Count(r =>
                r.Result.Contains("정상") &&
                !r.Result.Contains("비정상") &&
                !r.Result.Contains("에러"));
            int ngCount = total - okCount;
            double rate = (total > 0) ? (ngCount * 100.0 / total) : 0.0;

            lblTotalValue.Text = total.ToString();
            lblOkValue.Text = okCount.ToString();
            lblNgValue.Text = ngCount.ToString();
            lblRateValue.Text = $"{rate:0.0} %";

            // 그리드 view rows
            var newViewRows = latestLogs
                .Select((r, idx) => new
                {
                    번호 = latestLogs.Count - idx,
                    시간 = r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                    결과 = r.Result,
                    불량사유 = r.Reason,
                    TOP경로 = r.TopPath,
                    SIDE경로 = r.SidePath
                })
                .ToList<object>();

            string selKey = GetCurrentRealtimeSelectionKey();
            bool changed = DifferentRealtimeRows(_lastRealtimeViewRows, newViewRows);

            if (changed)
            {
                dataGridLogs.DataSource = null;
                dataGridLogs.DataSource = newViewRows;
                _lastRealtimeViewRows = newViewRows;
                dataGridLogs.AutoResizeColumns();
            }

            if (!string.IsNullOrEmpty(selKey))
                RestoreRealtimeSelection(selKey);

            // 상태 라벨
            lblTcpStatus.Text = "TCP STATUS: " + ServerMonitor.ServerStatus;
            lblLastClient.Text = "LastClient: " + ServerMonitor.LastClientInfo;
            lblPythonError.Text = "LastError: " + ServerMonitor.PythonLastErrorMessage;
            lblPythonStatus.Text = ServerMonitor.PythonAlive ? "PYTHON OK" : "PYTHON DOWN";
            lblPythonStatus.ForeColor = ServerMonitor.PythonAlive ? Color.Green : Color.Red;

            // 최근 이미지
            if (latestLogs.Count > 0)
            {
                var newest = latestLogs[0];
                lblLastResult.Text = "LastResult: " + newest.Result;

                TryLoadPreviewImage(newest.TopPath, picTop);
                TryLoadPreviewImage(newest.SidePath, picSide);
            }
        }

        private string GetCurrentRealtimeSelectionKey()
        {
            if (dataGridLogs.CurrentRow == null) return null;
            if (dataGridLogs.CurrentRow.Index < 0) return null;
            var row = dataGridLogs.CurrentRow;
            string t = Convert.ToString(row.Cells["시간"].Value);
            string top = Convert.ToString(row.Cells["TOP경로"].Value);
            string sid = Convert.ToString(row.Cells["SIDE경로"].Value);
            return $"{t}|{top}|{sid}";
        }

        private void RestoreRealtimeSelection(string selKey)
        {
            if (string.IsNullOrEmpty(selKey)) return;
            foreach (DataGridViewRow r in dataGridLogs.Rows)
            {
                string t = Convert.ToString(r.Cells["시간"].Value);
                string top = Convert.ToString(r.Cells["TOP경로"].Value);
                string sid = Convert.ToString(r.Cells["SIDE경로"].Value);
                string key = $"{t}|{top}|{sid}";
                if (key == selKey)
                {
                    dataGridLogs.CurrentCell = r.Cells[0];
                    r.Selected = true;
                    dataGridLogs.FirstDisplayedScrollingRowIndex = r.Index;
                    break;
                }
            }
        }

        private bool DifferentRealtimeRows(List<object> a, List<object> b)
        {
            if (a.Count != b.Count) return true;
            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i]))
                    return true;
            }
            return false;
        }

        private ServerMonitor.InspectionRecord RowToRecordFromRealtime(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= dataGridLogs.Rows.Count)
                return null;

            string timeText = Convert.ToString(dataGridLogs.Rows[rowIndex].Cells["시간"].Value);
            string topPath = Convert.ToString(dataGridLogs.Rows[rowIndex].Cells["TOP경로"].Value);
            string sidePath = Convert.ToString(dataGridLogs.Rows[rowIndex].Cells["SIDE경로"].Value);

            // 방금 UI에 쓴 DB기반 리스트에서 다시 찾아오기
            var latestLogs = DatabaseService.GetRecentInspections(200);
            var rec = latestLogs.FirstOrDefault(r =>
                r.Time.ToString("yyyy-MM-dd HH:mm:ss") == timeText &&
                r.TopPath == topPath &&
                r.SidePath == sidePath
            );
            return rec;
        }

        private void TryLoadPreviewImage(string path, PictureBox box)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                box.Image = null;
                return;
            }
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var ms = new MemoryStream();
                    fs.CopyTo(ms);
                    ms.Position = 0;
                    box.Image = Image.FromStream(ms);
                }
            }
            catch { }
        }

        // ─────────────────────────────
        // 탭2 생산현황 (차트/요약)
        // ─────────────────────────────
        private void UpdateStatsCharts(List<ServerMonitor.InspectionRecord> dayLogs)
        {
            // dayLogs는 하루치(DB에서 이미 필터한 결과)
            int total = dayLogs.Count;
            int okCount = dayLogs.Count(r =>
                r.Result.Contains("정상") &&
                !r.Result.Contains("비정상") &&
                !r.Result.Contains("에러"));
            int ngCount = total - okCount;
            double rate = (total > 0) ? (ngCount * 100.0 / total) : 0.0;

            lblStatsSummaryTotal.Text = $"총 검사: {total}";
            lblStatsSummaryOk.Text = $"정상: {okCount}";
            lblStatsSummaryNg.Text = $"불량: {ngCount}";
            lblStatsSummaryRate.Text = $"불량률: {rate:0.0} %";

            // 파이
            var pieSeries = chartPie.Series["PieSeries"];
            pieSeries.Points.Clear();
            int pOK = pieSeries.Points.AddY(okCount);
            pieSeries.Points[pOK].LegendText = "정상";
            pieSeries.Points[pOK].Label = $"정상 {okCount}";
            pieSeries.Points[pOK].Color = Color.FromArgb(0, 128, 0);

            int pNG = pieSeries.Points.AddY(ngCount);
            pieSeries.Points[pNG].LegendText = "불량";
            pieSeries.Points[pNG].Label = $"불량 {ngCount}";
            pieSeries.Points[pNG].Color = Color.Red;

            // 막대 (시간대별)
            var grouped = dayLogs
                .GroupBy(r => r.Time.Hour)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    Hour = g.Key,
                    Ok = g.Count(x =>
                        x.Result.Contains("정상") &&
                        !x.Result.Contains("비정상") &&
                        !x.Result.Contains("에러")),
                    Ng = g.Count(x =>
                        !(x.Result.Contains("정상") &&
                          !x.Result.Contains("비정상") &&
                          !x.Result.Contains("에러")))
                })
                .ToList();

            var seriesOK = chartBar.Series["정상"];
            var seriesNG = chartBar.Series["불량"];
            seriesOK.Points.Clear();
            seriesNG.Points.Clear();

            foreach (var h in grouped)
            {
                string hourLabel = h.Hour.ToString("D2") + "시";
                int a = seriesOK.Points.AddXY(hourLabel, h.Ok);
                seriesOK.Points[a].ToolTip = $"정상 {h.Ok}";
                int b2 = seriesNG.Points.AddXY(hourLabel, h.Ng);
                seriesNG.Points[b2].ToolTip = $"불량 {h.Ng}";
            }
        }

        // ─────────────────────────────
        // 탭3 생산로그
        // ─────────────────────────────
        private void RefreshLogsGrid()
        {
            IEnumerable<ServerMonitor.InspectionRecord> filtered = _logsTabDayRaw;

            if (rdoFilterOK.Checked)
            {
                filtered = filtered.Where(r =>
                    r.Result.Contains("정상") &&
                    !r.Result.Contains("비정상") &&
                    !r.Result.Contains("에러"));
            }
            else if (rdoFilterNG.Checked)
            {
                filtered = filtered.Where(r =>
                    !(r.Result.Contains("정상") &&
                      !r.Result.Contains("비정상") &&
                      !r.Result.Contains("에러")));
            }

            var viewData = filtered
                .Select((r, idx) => new
                {
                    번호 = idx + 1,
                    시간 = r.Time.ToString("HH:mm:ss"),
                    결과 = r.Result,
                    불량사유 = r.Reason,
                    TOP = r.TopPath,
                    SIDE = r.SidePath
                })
                .ToList();

            dgvLogs.DataSource = viewData;
            dgvLogs.AutoResizeColumns();
            dgvLogs.Refresh();
        }

        private ServerMonitor.InspectionRecord RowToRecordFromLogsTab(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= dgvLogs.Rows.Count)
                return null;

            string t = Convert.ToString(dgvLogs.Rows[rowIndex].Cells["시간"].Value);
            string top = Convert.ToString(dgvLogs.Rows[rowIndex].Cells["TOP"].Value);
            string sid = Convert.ToString(dgvLogs.Rows[rowIndex].Cells["SIDE"].Value);

            var rec = _logsTabDayRaw.FirstOrDefault(r =>
                r.Time.ToString("HH:mm:ss") == t &&
                r.TopPath == top &&
                r.SidePath == sid
            );
            return rec;
        }
    }
}
