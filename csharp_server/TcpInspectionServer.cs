// TcpInspectionServer.cs — TOP/SIDE 사유 구분 + inspection_result 저장 버전 (전체 교체)
// 한 줄 한 줄 주석 포함

using System;                           // DateTime, Console
using System.IO;                        // File, Directory, MemoryStream, Path
using System.Net;                       // IPAddress
using System.Net.Sockets;               // TcpListener, TcpClient, NetworkStream
using System.Text;                      // Encoding
using System.Threading;                 // CancellationTokenSource
using System.Threading.Tasks;           // Task, async/await
using System.Collections.Generic;       // List<T>
using Newtonsoft.Json.Linq;             // JObject, JToken
using MFCServer1;                       // ServerMonitor, DatabaseService 네임스페이스

namespace MFCServer1
{
    public class TcpInspectionServer
    {
        // ==========================
        // 서버/파이썬 설정
        // ==========================
        private readonly int _listenPort;      // C++ 클라가 붙는 포트(예: 9000)
        private readonly string _pythonHost;   // 파이썬 AI 서버 IP
        private readonly int _pythonPort;      // 파이썬 AI 서버 포트

        private TcpListener _listener;         // 리스너
        private CancellationTokenSource _cts;  // 취소 토큰

        // 2장 세트 보관용 (단일 라인 가정)
        private static string _pendingTopPath = null; // 직전 TOP 파일 경로
        private static DateTime _pendingTopTime;      // TOP 시간
        private static byte[] _pendingTopBytes = null;// TOP 원본 바이트

        public TcpInspectionServer(int listenPort, string pythonHost, int pythonPort)
        {
            _listenPort = listenPort;
            _pythonHost = pythonHost;
            _pythonPort = pythonPort;
        }

        // ==========================
        // 서버 시작
        // ==========================
        public async Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _listenPort);
            _listener.Start();

            Console.WriteLine($"[TcpInspectionServer] Listening on port: {_listenPort}");
            ServerMonitor.ServerStatus = "True / LISTEN " + _listenPort;

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    string remote = client.Client.RemoteEndPoint?.ToString() ?? "";
                    Console.WriteLine("[TcpInspectionServer] >>> Client connected: " + remote);
                    ServerMonitor.LastClientInfo = remote;

                    _ = Task.Run(() => HandleClientAsync(client));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[TcpInspectionServer] Accept FAIL: " + ex.Message);
                    ServerMonitor.ServerStatus = "False / EXC " + ex.Message;
                    await Task.Delay(400);
                }
            }
        }

        // ==========================
        // 서버 정지
        // ==========================
        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            ServerMonitor.ServerStatus = "False / STOP";
        }

        // ==========================
        // 클라이언트 세션 처리
        // ==========================
        private async Task HandleClientAsync(TcpClient client)
        {
            string remote = client.Client.RemoteEndPoint?.ToString() ?? "";

            try
            {
                using (client)
                using (NetworkStream ns = client.GetStream())
                {
                    // ---- 1) 길이(4바이트, Big-Endian) 수신 ----
                    byte[] lenBuf = new byte[4];
                    int gotLen = await ReadExactAsync(ns, lenBuf, 0, 4);
                    if (gotLen < 4) return;

                    int imgSize = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | (lenBuf[3]);
                    if (imgSize <= 0 || imgSize > 100_000_000) return;

                    // ---- 2) 이미지 바디 수신 ----
                    byte[] imgBytes = new byte[imgSize];
                    int gotImg = await ReadExactAsync(ns, imgBytes, 0, imgSize);
                    if (gotImg < imgSize) return;

                    // ---- 3) 파일 저장 ----
                    string dateDir = DateTime.Now.ToString("yyyyMMdd");
                    string saveDir = Path.Combine(@"C:\captures", dateDir);
                    Directory.CreateDirectory(saveDir);

                    string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    bool isFirstShot = (_pendingTopPath == null);
                    string role = isFirstShot ? "TOP" : "SIDE";
                    string filePath = Path.Combine(saveDir, $"{role}_{ts}.jpg");
                    File.WriteAllBytes(filePath, imgBytes);
                    Console.WriteLine($"[SAVE] {role} -> {filePath}");

                    // ---- 4) 분기: 첫 장이면 보관, 두 번째면 Dual 분석 ----
                    if (isFirstShot)
                    {
                        _pendingTopPath = filePath;
                        _pendingTopBytes = imgBytes;
                        _pendingTopTime = DateTime.Now;

                        string tempJson = "{\"result\":\"대기\",\"reason\":\"TOP수신완료\",\"timestamp\":\"" +
                                          _pendingTopTime.ToString("yyyy-MM-dd HH:mm:ss") + "\"}";
                        var tmp = Encoding.UTF8.GetBytes(tempJson);
                        await ns.WriteAsync(tmp, 0, tmp.Length);   // .NET 표준 3-인자
                        Console.WriteLine("[INFO] TOP captured, waiting for SIDE...");
                        return;
                    }

                    // 두 번째(SIDE)
                    string sidePath = filePath;
                    byte[] sideBytes = imgBytes;
                    DateTime now = DateTime.Now;

                    if (_pendingTopPath == null || _pendingTopBytes == null)
                    {
                        // 예외: TOP이 없는데 SIDE가 옴
                        string fallback = "{\"result\":\"에러\",\"reason\":\"TOP없음\",\"timestamp\":\"" +
                                          now.ToString("yyyy-MM-dd HH:mm:ss") + "\"}";
                        var fb = Encoding.UTF8.GetBytes(fallback);
                        await ns.WriteAsync(fb, 0, fb.Length);
                        _pendingTopPath = null;
                        _pendingTopBytes = null;
                        return;
                    }

                    // ---- 5) 파이썬 dual 호출 ----
                    string aiRawJson = "";
                    try
                    {
                        aiRawJson = await CallPythonDualAsync(_pythonHost, _pythonPort, _pendingTopBytes, sideBytes);
                        Console.WriteLine("[AI RAW] " + aiRawJson);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[AI] dual FAIL: " + ex.Message);
                        aiRawJson = "";
                    }

                    // ---- 6) AI 해석 ----
                    string finalResult = "에러";       // 기본값
                    string defectReason = "AI응답없음";
                    JObject parsed = null;             // ← 세부 INSERT에 재사용

                    if (!string.IsNullOrWhiteSpace(aiRawJson))
                    {
                        try
                        {
                            parsed = JObject.Parse(aiRawJson);

                            // 파이썬 최종 결과("정상"|"비정상")
                            string pyRes = parsed.Value<string>("result");
                            var detTop = parsed["det_top"];
                            var detSide = parsed["det_side"];

                            bool nothingDetected =
                                ((detTop == null || !detTop.HasValues) &&
                                 (detSide == null || !detSide.HasValues));

                            if (nothingDetected)
                            {
                                finalResult = "에러";
                                defectReason = "캔인식실패";
                            }
                            else
                            {
                                finalResult = (pyRes == "정상") ? "정상"
                                             : (pyRes == "비정상") ? "불량"
                                             : "에러";

                                // 사유를 TOP/SIDE로 구분해 조합
                                defectReason = BuildCombinedReason(parsed);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[AI] JSON parse FAIL: " + ex.Message);
                            finalResult = "에러";
                            defectReason = "AI응답파싱실패";
                        }
                    }

                    Console.WriteLine($"[FINAL] result:{finalResult}, reason:{defectReason}");

                    // ---- 7) DB 기록: inspection / inspection_image ----
                    int newId = -1;
                    try
                    {
                        newId = DatabaseService.InsertInspection(
                            now, finalResult, defectReason, _pendingTopPath, sidePath);
                        Console.WriteLine("[DB] inspection inserted id=" + newId);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[DB] inspection insert FAIL: " + ex.Message);
                    }

                    // ---- 8) DB 기록: inspection_result (det_top / det_side) ----
                    try
                    {
                        if (newId > 0 && parsed != null)
                        {
                            var rows = BuildResultRowsFromParsed(parsed, newId);
                            if (rows.Count > 0)
                            {
                                DatabaseService.InsertInspectionResults(rows);
                                Console.WriteLine("[DB] inspection_result rows=" + rows.Count);
                            }
                            else
                            {
                                Console.WriteLine("[DB] inspection_result none (no detections)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[DB] inspection_result insert FAIL: " + ex.Message);
                    }

                    // ---- 9) UI 업데이트 ----
                    ServerMonitor.RecordInspection(now, finalResult, defectReason, _pendingTopPath, sidePath);

                    // ---- 10) 클라 응답 ----
                    string json = "{\"result\":\"" + finalResult + "\",\"reason\":\"" + defectReason +
                                  "\",\"timestamp\":\"" + now.ToString("yyyy-MM-dd HH:mm:ss") + "\"}";
                    var send = Encoding.UTF8.GetBytes(json);
                    await ns.WriteAsync(send, 0, send.Length);

                    // ---- 11) pending 초기화 ----
                    _pendingTopPath = null;
                    _pendingTopBytes = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TcpInspectionServer] EX: " + ex.Message);
            }

            Console.WriteLine("[TcpInspectionServer] <<< Client disconnected: " + remote);
        }

        // ==========================
        // 헬퍼: 정확히 len 바이트 읽기
        // ==========================
        private async Task<int> ReadExactAsync(NetworkStream ns, byte[] buf, int offset, int len)
        {
            int total = 0;
            while (total < len)
            {
                int r = await ns.ReadAsync(buf, offset + total, len - total);
                if (r <= 0) break;
                total += r;
            }
            return total;
        }

        // ==========================
        // 파이썬 dual 호출(0x02 프로토콜)
        // ==========================
        private async Task<string> CallPythonDualAsync(string host, int port, byte[] topBytes, byte[] sideBytes)
        {
            using (var cli = new TcpClient())
            {
                await cli.ConnectAsync(host, port);
                using (var ns = cli.GetStream())
                {
                    // 모드 0x02
                    await ns.WriteAsync(new byte[] { 0x02 }, 0, 1);

                    // TOP 길이(LE) + 데이터
                    byte[] topLen = BitConverter.GetBytes(topBytes.Length);
                    if (!BitConverter.IsLittleEndian) Array.Reverse(topLen);
                    await ns.WriteAsync(topLen, 0, 4);
                    await ns.WriteAsync(topBytes, 0, topBytes.Length);

                    // SIDE 길이(LE) + 데이터
                    byte[] sideLen = BitConverter.GetBytes(sideBytes.Length);
                    if (!BitConverter.IsLittleEndian) Array.Reverse(sideLen);
                    await ns.WriteAsync(sideLen, 0, 4);
                    await ns.WriteAsync(sideBytes, 0, sideBytes.Length);

                    // JSON 수신 (연결 종료까지)
                    using (var ms = new MemoryStream())
                    {
                        byte[] buf = new byte[4096];
                        while (true)
                        {
                            int r = await ns.ReadAsync(buf, 0, buf.Length);
                            if (r <= 0) break;
                            ms.Write(buf, 0, r);
                        }
                        return Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
            }
        }

        // ==========================
        // 사유 문자열 생성: "TOP: ... · SIDE: ..."
        // ==========================
        private static string BuildCombinedReason(JObject jobj)
        {
            // TOP / SIDE 최종
            string topRes = jobj.Value<string>("top_result") ?? "";
            string sideRes = jobj.Value<string>("side_result") ?? "";

            // 감지 리스트
            var detTop = jobj["det_top"];
            var detSide = jobj["det_side"];

            // 첫 라벨 추출
            string firstTop = (detTop != null && detTop.HasValues) ? detTop.First?[0]?.ToString() ?? "" : "";
            string firstSide = (detSide != null && detSide.HasValues) ? detSide.First?[0]?.ToString() ?? "" : "";

            // 표현식
            string topExpr = (topRes == "정상") ? "정상"
                           : (topRes == "비정상") ? $"비정상({(string.IsNullOrEmpty(firstTop) ? "불량" : firstTop)})"
                           : topRes;

            string sideExpr = (sideRes == "정상") ? "정상"
                           : (sideRes == "비정상") ? $"비정상({(string.IsNullOrEmpty(firstSide) ? "불량" : firstSide)})"
                           : sideRes;

            return $"TOP: {topExpr} · SIDE: {sideExpr}";
        }

        // ==========================
        // inspection_result 행 생성
        //  - det_top / det_side -> List<InspectionResultRow>
        // ==========================
        private static List<DatabaseService.InspectionResultRow> BuildResultRowsFromParsed(JObject jobj, int newId)
        {
            var rows = new List<DatabaseService.InspectionResultRow>();

            // det_top: [[label, score], ...]
            var detTop = jobj["det_top"];
            if (detTop != null && detTop.HasValues)
            {
                foreach (var arr in detTop)
                {
                    string lbl = arr?[0]?.ToString() ?? "";
                    double conf = 0.0;
                    double.TryParse(arr?[1]?.ToString(), out conf);

                    rows.Add(new DatabaseService.InspectionResultRow
                    {
                        InspectionId = newId,
                        CameraType = "top",
                        DefectType = lbl,
                        DefectValue = "",
                        Confidence = conf,
                        AdditionalInfo = ""   // bbox 필요 시 JSON으로 넣기
                    });
                }
            }

            // det_side: [[label, score], ...]
            var detSide = jobj["det_side"];
            if (detSide != null && detSide.HasValues)
            {
                foreach (var arr in detSide)
                {
                    string lbl = arr?[0]?.ToString() ?? "";
                    double conf = 0.0;
                    double.TryParse(arr?[1]?.ToString(), out conf);

                    rows.Add(new DatabaseService.InspectionResultRow
                    {
                        InspectionId = newId,
                        CameraType = "side",
                        DefectType = lbl,
                        DefectValue = "",
                        Confidence = conf,
                        AdditionalInfo = ""
                    });
                }
            }

            return rows;
        }
    }
}
