// Form2.cs — 실시간 모니터/생산현황/생산로그 UI
// ------------------------------------------------------------
// 변경 요약:
//  - DatabaseService.LoadInspectionsByDate(date, filter) 2-인자 호출에 맞춤
//  - DailySummary/HourlySummary를 DataTable에서 안전하게 읽도록 수정
//  - DataTable에 .Total() / .Ok() / .Ng() / .NgRatePercent() 등을
//    호출하던 부분을 전부 제거하고 명시적인 컬럼 접근으로 교체
// ------------------------------------------------------------

using System;                                             // DateTime
using System.Collections.Generic;                         // List<T>
using System.Data;                                        // DataTable
using System.Drawing;                                     // Color, Image
using System.IO;                                          // File
using System.Linq;                                        // LINQ (일부)
using System.Net.Sockets;                                 // TcpClient (헬스체크)
using System.Threading.Tasks;                             // async/await
using System.Windows.Forms;                               // WinForms
using System.Windows.Forms.DataVisualization.Charting;    // Chart
using MFCServer1;                                         // ServerMonitor, DatabaseService

namespace MFCServer1
{
    public partial class Form2 : Form
    {
        // ===== 1) 실시간 모니터 타이머 =====
        private Timer _timer;                              // 1초 주기

        // ===== 2) 실시간 그리드용 뷰 모델 =====
        private class GridRow
        {
            public string Time { get; set; }              // 표시 시간
            public string Result { get; set; }            // 결과
            public string Reason { get; set; }            // 사유
            public string TopPath { get; set; }           // TOP 경로
            public string SidePath { get; set; }          // SIDE 경로
            public ServerMonitor.InspectionRecord Ref { get; set; } // 원본
        }

        // ===== 생성자 =====
        public Form2()
        {
            InitializeComponent();                        // 디자이너 초기화

            InitDataGridLogs();                           // 실시간 그리드
            InitDgvLogs();                                // 생산로그 그리드
            InitCharts();                                 // 차트 설정
            InitTimer();                                  // 타이머 시작

            _ = KickPythonHealthOnce();                   // 파이썬 헬스 1회
            UpdateStatusLabels();                         // 상태 라벨 1회
            RefreshLogsTab();                             // 로그 탭 1회
            RefreshStatsTab();                            // 현황 탭 1회
        }

        // ===== 실시간 그리드 초기화 =====
        private void InitDataGridLogs()
        {
            dataGridLogs.AutoGenerateColumns = false;     // 수동 컬럼
            dataGridLogs.AllowUserToAddRows = false;
            dataGridLogs.AllowUserToDeleteRows = false;
            dataGridLogs.AllowUserToResizeRows = false;
            dataGridLogs.ReadOnly = true;
            dataGridLogs.MultiSelect = false;
            dataGridLogs.RowHeadersVisible = false;
            dataGridLogs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridLogs.BackgroundColor = Color.FromArgb(240, 240, 240);

            dataGridLogs.Columns.Clear();
            dataGridLogs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "시간", DataPropertyName = "Time", Width = 140 });
            dataGridLogs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "결과", DataPropertyName = "Result", Width = 60 });
            dataGridLogs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "사유", DataPropertyName = "Reason", Width = 220 });
            dataGridLogs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "TOP 경로", DataPropertyName = "TopPath", Width = 220 });
            dataGridLogs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "SIDE 경로", DataPropertyName = "SidePath", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            foreach (DataGridViewColumn c in dataGridLogs.Columns)
                c.SortMode = DataGridViewColumnSortMode.NotSortable;

            dataGridLogs.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (dataGridLogs.Columns[e.ColumnIndex].DataPropertyName != "Result") return;
                var row = dataGridLogs.Rows[e.RowIndex].DataBoundItem as GridRow;
                if (row == null) return;
                if (row.Result == "불량" || row.Result == "비정상") e.CellStyle.ForeColor = Color.Red;
                else if (row.Result == "정상") e.CellStyle.ForeColor = Color.LimeGreen;
                else if (row.Result == "에러") e.CellStyle.ForeColor = Color.Orange;
            };

            dataGridLogs.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var row = dataGridLogs.Rows[e.RowIndex].DataBoundItem as GridRow;
                if (row == null) return;
                ShowImages(row.TopPath, row.SidePath);
                UpdateLastResultLabels(row);
            };
        }

        // ===== 생산로그 그리드 초기화 =====
        private void InitDgvLogs()
        {
            dgvLogs.AutoGenerateColumns = false;
            dgvLogs.AllowUserToAddRows = false;
            dgvLogs.AllowUserToDeleteRows = false;
            dgvLogs.AllowUserToResizeRows = false;
            dgvLogs.ReadOnly = true;
            dgvLogs.MultiSelect = false;
            dgvLogs.RowHeadersVisible = false;
            dgvLogs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvLogs.BackgroundColor = Color.White;

            dgvLogs.Columns.Clear();
            dgvLogs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "시간", DataPropertyName = "inspection_time", Width = 140 });
            dgvLogs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "결과", DataPropertyName = "final_result", Width = 60 });
            dgvLogs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "사유", DataPropertyName = "defect_summary", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            foreach (DataGridViewColumn c in dgvLogs.Columns)
                c.SortMode = DataGridViewColumnSortMode.NotSortable;

            rdoFilterAll.CheckedChanged += (s, e) => { if (rdoFilterAll.Checked) RefreshLogsTab(); };
            rdoFilterOK.CheckedChanged += (s, e) => { if (rdoFilterOK.Checked) RefreshLogsTab(); };
            rdoFilterNG.CheckedChanged += (s, e) => { if (rdoFilterNG.Checked) RefreshLogsTab(); };

            btnRefreshLogs.Click += (s, e) => RefreshLogsTab();
        }

        // ===== 차트 초기화 =====
        private void InitCharts()
        {
            chartPie.Series.Clear();
            chartPie.ChartAreas.Clear();
            chartPie.Legends.Clear();
            var pieArea = new ChartArea("PieArea");
            chartPie.ChartAreas.Add(pieArea);
            var pieLegend = new Legend("PieLegend");
            chartPie.Legends.Add(pieLegend);
            var sPie = new Series("PieSeries")
            {
                ChartType = SeriesChartType.Pie,
                ChartArea = "PieArea",
                Legend = "PieLegend"
            };
            chartPie.Series.Add(sPie);

            chartBar.Series.Clear();
            chartBar.ChartAreas.Clear();
            chartBar.Legends.Clear();
            var barArea = new ChartArea("BarArea");
            chartBar.ChartAreas.Add(barArea);
            var barLegend = new Legend("BarLegend");
            chartBar.Legends.Add(barLegend);
            var sOk = new Series("정상") { ChartType = SeriesChartType.Column, ChartArea = "BarArea", Legend = "BarLegend" };
            var sNg = new Series("불량") { ChartType = SeriesChartType.Column, ChartArea = "BarArea", Legend = "BarLegend" };
            chartBar.Series.Add(sOk);
            chartBar.Series.Add(sNg);

            btnRefreshStats.Click += (s, e) => RefreshStatsTab();
        }

        // ===== 타이머 시작 =====
        private void InitTimer()
        {
            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        // ===== 파이썬 헬스체크 =====
        private async Task KickPythonHealthOnce()
        {
            string pythonHost = "10.10.21.110";         // 환경에 맞게
            int pythonPort = 8009;
            try
            {
                using (var cli = new TcpClient())
                {
                    await cli.ConnectAsync(pythonHost, pythonPort);
                    ServerMonitor.PythonAlive = true;
                    ServerMonitor.PythonLastErrorMessage = "";
                }
            }
            catch (Exception ex)
            {
                ServerMonitor.PythonAlive = false;
                ServerMonitor.PythonLastErrorMessage = ex.Message;
            }
        }

        // ===== 타이머 틱 =====
        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                var recents = ServerMonitor.GetRecent(); // 메모리 최근
                var rows = recents.Select(r => new GridRow
                {
                    Time = r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                    Result = r.Result,
                    Reason = r.Reason,
                    TopPath = r.TopPath,
                    SidePath = r.SidePath,
                    Ref = r
                }).ToList();

                BindDataGridLogsPreserveScroll(rows);     // 스크롤 보존 바인딩

                var latest = recents.FirstOrDefault();
                if (latest != null)
                {
                    ShowImages(latest.TopPath, latest.SidePath);
                    UpdateLastResultLabels(new GridRow
                    {
                        Time = latest.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                        Result = latest.Result,
                        Reason = latest.Reason
                    });
                }

                UpdateKpi(recents);                       // 상단 KPI
                UpdateStatusLabels();                     // 하단 상태
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Timer_Tick] " + ex.Message);
            }
        }

        // ===== 실시간 그리드 바인딩(스크롤/선택 보존) =====
        private void BindDataGridLogsPreserveScroll(List<GridRow> rows)
        {
            int top = -1;
            int sel = -1;
            int col = 0;

            try { top = dataGridLogs.FirstDisplayedScrollingRowIndex; } catch { top = -1; }
            if (dataGridLogs.CurrentCell != null)
            {
                sel = dataGridLogs.CurrentCell.RowIndex;
                col = dataGridLogs.CurrentCell.ColumnIndex;
            }

            dataGridLogs.SuspendLayout();

            var bs = dataGridLogs.DataSource as BindingSource;
            if (bs == null)
            {
                bs = new BindingSource();
                dataGridLogs.DataSource = bs;
            }
            bs.DataSource = rows;

            dataGridLogs.ResumeLayout();

            if (sel >= 0 && sel < dataGridLogs.Rows.Count)
            {
                try
                {
                    dataGridLogs.CurrentCell =
                        dataGridLogs.Rows[sel].Cells[Math.Max(0, col)];
                    dataGridLogs.Rows[sel].Selected = true;
                }
                catch { }
            }
            if (top >= 0 && top < dataGridLogs.Rows.Count)
            {
                try { dataGridLogs.FirstDisplayedScrollingRowIndex = top; } catch { }
            }
        }

        // ===== 이미지 로더 =====
        private void ShowImages(string topPath, string sidePath)
        {
            SafeLoadToPictureBox(picTop, topPath);
            SafeLoadToPictureBox(picSide, sidePath);
        }

        private void SafeLoadToPictureBox(PictureBox pb, string path)
        {
            if (pb == null || string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) return;

            try
            {
                if (pb.Image != null) { var old = pb.Image; pb.Image = null; old.Dispose(); }
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var ms = new System.IO.MemoryStream())
                {
                    fs.CopyTo(ms);
                    ms.Position = 0;
                    pb.Image = Image.FromStream(ms);
                }
                pb.SizeMode = PictureBoxSizeMode.Zoom;
                pb.BackColor = Color.Black;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[SafeLoadToPictureBox] " + ex.Message);
            }
        }

        // ===== 마지막 결과 라벨 =====
        private void UpdateLastResultLabels(GridRow row)
        {
            string txt = "LastResult: " + (row.Result ?? "-");
            if (!string.IsNullOrEmpty(row.Reason)) txt += " (" + row.Reason + ")";
            if (!string.IsNullOrEmpty(row.Time)) txt += " @" + row.Time;
            lblLastResult.Text = txt;

            lblLastResult.ForeColor =
                (row.Result == "불량" || row.Result == "비정상") ? Color.Red :
                (row.Result == "정상") ? Color.LimeGreen :
                (row.Result == "에러") ? Color.Orange : Color.White;
        }

        // ===== 상단 KPI (최근 메모리 기준) =====
        private void UpdateKpi(List<ServerMonitor.InspectionRecord> list)
        {
            int total = list.Count;
            int ok = list.Count(r => r.Result == "정상");
            int ng = list.Count(r => r.Result != "정상");

            double rate = total > 0 ? ng * 100.0 / total : 0.0;

            lblTotalValue.Text = total.ToString();
            lblOkValue.Text = ok.ToString();
            lblNgValue.Text = ng.ToString();
            lblRateValue.Text = rate.ToString("0.0") + " %";

            lblNgValue.ForeColor = ng > 0 ? Color.Red : Color.White;
            lblRateValue.ForeColor = ng > 0 ? Color.Red : Color.MidnightBlue;
        }

        // ===== 하단 상태 라벨 =====
        private void UpdateStatusLabels()
        {
            lblTcpStatus.Text = "TCP STATUS: " + ServerMonitor.ServerStatus;
            lblTcpStatus.ForeColor =
                (ServerMonitor.ServerStatus != null && ServerMonitor.ServerStatus.StartsWith("True"))
                ? Color.LimeGreen : Color.Red;

            lblPythonStatus.Text = ServerMonitor.PythonAlive ? "PYTHON OK" : "PYTHON DOWN";
            lblPythonStatus.ForeColor = ServerMonitor.PythonAlive ? Color.LimeGreen : Color.Red;

            lblPythonError.Text = "LastError: " + (ServerMonitor.PythonLastErrorMessage ?? "");
            lblLastClient.Text = "LastClient: " + (ServerMonitor.LastClientInfo ?? "-");
        }

        // ===== 생산현황 탭 갱신 =====
        private void RefreshStatsTab()
        {
            DateTime d = dtStatsDate.Value.Date;           // 조회 일자

            // 1) 일간 요약 가져오기
            DataTable sum = DatabaseService.LoadDailySummary(d);   // total_count/ok_count/ng_count
            int total = 0, ok = 0, ng = 0;

            if (sum.Rows.Count > 0)
            {
                var row = sum.Rows[0];
                total = row["total_count"] == DBNull.Value ? 0 : Convert.ToInt32(row["total_count"]);
                ok = row["ok_count"] == DBNull.Value ? 0 : Convert.ToInt32(row["ok_count"]);
                ng = row["ng_count"] == DBNull.Value ? 0 : Convert.ToInt32(row["ng_count"]);
            }
            double rate = total > 0 ? ng * 100.0 / total : 0.0;

            // 요약 라벨 업데이트
            lblStatsSummaryTotal.Text = $"총 검사: {total}";
            lblStatsSummaryOk.Text = $"정상: {ok}";
            lblStatsSummaryNg.Text = $"불량: {ng}";
            lblStatsSummaryRate.Text = $"불량률: {rate:0.0} %";

            // 2) 파이 차트 갱신
            var sPie = chartPie.Series["PieSeries"];
            sPie.Points.Clear();
            sPie.Points.AddXY("정상", ok);
            sPie.Points.AddXY("불량", ng);

            // 3) 시간대 요약(막대 차트)
            DataTable hourly = DatabaseService.LoadHourlySummary(d); // hour, ok_count, ng_count
            var sOk = chartBar.Series["정상"]; sOk.Points.Clear();
            var sNg = chartBar.Series["불량"]; sNg.Points.Clear();

            foreach (DataRow r in hourly.Rows)
            {
                int h = r["hour"] == DBNull.Value ? 0 : Convert.ToInt32(r["hour"]);
                int okh = r["ok_count"] == DBNull.Value ? 0 : Convert.ToInt32(r["ok_count"]);
                int ngh = r["ng_count"] == DBNull.Value ? 0 : Convert.ToInt32(r["ng_count"]);

                sOk.Points.AddXY($"{h}시", okh);
                sNg.Points.AddXY($"{h}시", ngh);
            }
        }

        // ===== 생산로그 탭 갱신 =====
        private void RefreshLogsTab()
        {
            DateTime d = dtLogsDate.Value.Date;           // 조회 일자
            string filter =
                rdoFilterOK.Checked ? "OK" :
                rdoFilterNG.Checked ? "NG" : "ALL";       // 라디오 선택

            DataTable dt = DatabaseService.LoadInspectionsByDate(d, filter);
            dgvLogs.DataSource = dt;                       // 바로 바인딩
        }
    }
}
