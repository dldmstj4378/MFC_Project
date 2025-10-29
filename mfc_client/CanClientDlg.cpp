#include "pch.h"
#include "framework.h"
#include "CanClient.h"
#include "CanClientDlg.h"
#include "afxdialogex.h"

#include <fstream>
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")

// ===== JSON 라이브러리 추가 =====
#include <nlohmann/json.hpp>
using json = nlohmann::json;

#ifdef _DEBUG
#define new DEBUG_NEW
#endif

using namespace Pylon;
using namespace cv;

// ===================== 메시지 맵 =====================
BEGIN_MESSAGE_MAP(CCanClientDlg, CDialogEx)
    ON_BN_CLICKED(IDC_BTN_START, &CCanClientDlg::OnBnClickedBtnStart)
    ON_WM_DESTROY()
    ON_WM_TIMER()
    ON_WM_CTLCOLOR()
    ON_NOTIFY(NM_CUSTOMDRAW, IDC_LIST_HISTORY, &CCanClientDlg::OnCustomDrawHistory)
    ON_NOTIFY(NM_DBLCLK, IDC_LIST_HISTORY, &CCanClientDlg::OnDblClkHistory)

END_MESSAGE_MAP()

// ===================== 생성자 =====================
CCanClientDlg::CCanClientDlg(CWnd* pParent)
    : CDialogEx(IDD_CANCLIENT_DIALOG, pParent)
{
    m_hIcon = AfxGetApp()->LoadIcon(IDR_MAINFRAME);
}

// ===================== MFC 연결 =====================
void CCanClientDlg::DoDataExchange(CDataExchange* pDX)
{
    CDialogEx::DoDataExchange(pDX);
}

// ===================== 초기화 =====================
BOOL CCanClientDlg::OnInitDialog()
{
    CDialogEx::OnInitDialog();
    SetIcon(m_hIcon, TRUE);
    SetIcon(m_hIcon, FALSE);

    // ===== WSA 초기화 =====
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) == 0) {
        m_wsaInitialized = true;
        OutputDebugString(L"[INFO] WSA 초기화 완료\n");
    }

    // ===== 히스토리 리스트 초기화 =====
    InitHistoryList();

    // ===== 초기 UI 상태 =====
    ClearCurrentResult();

    try {
        PylonInitialize();
        CTlFactory& factory = CTlFactory::GetInstance();
        DeviceInfoList_t devices;

        if (factory.EnumerateDevices(devices) < 2) {
            AfxMessageBox(L"Basler 카메라 2대를 모두 연결해야 합니다.");
            return TRUE;
        }

        m_camTop.Attach(factory.CreateDevice(devices[0]));
        m_camFront.Attach(factory.CreateDevice(devices[1]));

        m_camTop.Open();
        m_camFront.Open();

        m_camTop.StartGrabbing(GrabStrategy_LatestImageOnly);
        m_camFront.StartGrabbing(GrabStrategy_LatestImageOnly);

        m_timerId = SetTimer(1, 33, nullptr);
    }
    catch (const GenericException& e) {
        CString msg(e.GetDescription());
        AfxMessageBox(msg);
    }

    return TRUE;
}

// ===================== 히스토리 리스트 초기화 =====================
void CCanClientDlg::InitHistoryList()
{
    m_historyList.SubclassDlgItem(IDC_LIST_HISTORY, this);

    // ===== 컬럼 추가 =====
    m_historyList.InsertColumn(0, _T("제품번호"), LVCFMT_CENTER, 150);
    m_historyList.InsertColumn(1, _T("분석결과"), LVCFMT_CENTER, 120);
    m_historyList.InsertColumn(2, _T("불량종류"), LVCFMT_CENTER, 180);
    m_historyList.InsertColumn(3, _T("시간"), LVCFMT_CENTER, 170);

    // ===== 확장 스타일 =====
    m_historyList.SetExtendedStyle(
        LVS_EX_FULLROWSELECT |  // 전체 행 선택
        LVS_EX_GRIDLINES        // 눈금선
    );

    // ===== 파일에서 히스토리 로드 =====
    LoadHistoryFromFile();
}

// ===================== 타이머 (미리보기) =====================
void CCanClientDlg::OnTimer(UINT_PTR nIDEvent)
{
    if (nIDEvent == 1)
    {
        try {
            CGrabResultPtr grabTop, grabFront;

            if (m_camTop.IsGrabbing() &&
                m_camTop.RetrieveResult(50, grabTop, TimeoutHandling_Return) &&
                grabTop->GrabSucceeded())
            {
                m_converter.OutputPixelFormat = PixelType_BGR8packed;
                m_converter.Convert(m_pylonImage, grabTop);
                Mat img((int)grabTop->GetHeight(), (int)grabTop->GetWidth(),
                    CV_8UC3, (void*)m_pylonImage.GetBuffer());
                DrawMatToCtrl(img, GetDlgItem(IDC_CAM_TOP));
            }

            if (m_camFront.IsGrabbing() &&
                m_camFront.RetrieveResult(50, grabFront, TimeoutHandling_Return) &&
                grabFront->GrabSucceeded())
            {
                m_converter.OutputPixelFormat = PixelType_BGR8packed;
                m_converter.Convert(m_pylonImage, grabFront);
                Mat img((int)grabFront->GetHeight(), (int)grabFront->GetWidth(),
                    CV_8UC3, (void*)m_pylonImage.GetBuffer());
                DrawMatToCtrl(img, GetDlgItem(IDC_CAM_FRONT));
            }
        }
        catch (...) {
            OutputDebugString(L"[Basler] RetrieveResult error\n");
        }
    }
    CDialogEx::OnTimer(nIDEvent);
}

// ===================== 이미지 출력 =====================
void CCanClientDlg::DrawMatToCtrl(const Mat& img, CWnd* pWnd)
{
    if (!pWnd || img.empty()) return;
    CClientDC dc(pWnd);
    CRect rc; pWnd->GetClientRect(&rc);

    BITMAPINFO bmi = {};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = img.cols;
    bmi.bmiHeader.biHeight = -img.rows;
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 24;
    bmi.bmiHeader.biCompression = BI_RGB;

    StretchDIBits(dc.GetSafeHdc(),
        0, 0, rc.Width(), rc.Height(),
        0, 0, img.cols, img.rows,
        img.data, &bmi, DIB_RGB_COLORS, SRCCOPY);
}

// ===================== 촬영 및 전송 =====================
void CCanClientDlg::OnBnClickedBtnStart()
{
    // ===== 카메라 타이머 일시 중지 (충돌 방지) =====
    if (m_timerId) {
        KillTimer(m_timerId);
    }
    GetDlgItem(IDC_BTN_START)->EnableWindow(FALSE);

    try {
        // ===== 저장 폴더 준비 =====
        CString folder = L"C:\\CanClient\\captures";
        CreateDirectory(folder, NULL);

        if (!m_camTop.IsOpen())  m_camTop.Open();
        if (!m_camFront.IsOpen()) m_camFront.Open();

        if (!m_camTop.IsGrabbing())
            m_camTop.StartGrabbing(GrabStrategy_LatestImageOnly);
        if (!m_camFront.IsGrabbing())
            m_camFront.StartGrabbing(GrabStrategy_LatestImageOnly);

        Sleep(100); // 카메라 안정화 대기

        // ===== 변수 선언 (스코프 문제 방지) =====
        CGrabResultPtr grabTop, grabFront;
        std::string topPath, frontPath;
        std::string topResponse, frontResponse;

        // ===== 1. TOP 이미지 캡처 및 전송 =====
        if (m_camTop.RetrieveResult(800, grabTop, TimeoutHandling_Return) &&
            grabTop->GrabSucceeded())
        {
            CImageFormatConverter converter;
            converter.OutputPixelFormat = PixelType_BGR8packed;
            CPylonImage imgTop;
            converter.Convert(imgTop, grabTop);

            Mat topMat((int)grabTop->GetHeight(), (int)grabTop->GetWidth(),
                CV_8UC3, (void*)imgTop.GetBuffer());

            topPath = "C:\\CanClient\\captures\\capture_" +
                std::to_string(time(NULL)) + "_top.jpg";

            if (imwrite(topPath, topMat)) {
                OutputDebugString(L"[INFO] TOP 이미지 저장 완료\n");
                SendImageToServer(topPath, topResponse);
                OutputDebugStringA(("[TOP 응답] " + topResponse + "\n").c_str());
            }
        }

        Sleep(200); // 서버 처리 대기

        // ===== 2. FRONT 이미지 캡처 및 전송 =====
        if (m_camFront.RetrieveResult(800, grabFront, TimeoutHandling_Return) &&
            grabFront->GrabSucceeded())
        {
            CImageFormatConverter converter;
            converter.OutputPixelFormat = PixelType_BGR8packed;
            CPylonImage imgFront;
            converter.Convert(imgFront, grabFront);

            Mat frontMat((int)grabFront->GetHeight(), (int)grabFront->GetWidth(),
                CV_8UC3, (void*)imgFront.GetBuffer());

            frontPath = "C:\\CanClient\\captures\\capture_" +
                std::to_string(time(NULL)) + "_front.jpg";

            if (imwrite(frontPath, frontMat)) {
                OutputDebugString(L"[INFO] FRONT 이미지 저장 완료\n");
                SendImageToServer(frontPath, frontResponse);
                OutputDebugStringA(("[FRONT 응답] " + frontResponse + "\n").c_str());
            }
        }

        // ===== 3. 검사 결과 구성 =====
        InspectionResult result;
        result.productId = GenerateProductId();
        result.timestamp = GetCurrentTimestamp();
        result.imgTopPath = CString(topPath.c_str());
        result.imgFrontPath = CString(frontPath.c_str());

        // ===== 4. JSON 파싱 =====
        if (ParseJsonResponse(frontResponse, result)) {
            UpdateCurrentResult(result);
            AddToHistory(result);
            OutputDebugString(L"[SUCCESS] 검사 완료 및 결과 표시\n");
        }
        else {
            OutputDebugString(L"[WARNING] JSON 파싱 실패, 원본 사용\n");
            result.defectType = _T("에러");
            result.defectDetail = CString(frontResponse.c_str());
            UpdateCurrentResult(result);
            AddToHistory(result);
        }
    }
    catch (const GenericException& e) {
        CString msg(e.GetDescription());
        AfxMessageBox(msg);
        OutputDebugString(L"[ERROR] 카메라 예외 발생\n");
    }

    // ===== 타이머 재시작 =====
    m_timerId = SetTimer(1, 33, nullptr);
    GetDlgItem(IDC_BTN_START)->EnableWindow(TRUE);
}


// ===================== TCP 전송 및 응답 수신 =====================
bool CCanClientDlg::SendImageToServer(const std::string& imgPath, std::string& response)
{
    OutputDebugString(L"[DEBUG] SendImageToServer 시작\n");

    // ===== 파일 읽기 =====
    std::ifstream file(imgPath, std::ios::binary | std::ios::ate);
    if (!file) {
        OutputDebugString(L"[ERROR] 이미지 파일 열기 실패\n");
        return false;
    }

    std::streamsize size = file.tellg();
    file.seekg(0, std::ios::beg);
    std::vector<char> buffer(size);

    if (!file.read(buffer.data(), size)) {
        OutputDebugString(L"[ERROR] 파일 읽기 실패\n");
        return false;
    }

    // ===== 소켓 생성 =====
    SOCKET sock = socket(AF_INET, SOCK_STREAM, 0);
    if (sock == INVALID_SOCKET) {
        OutputDebugString(L"[ERROR] 소켓 생성 실패\n");
        return false;
    }

    // ===== 서버 연결 =====
    sockaddr_in serverAddr = {};
    serverAddr.sin_family = AF_INET;
    //serverAddr.sin_port = htons(9000);
	//inet_pton(AF_INET, "127.0.0.1", &serverAddr.sin_addr);    // 로컬 테스트용

    serverAddr.sin_port = htons(9000);
	inet_pton(AF_INET, "10.10.21.121", &serverAddr.sin_addr);   // 실제 서버 IP

    if (connect(sock, (sockaddr*)&serverAddr, sizeof(serverAddr)) == SOCKET_ERROR) {
        int err = WSAGetLastError();
        CString errMsg;
        errMsg.Format(L"[ERROR] 서버 연결 실패 (WSA: %d)\n", err);
        OutputDebugString(errMsg);
        closesocket(sock);
        return false;
    }
    OutputDebugString(L"[DEBUG] 서버 연결 성공\n");

    // ===== 크기 전송 (Big Endian) =====
    int fileSize = static_cast<int>(size);
    int netSize = htonl(fileSize);

    if (send(sock, (char*)&netSize, sizeof(netSize), 0) != sizeof(netSize)) {
        OutputDebugString(L"[ERROR] 길이 전송 실패\n");
        closesocket(sock);
        return false;
    }

    // ===== 데이터 전송 =====
    int totalSent = 0;
    while (totalSent < fileSize) {
        int sent = send(sock, buffer.data() + totalSent, fileSize - totalSent, 0);
        if (sent <= 0) {
            OutputDebugString(L"[ERROR] 데이터 전송 실패\n");
            closesocket(sock);
            return false;
        }
        totalSent += sent;
    }
    OutputDebugString(L"[INFO] 이미지 전송 완료\n");

    // ===== 응답 수신 =====
    char recvBuf[4096] = { 0 };
    int recvLen = recv(sock, recvBuf, sizeof(recvBuf) - 1, 0);

    if (recvLen > 0) {
        recvBuf[recvLen] = '\0';
        response = std::string(recvBuf);
        OutputDebugStringA(("[응답 수신] " + response + "\n").c_str());
    }
    else {
        OutputDebugString(L"[WARNING] 응답 없음\n");
        response = "";
    }

    closesocket(sock);
    return true;
}

// ===================== JSON 파싱 (간단 버전) =====================
bool CCanClientDlg::ParseJsonResponse(const std::string& jsonStr, InspectionResult& result)
{
    if (jsonStr.empty()) {
        OutputDebugString(L"[ERROR] JSON 문자열이 비어있음\n");
        return false;
    }

    try {
        // ===== JSON 파싱 =====
        auto j = json::parse(jsonStr);

        // ===== result 필드 (필수) =====
        if (j.contains("result")) {
            std::string resultStr = j["result"].get<std::string>();
            result.defectType = CString(resultStr.c_str());

            CStringA log;
            log.Format("[JSON] result = %s\n", resultStr.c_str());
            OutputDebugStringA(log);
        }
        else {
            OutputDebugString(L"[ERROR] JSON에 'result' 필드 없음\n");
            return false;
        }

        // ===== reason 필드 (선택) =====
        if (j.contains("reason")) {
            std::string reasonStr = j["reason"].get<std::string>();
            result.defectDetail = CString(reasonStr.c_str());

            CStringA log;
            log.Format("[JSON] reason = %s\n", reasonStr.c_str());
            OutputDebugStringA(log);
        }
        else {
            result.defectDetail = _T("");
        }

        // ===== timestamp 필드 (선택) =====
        if (j.contains("timestamp")) {
            std::string timestampStr = j["timestamp"].get<std::string>();
            // 서버에서 보낸 timestamp 사용 (선택사항)
            // result.timestamp = CString(timestampStr.c_str());
        }

        return true;
    }
    catch (json::parse_error& e) {
        CStringA errMsg;
        errMsg.Format("[ERROR] JSON 파싱 실패: %s\n", e.what());
        OutputDebugStringA(errMsg);
        return false;
    }
    catch (json::type_error& e) {
        CStringA errMsg;
        errMsg.Format("[ERROR] JSON 타입 에러: %s\n", e.what());
        OutputDebugStringA(errMsg);
        return false;
    }
    catch (std::exception& e) {
        CStringA errMsg;
        errMsg.Format("[ERROR] JSON 예외: %s\n", e.what());
        OutputDebugStringA(errMsg);
        return false;
    }
}

// ===================== UI 업데이트 =====================
void CCanClientDlg::UpdateCurrentResult(const InspectionResult& result)
{
    SetDlgItemText(IDC_STATIC_PRODUCT_ID, result.productId);
    SetDlgItemText(IDC_STATIC_DEFECT_TYPE, result.defectType);

    if (result.defectDetail.IsEmpty() || result.defectType == _T("정상"))
        SetDlgItemText(IDC_STATIC_DEFECT_DETAIL, _T("-"));
    else
        SetDlgItemText(IDC_STATIC_DEFECT_DETAIL, result.defectDetail);

    // 색상 매핑
    if (result.defectType == _T("정상"))      m_currResultColor = RGB(34, 177, 76); // 초록
    else if (result.defectType == _T("불량"))  m_currResultColor = RGB(237, 28, 36); // 빨강
    else                                       m_currResultColor = RGB(128, 128, 128); // 회색(에러/기타)

    m_currResultText = result.defectType;
    GetDlgItem(IDC_STATIC_DEFECT_TYPE)->Invalidate(); // 즉시 갱신
}

// ===================== 현재 결과 초기화 =====================
void CCanClientDlg::ClearCurrentResult()
{
    SetDlgItemText(IDC_STATIC_PRODUCT_ID, _T("-"));
    SetDlgItemText(IDC_STATIC_DEFECT_TYPE, _T("-"));
    SetDlgItemText(IDC_STATIC_DEFECT_DETAIL, _T("-"));
}

void CCanClientDlg::AddToHistory(const InspectionResult& result)
{
    m_history.push_back(result);

    int idx = m_historyList.GetItemCount();

    m_historyList.InsertItem(idx, result.productId);
    m_historyList.SetItemText(idx, 1, result.defectType);
    m_historyList.SetItemText(idx, 2,
        result.defectDetail.IsEmpty() ? _T("-") : result.defectDetail);
    m_historyList.SetItemText(idx, 3, result.timestamp);

    SaveHistoryToFile();
    m_historyList.EnsureVisible(idx, FALSE);
    UpdateStats();
}

// ===================== 유틸리티 함수 =====================
CString CCanClientDlg::GenerateProductId()
{
    CString productId;
    productId.Format(_T("CK%04d"), m_productCounter);
    m_productCounter++;
    return productId;
}

CString CCanClientDlg::GetCurrentTimestamp()
{
    CTime now = CTime::GetCurrentTime();
    return now.Format(_T("%Y-%m-%d %H:%M:%S"));
}

// ===================== 히스토리 저장/로드 =====================
void CCanClientDlg::SaveHistoryToFile()
{
    CString folder = _T("C:\\CanClient");
    CreateDirectory(folder, NULL);
    CString filePath = folder + _T("\\history.txt");

    CStdioFile file;
    if (!file.Open(filePath, CFile::modeCreate | CFile::modeWrite | CFile::typeText)) {
        return;
    }

    for (const auto& rec : m_history) {
        CString line;
        line.Format(_T("%s|%s|%s|%s\n"),
            rec.productId, rec.defectType,
            rec.defectDetail.IsEmpty() ? _T("-") : rec.defectDetail,
            rec.timestamp);
        file.WriteString(line);
    }
    file.Close();
}

// ================= 히스토리 파일에서 로드 ====================
void CCanClientDlg::LoadHistoryFromFile()
{
    CString filePath = _T("C:\\CanClient\\history.txt");
    CStdioFile file;
    if (!file.Open(filePath, CFile::modeRead | CFile::typeText)) {
        return;
    }

    CString line;
    int maxId = 1011;

    while (file.ReadString(line))
    {
        line.Trim(); // 공백/개행 제거
        if (line.IsEmpty()) continue; // 빈 줄은 스킵

        int pos = 0;
        InspectionResult rec;
        rec.productId = line.Tokenize(_T("|"), pos);
        rec.defectType = line.Tokenize(_T("|"), pos);
        rec.defectDetail = line.Tokenize(_T("|"), pos);
        rec.timestamp = line.Tokenize(_T("|"), pos);

        // 방어 처리
        if (rec.productId.IsEmpty()) continue;

        if (rec.defectDetail == _T("-"))
            rec.defectDetail.Empty();

        // 리스트와 벡터에 추가
        m_history.push_back(rec);

        int idx = m_historyList.GetItemCount();
        m_historyList.InsertItem(idx, rec.productId);
        m_historyList.SetItemText(idx, 1, rec.defectType);
        m_historyList.SetItemText(idx, 2,
            rec.defectDetail.IsEmpty() ? _T("-") : rec.defectDetail);
        m_historyList.SetItemText(idx, 3, rec.timestamp);

        // ===== 안전한 ID 숫자 추출 =====
        if (rec.productId.GetLength() > 2)
        {
            CString tail = rec.productId.Mid(2);  // "CK" 뒤 숫자만 추출
            int num = _ttoi(tail);
            if (num > maxId) maxId = num;
        }
    }

    m_productCounter = maxId + 1;
    file.Close();
    UpdateStats();
}


// ===================== 종료 =====================
void CCanClientDlg::OnDestroy()
{
    CDialogEx::OnDestroy();

    if (m_timerId) {
        KillTimer(m_timerId);
        m_timerId = 0;
    }

    try {
        if (m_camTop.IsGrabbing()) m_camTop.StopGrabbing();
        if (m_camTop.IsOpen()) m_camTop.Close();
        if (m_camFront.IsGrabbing()) m_camFront.StopGrabbing();
        if (m_camFront.IsOpen()) m_camFront.Close();
        PylonTerminate();
    }
    catch (...) {}

    if (m_wsaInitialized) {
        WSACleanup();
        m_wsaInitialized = false;
        OutputDebugString(L"[INFO] WSA 종료\n");
    }
}

HBRUSH CCanClientDlg::OnCtlColor(CDC* pDC, CWnd* pWnd, UINT nCtlColor)
{
    HBRUSH hbr = CDialogEx::OnCtlColor(pDC, pWnd, nCtlColor);

    // 결과 Static 컨트롤(예: IDC_STATIC_DEFECT_TYPE)의 텍스트 색 변경
    if (pWnd->GetDlgCtrlID() == IDC_STATIC_DEFECT_TYPE) {
        pDC->SetTextColor(m_currResultColor);
        pDC->SetBkMode(TRANSPARENT);
        return (HBRUSH)GetStockObject(HOLLOW_BRUSH);
    }
    return hbr;
}

void CCanClientDlg::OnCustomDrawHistory(NMHDR* pNMHDR, LRESULT* pResult)
{
    LPNMLVCUSTOMDRAW pCD = reinterpret_cast<LPNMLVCUSTOMDRAW>(pNMHDR);

    switch (pCD->nmcd.dwDrawStage)
    {
    case CDDS_PREPAINT:
        *pResult = CDRF_NOTIFYITEMDRAW;
        return;
    case CDDS_ITEMPREPAINT:
    {
        int idx = static_cast<int>(pCD->nmcd.dwItemSpec);
        // m_history는 인덱스와 1:1로 적재됨 (히스토리 적재/로드 코드 이미 있음 :contentReference[oaicite:1]{index=1} :contentReference[oaicite:2]{index=2})
        if (idx >= 0 && idx < (int)m_history.size())
        {
            const auto& rec = m_history[idx];
            if (rec.defectType == _T("불량"))
                pCD->clrText = RGB(237, 28, 36); // 빨강
        }
        *pResult = CDRF_DODEFAULT;
        return;
    }
    }
    *pResult = 0;
}

void CCanClientDlg::UpdateStats()
{
    // 오늘 날짜 yyyy-mm-dd로 비교
    CTime now = CTime::GetCurrentTime();
    CString today = now.Format(_T("%Y-%m-%d"));

    int totalToday = 0, ok = 0, ng = 0;
    for (const auto& r : m_history) {
        if (r.timestamp.Left(10) == today) {
            totalToday++;
            if (r.defectType == _T("정상")) ok++;
            else if (r.defectType == _T("불량")) ng++;
        }
    }

    CString s1; s1.Format(_T("오늘 검사: %d건"), totalToday);
    CString s2; s2.Format(_T("정상 %d / 불량 %d"), ok, ng);

    double rate = (totalToday > 0) ? (100.0 * ok / totalToday) : 0.0;
    CString s3; s3.Format(_T("정상비율: %.1f%%"), rate);

    SetDlgItemText(IDC_STATIC_TODAY_CNT, s1);
    SetDlgItemText(IDC_STATIC_OK_NG, s2);
    SetDlgItemText(IDC_STATIC_RATE, s3);
}

// ===================== 미리보기 다이얼로그 구현 =====================
BOOL CPreviewDlg::OnInitDialog()
{
    CDialogEx::OnInitDialog();
    LoadToCtrl(IDC_IMG_LEFT, m_left);
    LoadToCtrl(IDC_IMG_RIGHT, m_right);
    return TRUE;
}

void CPreviewDlg::LoadToCtrl(int id, const CString& path)
{
    if (path.IsEmpty()) return;

    CImage img;
    if (SUCCEEDED(img.Load(path)))
    {
        CStatic* pStatic = (CStatic*)GetDlgItem(id);
        if (!pStatic) return;

        CDC* pDC = pStatic->GetDC();
        CRect rc; pStatic->GetClientRect(&rc);

        img.Draw(*pDC, 0, 0, rc.Width(), rc.Height(),
            0, 0, img.GetWidth(), img.GetHeight());

        pStatic->ReleaseDC(pDC);
    }
}

// ===================== 리스트 더블클릭 핸들러 =====================
void CCanClientDlg::OnDblClkHistory(NMHDR* pNMHDR, LRESULT* pResult)
{
    POSITION pos = m_historyList.GetFirstSelectedItemPosition();
    if (!pos) return;

    int idx = m_historyList.GetNextSelectedItem(pos);
    if (idx < 0 || idx >= (int)m_history.size()) return;

    const auto& rec = m_history[idx];
    CPreviewDlg dlg(rec.imgTopPath, rec.imgFrontPath);
    dlg.DoModal();

    *pResult = 0;
}
