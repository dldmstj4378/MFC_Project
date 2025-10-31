// Program.cs
// 역할: 앱 시작점. WinForms UI(Form2) 띄우고, 동시에 TcpInspectionServer 백그라운드에서 가동.
// 주의: 여기서는 서버를 비동기로 StartAsync()만 해두고 Stop()은 따로 안 부른다.
//       애플리케이션이 종료되면 프로세스도 같이 내려가니까 우선 이 정도만.

using MFCServer1;
using System;                // 기본 시스템
using System.Threading.Tasks; // Task
using System.Windows.Forms; // WinForms Application.Run

static class Program
{
    // 애플리케이션 진입점
    [STAThread]
    static void Main()
    {
        // -----------------------------
        // 1. TCP 검사 서버 준비
        // -----------------------------
        // listenPort  : C++ 클라이언트(카메라 쪽)에서 이미지 전송해오는 포트
        // pythonHost  : Python AI 서버 IP (일단 로컬 가정)
        // pythonPort  : Python AI 서버 포트
        int listenPort = 9000;          // <- 네가 쓰는 포트로 맞춰
        string pythonHost = "10.10.21.110"; // <- 파이썬 AI 서버 IP
        int pythonPort = 8009;          // <- 파이썬 AI 서버 포트

        var inspectionServer = new TcpInspectionServer(listenPort, pythonHost, pythonPort);

        // 서버는 무한 루프(async)로 클라이언트 계속 받는 구조이기 때문에
        // 별도 Task로 돌려놓고 UI는 계속 Run 돌리면 된다.
        Task.Run(async () =>
        {
            await inspectionServer.StartAsync();
        });

        // -----------------------------
        // 2. WinForms UI 실행
        // -----------------------------
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Form2: 너희 모니터링 화면(Form2.cs)
        // 만약 Form2 생성자 시그니처가 다르면 거기에 맞추면 돼.
        Application.Run(new Form2());

        // -----------------------------
        // 3. 종료 시 처리 (필요하면 여기서 Stop() 같은 거 만들 수 있음)
        // -----------------------------
        // 지금 TcpInspectionServer엔 Stop()이 없어서 호출 안 함.
        // 프로세스가 내려가면 listener도 같이 죽는다.
    }
}
