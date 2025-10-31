using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace MFCServer1
{
    partial class Form2
    {
        private System.ComponentModel.IContainer components = null;

        // ===== 공통 =====
        private TabControl tabMain;
        private TabPage tabMonitor;
        private TabPage tabStats;
        private TabPage tabLogs;

        // ===== 탭1: 실시간 모니터 =====
        private Panel panelKPIContainer;
        private Panel panelTotal;
        private Panel panelOk;
        private Panel panelNg;
        private Panel panelRate;
        private Label lblTotalTitle;
        private Label lblTotalValue;
        private Label lblOkTitle;
        private Label lblOkValue;
        private Label lblNgTitle;
        private Label lblNgValue;
        private Label lblRateTitle;
        private Label lblRateValue;

        private DataGridView dataGridLogs;

        private GroupBox groupImages;
        private Label lblTopImage;
        private Label lblSideImage;
        private PictureBox picTop;
        private PictureBox picSide;

        private Label lblTcpStatus;
        private Label lblPythonStatus;
        private Label lblPythonError;
        private Label lblLastClient;
        private Label lblLastResult;

        // ===== 탭2: 생산현황(차트) =====
        private Label lblStatsDateTitle;
        private DateTimePicker dtStatsDate;
        private Button btnRefreshStats;

        private Panel panelStatsSummary;
        private Label lblStatsSummaryTotal;
        private Label lblStatsSummaryOk;
        private Label lblStatsSummaryNg;
        private Label lblStatsSummaryRate;

        private Chart chartPie;
        private Chart chartBar;

        // ===== 탭3: 생산로그(히스토리) =====
        private Label lblLogsDateTitle;
        private DateTimePicker dtLogsDate;
        private Button btnRefreshLogs;

        private GroupBox grpLogFilter;
        private RadioButton rdoFilterAll;
        private RadioButton rdoFilterOK;
        private RadioButton rdoFilterNG;
        private DataGridView dgvLogs;



        /// <summary>
        /// Dispose
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();

            base.Dispose(disposing);
        }

        /// <summary>
        /// InitializeComponent
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();

            this.tabMain = new TabControl();
            this.tabMonitor = new TabPage();
            this.tabStats = new TabPage();
            this.tabLogs = new TabPage();

            // ---------------------------------------------------------
            // 탭1: 실시간 모니터
            // ---------------------------------------------------------

            this.panelKPIContainer = new Panel();
            this.panelTotal = new Panel();
            this.panelOk = new Panel();
            this.panelNg = new Panel();
            this.panelRate = new Panel();
            this.lblTotalTitle = new Label();
            this.lblTotalValue = new Label();
            this.lblOkTitle = new Label();
            this.lblOkValue = new Label();
            this.lblNgTitle = new Label();
            this.lblNgValue = new Label();
            this.lblRateTitle = new Label();
            this.lblRateValue = new Label();

            this.dataGridLogs = new DataGridView();

            this.groupImages = new GroupBox();
            this.lblTopImage = new Label();
            this.lblSideImage = new Label();
            this.picTop = new PictureBox();
            this.picSide = new PictureBox();

            this.lblTcpStatus = new Label();
            this.lblPythonStatus = new Label();
            this.lblPythonError = new Label();
            this.lblLastClient = new Label();
            this.lblLastResult = new Label();

            // KPI 컨테이너
            this.panelKPIContainer.Location = new Point(10, 10);
            this.panelKPIContainer.Size = new Size(820, 100);
            this.panelKPIContainer.BorderStyle = BorderStyle.None;

            // KPI: 총 검사
            this.panelTotal.Size = new Size(190, 90);
            this.panelTotal.Location = new Point(0, 0);
            this.panelTotal.BackColor = Color.FromArgb(240, 240, 240);
            this.panelTotal.BorderStyle = BorderStyle.FixedSingle;

            this.lblTotalTitle.Text = "총 검사";
            this.lblTotalTitle.Font = new Font("맑은 고딕", 10f, FontStyle.Bold);
            this.lblTotalTitle.Location = new Point(10, 10);
            this.lblTotalTitle.AutoSize = true;

            this.lblTotalValue.Text = "0";
            this.lblTotalValue.Font = new Font("맑은 고딕", 20f, FontStyle.Bold);
            this.lblTotalValue.Location = new Point(10, 35);
            this.lblTotalValue.AutoSize = true;

            this.panelTotal.Controls.Add(this.lblTotalTitle);
            this.panelTotal.Controls.Add(this.lblTotalValue);

            // KPI: 정상
            this.panelOk.Size = new Size(190, 90);
            this.panelOk.Location = new Point(200, 0);
            this.panelOk.BackColor = Color.FromArgb(235, 245, 235);
            this.panelOk.BorderStyle = BorderStyle.FixedSingle;

            this.lblOkTitle.Text = "정상";
            this.lblOkTitle.Font = new Font("맑은 고딕", 10f, FontStyle.Bold);
            this.lblOkTitle.Location = new Point(10, 10);
            this.lblOkTitle.AutoSize = true;

            this.lblOkValue.Text = "0";
            this.lblOkValue.Font = new Font("맑은 고딕", 20f, FontStyle.Bold);
            this.lblOkValue.ForeColor = Color.Green;
            this.lblOkValue.Location = new Point(10, 35);
            this.lblOkValue.AutoSize = true;

            this.panelOk.Controls.Add(this.lblOkTitle);
            this.panelOk.Controls.Add(this.lblOkValue);

            // KPI: 불량
            this.panelNg.Size = new Size(190, 90);
            this.panelNg.Location = new Point(400, 0);
            this.panelNg.BackColor = Color.FromArgb(245, 235, 235);
            this.panelNg.BorderStyle = BorderStyle.FixedSingle;

            this.lblNgTitle.Text = "불량";
            this.lblNgTitle.Font = new Font("맑은 고딕", 10f, FontStyle.Bold);
            this.lblNgTitle.Location = new Point(10, 10);
            this.lblNgTitle.AutoSize = true;

            this.lblNgValue.Text = "0";
            this.lblNgValue.Font = new Font("맑은 고딕", 20f, FontStyle.Bold);
            this.lblNgValue.ForeColor = Color.Red;
            this.lblNgValue.Location = new Point(10, 35);
            this.lblNgValue.AutoSize = true;

            this.panelNg.Controls.Add(this.lblNgTitle);
            this.panelNg.Controls.Add(this.lblNgValue);

            // KPI: 불량률
            this.panelRate.Size = new Size(190, 90);
            this.panelRate.Location = new Point(600, 0);
            this.panelRate.BackColor = Color.FromArgb(240, 240, 250);
            this.panelRate.BorderStyle = BorderStyle.FixedSingle;

            this.lblRateTitle.Text = "불량률";
            this.lblRateTitle.Font = new Font("맑은 고딕", 10f, FontStyle.Bold);
            this.lblRateTitle.Location = new Point(10, 10);
            this.lblRateTitle.AutoSize = true;

            this.lblRateValue.Text = "0.0 %";
            this.lblRateValue.Font = new Font("맑은 고딕", 20f, FontStyle.Bold);
            this.lblRateValue.ForeColor = Color.MidnightBlue;
            this.lblRateValue.Location = new Point(10, 35);
            this.lblRateValue.AutoSize = true;

            this.panelRate.Controls.Add(this.lblRateTitle);
            this.panelRate.Controls.Add(this.lblRateValue);

            // KPI 컨테이너에 넣기
            this.panelKPIContainer.Controls.Add(this.panelTotal);
            this.panelKPIContainer.Controls.Add(this.panelOk);
            this.panelKPIContainer.Controls.Add(this.panelNg);
            this.panelKPIContainer.Controls.Add(this.panelRate);

            // 실시간 로그 그리드
            this.dataGridLogs.Location = new Point(10, 120);
            this.dataGridLogs.Size = new Size(820, 400);
            this.dataGridLogs.ReadOnly = true;
            this.dataGridLogs.AllowUserToAddRows = false;
            this.dataGridLogs.AllowUserToDeleteRows = false;
            this.dataGridLogs.AllowUserToResizeRows = false;
            this.dataGridLogs.MultiSelect = false;
            this.dataGridLogs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            this.dataGridLogs.RowHeadersVisible = false;
            this.dataGridLogs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            // 이미지 그룹
            this.groupImages.Text = "최근 검사 이미지";
            this.groupImages.Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold);
            this.groupImages.Location = new Point(850, 10);
            this.groupImages.Size = new Size(400, 510);

            this.lblTopImage.Text = "TOP";
            this.lblTopImage.Location = new Point(15, 25);
            this.lblTopImage.AutoSize = true;

            this.picTop.BorderStyle = BorderStyle.FixedSingle;
            this.picTop.Location = new Point(15, 45);
            this.picTop.Size = new Size(360, 200);
            this.picTop.SizeMode = PictureBoxSizeMode.Zoom;
            this.picTop.BackColor = Color.Black;

            this.lblSideImage.Text = "SIDE";
            this.lblSideImage.Location = new Point(15, 255);
            this.lblSideImage.AutoSize = true;

            this.picSide.BorderStyle = BorderStyle.FixedSingle;
            this.picSide.Location = new Point(15, 275);
            this.picSide.Size = new Size(360, 200);
            this.picSide.SizeMode = PictureBoxSizeMode.Zoom;
            this.picSide.BackColor = Color.Black;

            this.groupImages.Controls.Add(this.lblTopImage);
            this.groupImages.Controls.Add(this.picTop);
            this.groupImages.Controls.Add(this.lblSideImage);
            this.groupImages.Controls.Add(this.picSide);

            // 상태 라벨들 (TCP/PYTHON/LastClient/LastResult/Error)
            this.lblTcpStatus.Text = "TCP LISTENING : Port 9000";
            this.lblTcpStatus.Location = new Point(10, 530);
            this.lblTcpStatus.ForeColor = Color.Green;
            this.lblTcpStatus.AutoSize = true;

            this.lblPythonStatus.Text = "PYTHON OK";
            this.lblPythonStatus.Location = new Point(10, 555);
            this.lblPythonStatus.ForeColor = Color.Green;
            this.lblPythonStatus.AutoSize = true;

            this.lblPythonError.Text = "LastError: (none)";
            this.lblPythonError.Location = new Point(10, 580);
            this.lblPythonError.AutoSize = true;

            this.lblLastClient.Text = "LastClient: -";
            this.lblLastClient.Location = new Point(10, 605);
            this.lblLastClient.AutoSize = true;

            this.lblLastResult.Text = "LastResult: -";
            this.lblLastResult.Location = new Point(10, 630);
            this.lblLastResult.AutoSize = true;

            // 탭1에 컨트롤들 추가
            this.tabMonitor.Controls.Add(this.panelKPIContainer);
            this.tabMonitor.Controls.Add(this.dataGridLogs);
            this.tabMonitor.Controls.Add(this.lblTcpStatus);
            this.tabMonitor.Controls.Add(this.lblPythonStatus);
            this.tabMonitor.Controls.Add(this.lblPythonError);
            this.tabMonitor.Controls.Add(this.lblLastClient);
            this.tabMonitor.Controls.Add(this.lblLastResult);
            this.tabMonitor.Controls.Add(this.groupImages);

            this.tabMonitor.Text = "실시간 모니터";
            this.tabMonitor.UseVisualStyleBackColor = true;

            // ---------------------------------------------------------
            // 탭2: 생산현황 (차트/요약)
            // ---------------------------------------------------------

            this.lblStatsDateTitle = new Label();
            this.dtStatsDate = new DateTimePicker();
            this.btnRefreshStats = new Button();

            this.panelStatsSummary = new Panel();
            this.lblStatsSummaryTotal = new Label();
            this.lblStatsSummaryOk = new Label();
            this.lblStatsSummaryNg = new Label();
            this.lblStatsSummaryRate = new Label();

            this.chartPie = new Chart();
            this.chartBar = new Chart();

            // 조회 일자
            this.lblStatsDateTitle.Text = "조회 일자";
            this.lblStatsDateTitle.Location = new Point(20, 20);
            this.lblStatsDateTitle.AutoSize = true;

            this.dtStatsDate.Format = DateTimePickerFormat.Short;
            this.dtStatsDate.Location = new Point(80, 15);
            this.dtStatsDate.Size = new Size(120, 23);

            this.btnRefreshStats.Text = "조회";
            this.btnRefreshStats.Location = new Point(210, 13);
            this.btnRefreshStats.Size = new Size(60, 26);

            // 요약 박스
            this.panelStatsSummary.Location = new Point(20, 55);
            this.panelStatsSummary.Size = new Size(250, 200);
            this.panelStatsSummary.BorderStyle = BorderStyle.FixedSingle;
            this.panelStatsSummary.BackColor = Color.WhiteSmoke;

            this.lblStatsSummaryTotal.Text = "총 검사: 0";
            this.lblStatsSummaryTotal.Font = new Font("맑은 고딕", 10f, FontStyle.Bold);
            this.lblStatsSummaryTotal.Location = new Point(10, 10);
            this.lblStatsSummaryTotal.AutoSize = true;

            this.lblStatsSummaryOk.Text = "정상: 0";
            this.lblStatsSummaryOk.ForeColor = Color.Green;
            this.lblStatsSummaryOk.Location = new Point(10, 50);
            this.lblStatsSummaryOk.AutoSize = true;

            this.lblStatsSummaryNg.Text = "불량: 0";
            this.lblStatsSummaryNg.ForeColor = Color.Red;
            this.lblStatsSummaryNg.Location = new Point(10, 80);
            this.lblStatsSummaryNg.AutoSize = true;

            this.lblStatsSummaryRate.Text = "불량률: 0.0 %";
            this.lblStatsSummaryRate.ForeColor = Color.MidnightBlue;
            this.lblStatsSummaryRate.Location = new Point(10, 110);
            this.lblStatsSummaryRate.AutoSize = true;

            this.panelStatsSummary.Controls.Add(this.lblStatsSummaryTotal);
            this.panelStatsSummary.Controls.Add(this.lblStatsSummaryOk);
            this.panelStatsSummary.Controls.Add(this.lblStatsSummaryNg);
            this.panelStatsSummary.Controls.Add(this.lblStatsSummaryRate);

            // 파이 차트
            ChartArea pieArea = new ChartArea("PieArea");
            this.chartPie.ChartAreas.Add(pieArea);
            Series pieSeries = new Series("PieSeries");
            pieSeries.ChartType = SeriesChartType.Pie;
            pieSeries.ChartArea = "PieArea";
            this.chartPie.Series.Add(pieSeries);
            this.chartPie.Legends.Add(new Legend("PieLegend"));

            this.chartPie.Location = new Point(290, 55);
            this.chartPie.Size = new Size(350, 250);

            // 막대 차트
            ChartArea barArea = new ChartArea("BarArea");
            this.chartBar.ChartAreas.Add(barArea);

            Series barSeriesOK = new Series("정상");
            barSeriesOK.ChartType = SeriesChartType.Column;
            barSeriesOK.ChartArea = "BarArea";
            barSeriesOK.Color = Color.FromArgb(100, 0, 128, 0);

            Series barSeriesNG = new Series("불량");
            barSeriesNG.ChartType = SeriesChartType.Column;
            barSeriesNG.ChartArea = "BarArea";
            barSeriesNG.Color = Color.FromArgb(100, 255, 0, 0);

            this.chartBar.Series.Add(barSeriesOK);
            this.chartBar.Series.Add(barSeriesNG);
            this.chartBar.Legends.Add(new Legend("BarLegend"));

            this.chartBar.Location = new Point(660, 55);
            this.chartBar.Size = new Size(580, 250);

            // 탭2 컨트롤 추가
            this.tabStats.Controls.Add(this.lblStatsDateTitle);
            this.tabStats.Controls.Add(this.dtStatsDate);
            this.tabStats.Controls.Add(this.btnRefreshStats);

            this.tabStats.Controls.Add(this.panelStatsSummary);
            this.tabStats.Controls.Add(this.chartPie);
            this.tabStats.Controls.Add(this.chartBar);

            this.tabStats.Text = "생산현황";
            this.tabStats.UseVisualStyleBackColor = true;

            // ---------------------------------------------------------
            // 탭3: 생산로그 (히스토리)
            // ---------------------------------------------------------

            this.lblLogsDateTitle = new Label();
            this.dtLogsDate = new DateTimePicker();
            this.btnRefreshLogs = new Button();

            this.grpLogFilter = new GroupBox();
            this.rdoFilterAll = new RadioButton();
            this.rdoFilterOK = new RadioButton();
            this.rdoFilterNG = new RadioButton();
            this.dgvLogs = new DataGridView();

            // 상단 날짜 + 조회 버튼
            this.lblLogsDateTitle.Text = "조회 일자";
            this.lblLogsDateTitle.Location = new Point(20, 20);
            this.lblLogsDateTitle.AutoSize = true;

            this.dtLogsDate.Format = DateTimePickerFormat.Short;
            this.dtLogsDate.Location = new Point(80, 15);
            this.dtLogsDate.Size = new Size(120, 23);

            this.btnRefreshLogs.Text = "조회";
            this.btnRefreshLogs.Location = new Point(210, 13);
            this.btnRefreshLogs.Size = new Size(60, 26);

            // 필터 그룹
            this.grpLogFilter.Text = "생산 로그";
            this.grpLogFilter.Font = new Font("맑은 고딕", 9f, FontStyle.Bold);
            this.grpLogFilter.Location = new Point(20, 55);
            this.grpLogFilter.Size = new Size(1220, 555);

            this.rdoFilterAll.Text = "전체";
            this.rdoFilterAll.Font = new Font("맑은 고딕", 9f, FontStyle.Regular);
            this.rdoFilterAll.Location = new Point(15, 25);
            this.rdoFilterAll.AutoSize = true;
            this.rdoFilterAll.Checked = true;

            this.rdoFilterOK.Text = "정상만";
            this.rdoFilterOK.Font = new Font("맑은 고딕", 9f, FontStyle.Regular);
            this.rdoFilterOK.Location = new Point(80, 25);
            this.rdoFilterOK.AutoSize = true;

            this.rdoFilterNG.Text = "불량만";
            this.rdoFilterNG.Font = new Font("맑은 고딕", 9f, FontStyle.Regular);
            this.rdoFilterNG.Location = new Point(155, 25);
            this.rdoFilterNG.AutoSize = true;

            this.dgvLogs.Location = new Point(15, 55);
            this.dgvLogs.Size = new Size(1185, 480);
            this.dgvLogs.ReadOnly = true;
            this.dgvLogs.AllowUserToAddRows = false;
            this.dgvLogs.AllowUserToDeleteRows = false;
            this.dgvLogs.AllowUserToResizeRows = false;
            this.dgvLogs.MultiSelect = false;
            this.dgvLogs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            this.dgvLogs.RowHeadersVisible = false;
            this.dgvLogs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            this.grpLogFilter.Controls.Add(this.rdoFilterAll);
            this.grpLogFilter.Controls.Add(this.rdoFilterOK);
            this.grpLogFilter.Controls.Add(this.rdoFilterNG);
            this.grpLogFilter.Controls.Add(this.dgvLogs);

            // 탭3에 넣기
            this.tabLogs.Controls.Add(this.lblLogsDateTitle);
            this.tabLogs.Controls.Add(this.dtLogsDate);
            this.tabLogs.Controls.Add(this.btnRefreshLogs);
            this.tabLogs.Controls.Add(this.grpLogFilter);

            this.tabLogs.Text = "생산로그";
            this.tabLogs.UseVisualStyleBackColor = true;

            // ---------------------------------------------------------
            // 최상위 TabControl
            // ---------------------------------------------------------
            this.tabMain.Dock = DockStyle.Fill;
            this.tabMain.Controls.Add(this.tabMonitor);
            this.tabMain.Controls.Add(this.tabStats);
            this.tabMain.Controls.Add(this.tabLogs);

            // ---------------------------------------------------------
            // Form 기본
            // ---------------------------------------------------------
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new Size(1280, 720);
            this.Text = "스마트팩토리 모니터 v1";

            this.Controls.Add(this.tabMain);
        }
    }
}
