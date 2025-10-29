#include "pch.h"
#include "framework.h"
#include "CanClient.h"
#include "CanClientDlg.h"
#include "afxdialogex.h"

#include <fstream>
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")

// ===== JSON 라이브러리 =====
#include <nlohmann/json.hpp>
using json = nlohmann::json;

// UTF-8 std::string -> UTF-16 CString
static CString Utf8ToCStr(const std::string& s)
{
    if (s.empty()) return CString();
    int wlen = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
    CString w;
    LPWSTR buf = w.GetBuffer(wlen);
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), buf, wlen);
    w.ReleaseBuffer(wlen);
    return w;
}


#ifdef _DEBUG
#define new DEBUG_NEW
#endif

using namespace Pylon;

// ===================== 메시지 맵 =====================
BEGIN_MESSAGE_MAP(CCanClientDlg, CDialogEx)
    ON_BN_CLICKED(IDC_BTN_START, &CCanClientDlg::OnBnClickedBtnStart)
    ON_WM_DESTROY()
    ON_WM_TIMER()
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

    // ===== 기존 히스토리 파일 초기화 =====
    {
        CString folder = _T("C:\\CanClient");
        CreateDirectory(folder, NULL);
        CString filePath = folder + _T("\\history.txt");
        CFile file;
        if (file.Open(filePath, CFile::modeCreate | CFile::modeWrite)) {
            file.Close(); // 빈 파일로 새로 생성
        }

        m_history.clear();               // 메모리 내 히스토리 초기화
        if (m_historyList.GetSafeHwnd()) // 리스트뷰도 비움
            m_historyList.DeleteAllItems();
    }

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

        // 미리보기용: 최신 프레임만 유지
        m_camTop.StartGrabbing(GrabStrategy_LatestImageOnly);
        m_camFront.StartGrabbing(GrabStrategy_LatestImageOnly);

        // 변환기 기본 설정: 미리보기/저장 공용 BGR8
        m_converter.OutputPixelFormat = PixelType_BGR8packed;
        m_converter.OutputBitAlignment = OutputBitAlignment_MsbAligned;

        // 미리보기 타이머
        m_timerId = SetTimer(1, 33, nullptr); // ~30fps
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
                // BGR8 변환 (미리보기)
                m_converter.Convert(m_pylonImage, grabTop);
                const uint8_t* buf = reinterpret_cast<const uint8_t*>(m_pylonImage.GetBuffer());
                int w = static_cast<int>(grabTop->GetWidth());
                int h = static_cast<int>(grabTop->GetHeight());
                DrawImageBufferToCtrl(buf, w, h, GetDlgItem(IDC_CAM_TOP));
            }

            if (m_camFront.IsGrabbing() &&
                m_camFront.RetrieveResult(50, grabFront, TimeoutHandling_Return) &&
                grabFront->GrabSucceeded())
            {
                // BGR8 변환 (미리보기)
                m_converter.Convert(m_pylonImage, grabFront);
                const uint8_t* buf = reinterpret_cast<const uint8_t*>(m_pylonImage.GetBuffer());
                int w = static_cast<int>(grabFront->GetWidth());
                int h = static_cast<int>(grabFront->GetHeight());
                DrawImageBufferToCtrl(buf, w, h, GetDlgItem(IDC_CAM_FRONT));
            }
        }
        catch (...) {
            OutputDebugString(L"[Basler] RetrieveResult error\n");
        }
    }
    CDialogEx::OnTimer(nIDEvent);
}

// ===================== BGR8 버퍼 출력 (종횡비 유지 + HALFTONE) =====================
void CCanClientDlg::DrawImageBufferToCtrl(const uint8_t* data, int width, int height, CWnd* pWnd)
{
    if (!pWnd || !data || width <= 0 || height <= 0) return;

    CClientDC dc(pWnd);
    CRect rc; pWnd->GetClientRect(&rc);

    // 목표 영역 대비 종횡비 유지: 레터박스 영역 계산
    const double srcAR = static_cast<double>(width) / static_cast<double>(height);
    const double dstAR = static_cast<double>(rc.Width()) / static_cast<double>(rc.Height());

    int drawW, drawH, drawX, drawY;
    if (srcAR > dstAR) {
        drawW = rc.Width();
        drawH = static_cast<int>(drawW / srcAR);
        drawX = 0;
        drawY = (rc.Height() - drawH) / 2;
    }
    else {
        drawH = rc.Height();
        drawW = static_cast<int>(drawH * srcAR);
        drawX = (rc.Width() - drawW) / 2;
        drawY = 0;
    }

    // 레터박스 배경(검정) 지우기
    CBrush brush(RGB(0, 0, 0));
    dc.FillRect(rc, &brush);

    // 고품질 스케일링
    int oldMode = SetStretchBltMode(dc.GetSafeHdc(), HALFTONE);
    SetBrushOrgEx(dc.GetSafeHdc(), 0, 0, nullptr); // HALFTONE 권장

    // DIB 정보(BGR8)
    BITMAPINFO bmi = {};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = width;
    bmi.bmiHeader.biHeight = -height; // 상단부터
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 24;
    bmi.bmiHeader.biCompression = BI_RGB;

    StretchDIBits(dc.GetSafeHdc(),
        drawX, drawY, drawW, drawH,
        0, 0, width, height,
        data, &bmi, DIB_RGB_COLORS, SRCCOPY);

    SetStretchBltMode(dc.GetSafeHdc(), oldMode);
}

// ===================== 촬영 및 전송 =====================
void CCanClientDlg::OnBnClickedBtnStart()
{
    // 타이머 일시 중지 (카메라 충돌 방지)
    if (m_timerId) {
        KillTimer(m_timerId);
    }

    GetDlgItem(IDC_BTN_START)->EnableWindow(FALSE);

    try {
        CString folder = L"C:\\CanClient\\captures";
        CreateDirectory(folder, NULL);

        if (!m_camTop.IsOpen())   m_camTop.Open();
        if (!m_camFront.IsOpen()) m_camFront.Open();

        if (!m_camTop.IsGrabbing())
            m_camTop.StartGrabbing(GrabStrategy_LatestImageOnly);
        if (!m_camFront.IsGrabbing())
            m_camFront.StartGrabbing(GrabStrategy_LatestImageOnly);

        Sleep(120); // 카메라 안정화

        CGrabResultPtr grabTop, grabFront;
        std::string topResponse, frontResponse;

        // ===== 1) TOP 캡처 & 저장(PNG, 무손실) & 전송 =====
        if (m_camTop.RetrieveResult(800, grabTop, TimeoutHandling_Return) &&
            grabTop->GrabSucceeded())
        {
            CPylonImage imgTop;
            m_converter.Convert(imgTop, grabTop); // BGR8

            std::string topPath = "C:\\CanClient\\captures\\capture_" +
                std::to_string(time(NULL)) + "_top.png";

            // 버전 호환을 위해 옵션 없이 저장 (무손실 PNG)
            CImagePersistence::Save(ImageFileFormat_Png, topPath.c_str(), imgTop);

            OutputDebugString(L"[INFO] TOP 이미지 저장 완료(PNG)\n");
            SendImageToServer(topPath, topResponse);
            OutputDebugStringA(("[TOP 응답] " + topResponse + "\n").c_str());
        }

        Sleep(200); // 서버 처리 대기

        // ===== 2) FRONT 캡처 & 저장(PNG, 무손실) & 전송 =====
        if (m_camFront.RetrieveResult(800, grabFront, TimeoutHandling_Return) &&
            grabFront->GrabSucceeded())
        {
            CPylonImage imgFront;
            m_converter.Convert(imgFront, grabFront); // BGR8

            std::string frontPath = "C:\\CanClient\\captures\\capture_" +
                std::to_string(time(NULL)) + "_front.png";

            CImagePersistence::Save(ImageFileFormat_Png, frontPath.c_str(), imgFront);

            OutputDebugString(L"[INFO] FRONT 이미지 저장 완료(PNG)\n");
            SendImageToServer(frontPath, frontResponse);
            OutputDebugStringA(("[FRONT 응답] " + frontResponse + "\n").c_str());

            // ===== 3) 검사 결과 처리 =====
            InspectionResult result;
            result.productId = GenerateProductId();
            result.timestamp = GetCurrentTimestamp();

            if (ParseJsonResponse(frontResponse, result)) {
                UpdateCurrentResult(result);
                AddToHistory(result);
                OutputDebugString(L"[SUCCESS] 검사 완료 및 결과 표시\n");
            }
            else {
                // JSON 파싱 실패 → 원본 문자열 그대로 표시
                OutputDebugStringA(("[WARNING] JSON 파싱 실패, 원본: " + frontResponse + "\n").c_str());
                result.defectType = _T("에러");
                result.defectDetail = Utf8ToCStr(frontResponse);
                UpdateCurrentResult(result);
                AddToHistory(result);
            }
        }
    }
    catch (const GenericException& e) {
        CString msg(e.GetDescription());
        AfxMessageBox(msg);
        OutputDebugString(L"[ERROR] 카메라 에러\n");
    }

    // 타이머 재시작
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
    std::vector<char> buffer(static_cast<size_t>(size));

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
    //inet_pton(AF_INET, "127.0.0.1", &serverAddr.sin_addr);

    serverAddr.sin_port = htons(9000);
    inet_pton(AF_INET, "10.10.21.121", &serverAddr.sin_addr);

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

    // ===== 데이터 전송 (sendall) =====
    int totalSent = 0;
    while (totalSent < fileSize) {
        int chunk = min(64 * 1024, fileSize - totalSent); // 64KB 청크
        int sent = send(sock, buffer.data() + totalSent, chunk, 0);
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
        response.clear();
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
        auto j = json::parse(jsonStr);

        // result 필드 (필수)
        if (j.contains("result")) {
            std::string resultStr = j["result"].get<std::string>();
            result.defectType = Utf8ToCStr(resultStr);

            CStringA log;
            log.Format("[JSON] result = %s\n", resultStr.c_str());
            OutputDebugStringA(log);
        }
        else {
            OutputDebugString(L"[ERROR] JSON에 'result' 필드 없음\n");
            return false;
        }

        // reason 필드 (선택)
        if (j.contains("reason")) {
            std::string reasonStr = j["reason"].get<std::string>();
            result.defectDetail = Utf8ToCStr(reasonStr);

            CStringA log;
            log.Format("[JSON] reason = %s\n", reasonStr.c_str());
            OutputDebugStringA(log);
        }
        else {
            result.defectDetail = _T("");
        }

        // timestamp 필드 (선택)
        if (j.contains("timestamp")) {
            std::string timestampStr = j["timestamp"].get<std::string>();
            // 서버 timestamp 사용하려면:
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

    if (result.defectDetail.IsEmpty() || result.defectType == _T("정상")) {
        SetDlgItemText(IDC_STATIC_DEFECT_DETAIL, _T("-"));
    }
    else {
        SetDlgItemText(IDC_STATIC_DEFECT_DETAIL, result.defectDetail);
    }
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

    // 통계 업데이트
    UpdateStatistics();
}

// ===================== 통계 갱신 =====================
void CCanClientDlg::UpdateStatistics()
{
    int total = 0, normal = 0, defect = 0;
    CString today = CTime::GetCurrentTime().Format(_T("%Y-%m-%d"));

    // 오늘 날짜 기준 통계 계산
    for (const auto& rec : m_history)
    {
        if (rec.timestamp.Left(10) == today)
        {
            total++;
            if (rec.defectType == _T("정상"))
                normal++;
            else
                defect++;
        }
    }

    // 비율 계산
    double ratio = (total > 0) ? (normal * 100.0 / total) : 0.0;

    // ===== 문자열 구성 =====
    CString strToday, strOkNg, strRate;
    strToday.Format(_T("오늘 검사량 : %d개"), total);
    strOkNg.Format(_T("정상 : %d개 / 불량 : %d개"), normal, defect);
    strRate.Format(_T("정상 비율 : %.0f%%"), ratio);

    // ===== UI 갱신 =====
    SetDlgItemText(IDC_STATIC_TODAY_CNT, strToday);
    SetDlgItemText(IDC_STATIC_OK_NG, strOkNg);
    SetDlgItemText(IDC_STATIC_RATE, strRate);
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
            rec.productId.GetString(),
            rec.defectType.GetString(),
            (rec.defectDetail.IsEmpty() ? _T("-") : rec.defectDetail.GetString()),
            rec.timestamp.GetString());
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
        line.Trim();
        if (line.IsEmpty())
            continue;

        // 안전한 split (Tokenize 대신)
        std::vector<CString> tokens;
        int cur = 0;
        while (true)
        {
            int next = line.Find(_T("|"), cur);
            if (next == -1)
            {
                tokens.push_back(line.Mid(cur));
                break;
            }
            tokens.push_back(line.Mid(cur, next - cur));
            cur = next + 1;
        }

        // 최소 4개 필드 있어야 함
        if (tokens.size() < 4)
            continue;

        InspectionResult rec;
        rec.productId = tokens[0].Trim();
        rec.defectType = tokens[1].Trim();
        rec.defectDetail = tokens[2].Trim();
        rec.timestamp = tokens[3].Trim();

        if (rec.defectDetail == _T("-"))
            rec.defectDetail.Empty();

        if (!rec.productId.IsEmpty())
        {
            m_history.push_back(rec);

            int idx = m_historyList.GetItemCount();
            m_historyList.InsertItem(idx, rec.productId);
            m_historyList.SetItemText(idx, 1, rec.defectType);
            m_historyList.SetItemText(idx, 2,
                rec.defectDetail.IsEmpty() ? _T("-") : rec.defectDetail);
            m_historyList.SetItemText(idx, 3, rec.timestamp);

            CString numStr = rec.productId.Mid(2);
            int num = _ttoi(numStr);
            if (num > maxId) maxId = num;
        }
    }

    m_productCounter = maxId + 1;
    file.Close();
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
        if (m_camTop.IsGrabbing())   m_camTop.StopGrabbing();
        if (m_camTop.IsOpen())       m_camTop.Close();
        if (m_camFront.IsGrabbing()) m_camFront.StopGrabbing();
        if (m_camFront.IsOpen())     m_camFront.Close();
        PylonTerminate();
    }
    catch (...) {}

    if (m_wsaInitialized) {
        WSACleanup();
        m_wsaInitialized = false;
        OutputDebugString(L"[INFO] WSA 종료\n");
    }
}
