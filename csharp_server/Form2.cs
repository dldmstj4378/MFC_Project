using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace MFCServer1
{
    public partial class Form2 : Form
    {
        private Timer _timer;

        public Form2()
        {
            InitializeComponent();

            InitDataGridLogs();   // 실시간 모니터 탭 그리드(dataGridLogs)
            InitDgvLogs();        // 생산로그 탭 그리드(dgvLogs)
            InitCharts();         // 생산현황 탭 차트 설정
            InitTimer();          // 1초 주기 갱신 타이머
            _ = KickPythonHealthOnce(); // 파이썬 헬스체크 1회
        }

        // ---------------------------------
        // 실시간 모니터 탭 (dataGridLogs) 초기화
        // ---------------------------------
        private void InitDataGridLogs()
        {
            dataGridLogs.AutoGenerateColumns = false;
            dataGridLogs.AllowUserToAddRows = false;
            dataGridLogs.AllowUserToDeleteRows = false;
            dataGridLogs.AllowUserToResizeRows = false;
            dataGridLogs.ReadOnly = true;
            dataGridLogs.MultiSelect = false;
            dataGridLogs.RowHeadersVisible = false;
            dataGridLogs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridLogs.BackgroundColor = Color.FromArgb(240, 240, 240);

            dataGridLogs.Columns.Clear();

            var colTime = new DataGridViewTextBoxColumn
            {
                HeaderText = "시간",
                DataPropertyName = "Time",
                Width = 140
            };
            dataGridLogs.Columns.Add(colTime);

            var colResult = new DataGridViewTextBoxColumn
            {
                HeaderText = "결과",
                DataPropertyName = "Result",
                Width = 60
            };
            dataGridLogs.Columns.Add(colResult);

            var colReason = new DataGridViewTextBoxColumn
            {
                HeaderText = "사유",
                DataPropertyName = "Reason",
                Width = 180
            };
            dataGridLogs.Columns.Add(colReason);

            var colTop = new DataGridViewTextBoxColumn
            {
                HeaderText = "TOP 경로",
                DataPropertyName = "TopPath",
                Width = 200
            };
            dataGridLogs.Columns.Add(colTop);

            var colSide = new DataGridViewTextBoxColumn
            {
                HeaderText = "SIDE 경로",
                DataPropertyName = "SidePath",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            dataGridLogs.Columns.Add(colSide);

            dataGridLogs.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (dataGridLogs.Columns[e.ColumnIndex].DataPropertyName != "Result") return;

                var rowObj = dataGridLogs.Rows[e.RowIndex].DataBoundItem as GridRow;
                if (rowObj == null) return;

                if (rowObj.Result == "불량" || rowObj.Result == "비정상")
                {
                    e.CellStyle.ForeColor = Color.Red;
                }
                else if (rowObj.Result == "정상")
                {
                    e.CellStyle.ForeColor = Color.LimeGreen;
                }
                else if (rowObj.Result == "에러")
                {
                    e.CellStyle.ForeColor = Color.Orange;
                }
            };

            dataGridLogs.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var rowObj = dataGridLogs.Rows[e.RowIndex].DataBoundItem as GridRow;
                if (rowObj == null) return;

                ShowImages(rowObj.TopPath, rowObj.SidePath);
                UpdateLastResultLabels(rowObj);
            };
        }

        // ---------------------------------
        // 생산로그 탭 (dgvLogs) 초기화
        // ---------------------------------
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

            var colDT = new DataGridViewTextBoxColumn
            {
                HeaderText = "시간",
                DataPropertyName = "Time",
                Width = 140
            };
            dgvLogs.Columns.Add(colDT);

            var colRes = new DataGridViewTextBoxColumn
            {
                HeaderText = "결과",
                DataPropertyName = "Result",
                Width = 60
            };
            dgvLogs.Columns.Add(colRes);

            var colWhy = new DataGridViewTextBoxColumn
            {
                HeaderText = "사유",
                DataPropertyName = "Reason",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            dgvLogs.Columns.Add(colWhy);

            // 라디오 버튼 & 조회 버튼 이벤트
            rdoFilterAll.CheckedChanged += (s, e) => { if (rdoFilterAll.Checked) RefreshLogsTab(); };
            rdoFilterOK.CheckedChanged += (s, e) => { if (rdoFilterOK.Checked) RefreshLogsTab(); };
            rdoFilterNG.CheckedChanged += (s, e) => { if (rdoFilterNG.Checked) RefreshLogsTab(); };

            btnRefreshLogs.Click += (s, e) => RefreshLogsTab();

            // 폼 최초 로드시 한 번 조회
            RefreshLogsTab();
        }

        // ---------------------------------
        // 차트 / 생산현황 탭 초기화
        // ---------------------------------
        private void InitCharts()
        {
            // 파이 차트
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

            // 막대 차트
            chartBar.Series.Clear();
            chartBar.ChartAreas.Clear();
            chartBar.Legends.Clear();

            var barArea = new ChartArea("BarArea");
            chartBar.ChartAreas.Add(barArea);

            var barLegend = new Legend("BarLegend");
            chartBar.Legends.Add(barLegend);

            var sOk = new Series("정상")
            {
                ChartType = SeriesChartType.Column,
                ChartArea = "BarArea",
                Legend = "BarLegend",
                Color = Color.FromArgb(100, 0, 128, 0)
            };

            var sNg = new Series("불량")
            {
                ChartType = SeriesChartType.Column,
                ChartArea = "BarArea",
                Legend = "BarLegend",
                Color = Color.FromArgb(100, 255, 0, 0)
            };

            chartBar.Series.Add(sOk);
            chartBar.Series.Add(sNg);

            btnRefreshStats.Click += (s, e) => RefreshStatsTab();
        }

        // ---------------------------------
        // 타이머 (실시간 모니터 탭 갱신용)
        // ---------------------------------
        private void InitTimer()
        {
            _timer = new Timer();
            _timer.Interval = 1000; // 1초
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        // ---------------------------------
        // 파이썬 서버 헬스체크 (최초 1회)
        // ---------------------------------
        private async Task KickPythonHealthOnce()
        {
            string pythonHost = "10.10.21.110"; // 파이썬 AI 서버 IP
            int pythonPort = 8009;              // 파이썬 AI 서버 포트

            try
            {
                using (var cli = new TcpClient())
                {
                    await cli.ConnectAsync(pythonHost, pythonPort);

                    // 파이썬 서버 살아있음
                    ServerMonitor.PythonAlive = true;
                    ServerMonitor.PythonLastErrorMessage = "";
                }
            }
            catch (Exception ex)
            {
                // 파이썬 서버 연결 실패
                ServerMonitor.PythonAlive = false;
                ServerMonitor.PythonLastErrorMessage = ex.Message;
            }
        }


        // ---------------------------------
        // 실시간 모니터 탭 주기 갱신
        // ---------------------------------
        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                var recents = ServerMonitor.GetRecent();

                // dataGridLogs 바인딩용
                var gridRows = recents
                    .Select(r => new GridRow
                    {
                        Time = r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                        Result = r.Result,
                        Reason = r.Reason,
                        TopPath = r.TopPath,
                        SidePath = r.SidePath,
                        Ref = r
                    })
                    .ToList();

                dataGridLogs.DataSource = gridRows;
                dataGridLogs.Refresh();

                // 가장 최근 1건으로 이미지/라벨 갱신
                var latest = recents.FirstOrDefault();
                if (latest != null)
                {
                    ShowImages(latest.TopPath, latest.SidePath);
                    UpdateLastResultLabels(new GridRow
                    {
                        Time = latest.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                        Result = latest.Result,
                        Reason = latest.Reason,
                        TopPath = latest.TopPath,
                        SidePath = latest.SidePath,
                        Ref = latest
                    });
                }

                // 상단 KPI 갱신
                UpdateKpi(recents);

                // 하단 상태 라벨 갱신
                UpdateStatusLabels();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Timer_Tick] " + ex.Message);
            }
        }

        // ---------------------------------
        // TCP / Python 상태 라벨들
        // ---------------------------------
        private void UpdateStatusLabels()
        {
            lblTcpStatus.Text = "TCP STATUS: " + ServerMonitor.ServerStatus;
            lblTcpStatus.ForeColor =
                ServerMonitor.ServerStatus.StartsWith("True") ? Color.LimeGreen : Color.Red;

            lblPythonStatus.Text = ServerMonitor.PythonAlive ? "PYTHON OK" : "PYTHON DOWN";
            lblPythonStatus.ForeColor = ServerMonitor.PythonAlive ? Color.LimeGreen : Color.Red;

            lblPythonError.Text = "LastError: " + (ServerMonitor.PythonLastErrorMessage ?? "");
            lblLastClient.Text = "LastClient: " + (ServerMonitor.LastClientInfo ?? "");
        }

        // ---------------------------------
        // 실시간 모니터 탭: 마지막 결과 라벨
        // ---------------------------------
        private void UpdateLastResultLabels(GridRow row)
        {
            string txt = "LastResult: " + (row.Result ?? "-");
            if (!string.IsNullOrEmpty(row.Reason))
                txt += " (" + row.Reason + ")";
            if (!string.IsNullOrEmpty(row.Time))
                txt += " @" + row.Time;

            lblLastResult.Text = txt;
            lblLastResult.ForeColor =
                (row.Result == "불량" || row.Result == "비정상") ? Color.Red :
                (row.Result == "정상") ? Color.LimeGreen :
                (row.Result == "에러") ? Color.Orange :
                Color.White;
        }

        // ---------------------------------
        // 실시간 모니터 탭 KPI (총 검사 / 정상 / 불량 / 불량률)
        // → ServerMonitor 메모리 기준
        // ---------------------------------
        private void UpdateKpi(List<ServerMonitor.InspectionRecord> list)
        {
            int total = list.Count;
            int ok = 0;
            int ng = 0;

            foreach (var r in list)
            {
                if (r.Result == "정상") ok++;
                else if (r.Result == "불량" || r.Result == "비정상") ng++;
            }

            double rate = total > 0 ? (double)ng * 100.0 / (double)total : 0.0;

            lblTotalValue.Text = total.ToString();
            lblOkValue.Text = ok.ToString();
            lblNgValue.Text = ng.ToString();
            lblRateValue.Text = rate.ToString("0.0") + " %";

            lblNgValue.ForeColor = ng > 0 ? Color.Red : Color.White;
            lblRateValue.ForeColor = ng > 0 ? Color.Red : Color.MidnightBlue;
        }

        // ---------------------------------
        // 실시간 탭 이미지 프리뷰
        // ---------------------------------
        private void ShowImages(string topPath, string sidePath)
        {
            SafeLoadToPictureBox(picTop, topPath);
            SafeLoadToPictureBox(picSide, sidePath);
        }

        private void SafeLoadToPictureBox(PictureBox pb, string path)
        {
            if (pb == null) return;
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) return;

            try
            {
                if (pb.Image != null)
                {
                    var old = pb.Image;
                    pb.Image = null;
                    old.Dispose();
                }

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var ms = new MemoryStream())
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

        // ---------------------------------
        // 생산현황 탭 새로고침 (DB에서 집계 → 라벨/파이/막대)
        // ---------------------------------
        private void RefreshStatsTab()
        {
            // 기준 날짜: 생산로그 탭에서 쓰는 dtLogsDate 기준으로 맞춘다
            DateTime targetDate = dtLogsDate.Value.Date;

            // 1) 일일 요약 (상단 라벨 + 파이차트)
            var daily = DatabaseService.LoadDailySummary(targetDate);

            lblStatsSummaryTotal.Text = "총 검사: " + daily.Total;
            lblStatsSummaryOk.Text = "정상: " + daily.Ok;
            lblStatsSummaryNg.Text = "불량: " + daily.Ng;
            lblStatsSummaryRate.Text = "불량률: " + daily.NgRatePercent.ToString("0.0") + " %";

            var pie = chartPie.Series["PieSeries"];
            pie.Points.Clear();
            pie.Points.AddXY("정상", daily.Ok);
            pie.Points.AddXY("불량", daily.Ng);

            // 2) 시간대별 집계 (막대 차트)
            var hourly = DatabaseService.LoadHourlySummary(targetDate);

            var sOk = chartBar.Series["정상"];
            var sNg = chartBar.Series["불량"];
            sOk.Points.Clear();
            sNg.Points.Clear();

            foreach (var h in hourly)
            {
                string label = h.Hour.ToString("00") + "시";
                sOk.Points.AddXY(label, h.Ok);
                sNg.Points.AddXY(label, h.Ng);
            }
        }

        // ---------------------------------
        // 생산로그 탭 새로고침 (DB에서 목록 → dgvLogs)
        // ---------------------------------
        private void RefreshLogsTab()
        {
            // 날짜
            DateTime selDate = dtLogsDate.Value.Date;

            // 필터 ("ALL" / "OK" / "NG")
            string filter = "ALL";
            if (rdoFilterOK.Checked) filter = "OK";
            if (rdoFilterNG.Checked) filter = "NG";

            // DB 조회
            List<DatabaseService.InspectionRecord> rowsFromDb =
                DatabaseService.LoadInspectionsByDate(selDate, filter);

            // DataGridView 바인딩용으로 변환
            var rowsForGrid = rowsFromDb
                .Select(r => new
                {
                    Time = r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                    Result = r.Result,
                    Reason = r.Reason
                })
                .ToList();

            dgvLogs.DataSource = rowsForGrid;
            dgvLogs.Refresh();
        }

        // ---------------------------------
        // dataGridLogs용 행 뷰 모델
        // ---------------------------------
        private class GridRow
        {
            public string Time { get; set; }
            public string Result { get; set; }
            public string Reason { get; set; }
            public string TopPath { get; set; }
            public string SidePath { get; set; }

            public ServerMonitor.InspectionRecord Ref { get; set; }
        }
    }
}
