// TcpInspectionServer.cs — 듀얼(TOP→SIDE) 페어 수신 → Python 분석 → DB 기록(축소 스키마)
// -------------------------------------------------------------------------------------------------
// 핵심 변경점:
//  - BuildResultRowsFromParsed(): inspection_result 스키마(4열)에 맞춰 최소 필드만 생성
//  - DatabaseService.InsertInspectionResults(...) 호출 그대로, 내부가 4열 INSERT를 수행
//  - 나머지 흐름(파일저장/페어링/AI호출/최종요약/UI반영/클라회신)은 기존과 동일
// -------------------------------------------------------------------------------------------------

using System;                                       // 기본 타입/시간
using System.IO;                                    // 파일 IO
using System.Net;                                   // IPAddress
using System.Net.Sockets;                           // TCP
using System.Text;                                  // Encoding
using System.Threading;                             // CancellationTokenSource
using System.Threading.Tasks;                       // Task/async
using System.Collections.Generic;                   // List<T>
using Newtonsoft.Json.Linq;                         // JObject/JArray
using MFCServer1;                                   // DatabaseService, ServerMonitor 네임스페이스

namespace MFCServer1                                 // 네임스페이스 통일
{
    public class TcpInspectionServer
    {
        // ===== 구성 값 =====
        private readonly int _listenPort;           // 수신 포트
        private readonly string _pythonHost;        // 파이썬 서버 IP
        private readonly int _pythonPort;           // 파이썬 서버 포트

        // ===== 런타임 상태 =====
        private TcpListener _listener;              // 리스너
        private CancellationTokenSource _cts;       // 취소 토큰

        // ===== 듀얼 페어 상태 (TOP 먼저, 그 다음 SIDE) =====
        private static string _pendingTopPath = null;   // 직전 TOP 경로
        private static byte[] _pendingTopBytes = null;  // 직전 TOP 바이트

        // ===== 생성자 =====
        public TcpInspectionServer(int listenPort, string pythonHost, int pythonPort)
        {
            _listenPort = listenPort;               // 포트 저장
            _pythonHost = pythonHost;               // 호스트 저장
            _pythonPort = pythonPort;               // 포트 저장
        }

        // ===== 서버 시작 =====
        public async Task StartAsync()
        {
            _cts = new CancellationTokenSource();                       // 취소 토큰 생성
            _listener = new TcpListener(IPAddress.Any, _listenPort);    // ANY 수신
            _listener.Start();                                          // 시작

            Console.WriteLine("[TcpInspectionServer] Listening " + _listenPort); // 로그
            ServerMonitor.ServerStatus = "True / LISTEN " + _listenPort;         // 상태

            // 취소 전까지 반복
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // 클라이언트 수락 (대기)
                    TcpClient cli = await _listener.AcceptTcpClientAsync();      // 수락
                    string remote = cli.Client.RemoteEndPoint?.ToString() ?? ""; // 원격
                    Console.WriteLine("[ACCEPT] " + remote);                      // 로그
                    ServerMonitor.LastClientInfo = remote;                        // UI

                    // 세션 비동기 처리
                    _ = Task.Run(() => HandleClientAsync(cli));                   // 처리
                }
                catch (Exception ex)
                {
                    // 수락 예외
                    Console.WriteLine("[ACCEPT-ERR] " + ex.Message);              // 로그
                    ServerMonitor.ServerStatus = "False / EXC " + ex.Message;     // 상태
                    await Task.Delay(300);                                         // 잠깐 대기
                }
            }
        }

        // ===== 서버 정지 =====
        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }            // 취소
            try { _listener?.Stop(); } catch { }         // 정지
            ServerMonitor.ServerStatus = "False / STOP"; // 상태
        }

        // ===== 세션 처리 =====
        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)                                                    // using 보장
            {
                try
                {
                    using (NetworkStream ns = client.GetStream())             // 스트림
                    {
                        // (1) 길이(4바이트, Big-Endian) 수신
                        byte[] lenBuf = new byte[4];                          // 버퍼
                        int gotLen = await ReadExactAsync(ns, lenBuf, 0, 4);  // 정확 수신
                        if (gotLen < 4) return;                               // 끊김

                        // Big-Endian → int
                        int imgSize = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3]; // 길이
                        if (imgSize <= 0 || imgSize > 100_000_000) return;    // 이상치 방어

                        // (2) 이미지 본문 수신
                        byte[] imgBytes = new byte[imgSize];                  // 버퍼
                        int got = await ReadExactAsync(ns, imgBytes, 0, imgSize); // 수신
                        if (got < imgSize) return;                             // 끊김

                        // (3) 파일 저장
                        string dateDir = DateTime.Now.ToString("yyyyMMdd");   // 날짜 폴더
                        string saveDir = Path.Combine(@"C:\captures", dateDir); // 저장 루트
                        Directory.CreateDirectory(saveDir);                   // 폴더 보장

                        bool firstShot = (_pendingTopPath == null);           // TOP 여부
                        string role = firstShot ? "TOP" : "SIDE";             // 접두사
                        string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"); // 타임스탬프
                        string outPath = Path.Combine(saveDir, $"{role}_{ts}.jpg"); // 경로
                        File.WriteAllBytes(outPath, imgBytes);                // 저장

                        Console.WriteLine($"[RECV] {role} saved: {outPath}"); // 로그

                        // (4) 페어링: 첫 장이면 TOP 기억 후 종료
                        if (firstShot)
                        {
                            _pendingTopPath = outPath;                        // 경로 보관
                            _pendingTopBytes = imgBytes;                      // 바이트 보관
                            await WriteUtf8Async(ns, "{\"ok\":true,\"msg\":\"TOP saved\"}"); // 회신
                            return;                                           // 종료(SIDE 대기)
                        }

                        // 여기 도착 = 방금 받은 건 SIDE
                        string sidePath = outPath;                             // SIDE 경로

                        // (5) 파이썬 듀얼 분석 호출
                        string aiJson = "";                                    // 응답 JSON
                        try
                        {
                            aiJson = await CallPythonDualAsync(_pendingTopBytes, imgBytes); // 호출
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[AI] call FAIL: " + ex.Message); // 로그
                            aiJson = "";                                        // 빈 응답
                        }

                        // (6) AI 응답 해석 → 최종 결과/사유
                        string finalResult = "에러";                            // 기본값
                        string defectReason = "AI응답없음";                     // 기본 사유
                        JObject parsed = null;                                  // 파싱 객체

                        if (!string.IsNullOrWhiteSpace(aiJson))                 // 응답 존재
                        {
                            try
                            {
                                parsed = JObject.Parse(aiJson);                 // 파싱

                                string pyRes = parsed.Value<string>("result");  // "정상"/"비정상"
                                JToken detTop = parsed["det_top"];              // TOP 검출리스트
                                JToken detSide = parsed["det_side"];            // SIDE 검출리스트

                                bool nothing =
                                    ((detTop == null || !detTop.HasValues) &&
                                     (detSide == null || !detSide.HasValues)); // 양쪽 다 없음?

                                if (nothing)                                    // 검출 없음
                                {
                                    finalResult = "에러";                        // 에러
                                    defectReason = "캔인식실패";                // 사유
                                }
                                else
                                {
                                    if (pyRes == "정상") finalResult = "정상";   // 매핑
                                    else if (pyRes == "비정상") finalResult = "불량";
                                    else finalResult = "에러";                  // 기타

                                    defectReason = BuildCombinedReason(parsed); // 사유 문자열
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("[AI] parse FAIL: " + ex.Message); // 파싱 실패
                                finalResult = "에러";                                 // 에러
                                defectReason = "AI응답파싱실패";                     // 사유
                            }
                        }

                        Console.WriteLine($"[FINAL] {finalResult} / {defectReason}"); // 요약

                        // (7) DB: inspection / inspection_image INSERT
                        int newId = -1;                                               // PK
                        try
                        {
                            newId = DatabaseService.InsertInspection(                 // 메인 저장
                                DateTime.Now,                                        // 시간
                                finalResult,                                         // 결과
                                defectReason,                                        // 요약
                                _pendingTopPath,                                     // TOP
                                sidePath                                             // SIDE
                            );
                            Console.WriteLine("[DB] inspection id=" + newId);         // 로그
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[DB] inspection FAIL: " + ex.Message); // 실패
                        }

                        // (8) DB: inspection_result (축소 스키마 4열) INSERT
                        try
                        {
                            if (newId > 0 && parsed != null)                         // 성공+JSON
                            {
                                var rows = BuildResultRowsFromParsed(parsed, newId); // 행 생성
                                if (rows.Count > 0)                                   // 있으면
                                {
                                    DatabaseService.InsertInspectionResults(rows);    // INSERT
                                    Console.WriteLine("[DB] inspection_result rows=" + rows.Count); // 로그
                                }
                                else
                                {
                                    Console.WriteLine("[DB] inspection_result none"); // 없음
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[DB] result FAIL: " + ex.Message);     // 실패
                        }

                        // (9) UI: 실시간 목록에 한 줄 추가
                        ServerMonitor.RecordInspection(
                            DateTime.Now,                                            // 시간
                            finalResult,                                             // 결과
                            defectReason,                                            // 사유
                            _pendingTopPath,                                         // TOP
                            sidePath                                                 // SIDE
                        );

                        // (10) 클라이언트 회신(JSON 요약)
                        string reply =
                            "{\"result\":\"" + finalResult +
                            "\",\"reason\":\"" + defectReason.Replace("\"", "'") +
                            "\",\"timestamp\":\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                            "\"}";
                        await WriteUtf8Async(ns, reply);                              // 전송

                        // (11) 페어 초기화
                        _pendingTopPath = null;                                      // 리셋
                        _pendingTopBytes = null;                                     // 리셋
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[SESSION-ERR] " + ex.Message);                 // 세션 예외
                }
            }
        }

        // ===== 파이썬 듀얼 호출 (모드 0x02 / 길이는 Little-Endian) =====
        private async Task<string> CallPythonDualAsync(byte[] topBytes, byte[] sideBytes)
        {
            using (var cli = new TcpClient())                                        // 클라
            {
                await cli.ConnectAsync(_pythonHost, _pythonPort);                    // 연결
                using (NetworkStream ns = cli.GetStream())                           // 스트림
                {
                    await ns.WriteAsync(new byte[] { 0x02 }, 0, 1);                  // 모드

                    byte[] lenTop = BitConverter.GetBytes(topBytes.Length);          // TOP 길이
                    if (!BitConverter.IsLittleEndian) Array.Reverse(lenTop);         // LE 보정
                    await ns.WriteAsync(lenTop, 0, 4);                               // 전송
                    await ns.WriteAsync(topBytes, 0, topBytes.Length);               // 전송

                    byte[] lenSide = BitConverter.GetBytes(sideBytes.Length);        // SIDE 길이
                    if (!BitConverter.IsLittleEndian) Array.Reverse(lenSide);        // LE 보정
                    await ns.WriteAsync(lenSide, 0, 4);                              // 전송
                    await ns.WriteAsync(sideBytes, 0, sideBytes.Length);             // 전송

                    using (var ms = new MemoryStream())                              // 수신 버퍼
                    {
                        byte[] buf = new byte[4096];                                 // 버퍼
                        while (true)                                                 // 루프
                        {
                            int r = await ns.ReadAsync(buf, 0, buf.Length);          // 읽기
                            if (r <= 0) break;                                       // EOF
                            ms.Write(buf, 0, r);                                     // 누적
                        }
                        return Encoding.UTF8.GetString(ms.ToArray());                // 문자열
                    }
                }
            }
        }

        // ===== 사유 문자열 조립 (사람 읽기용) =====
        private static string BuildCombinedReason(JObject jobj)
        {
            string topRes = jobj.Value<string>("top_result") ?? "";                   // TOP 결과
            string sideRes = jobj.Value<string>("side_result") ?? "";                 // SIDE 결과

            JToken detTop = jobj["det_top"];                                          // TOP 리스트
            JToken detSide = jobj["det_side"];                                        // SIDE 리스트

            string firstTop = (detTop != null && detTop.HasValues)
                ? (detTop.First?[0]?.ToString() ?? "") : "";                          // 첫 라벨
            string firstSide = (detSide != null && detSide.HasValues)
                ? (detSide.First?[0]?.ToString() ?? "") : "";                         // 첫 라벨

            string topExpr =
                (topRes == "정상") ? "정상" :
                (topRes == "비정상") ? "비정상(" + (string.IsNullOrEmpty(firstTop) ? "불량" : firstTop) + ")" :
                topRes;                                                               // 에러 그대로

            string sideExpr =
                (sideRes == "정상") ? "정상" :
                (sideRes == "비정상") ? "비정상(" + (string.IsNullOrEmpty(firstSide) ? "불량" : firstSide) + ")" :
                sideRes;                                                              // 에러 그대로

            return "TOP: " + topExpr + " · SIDE: " + sideExpr;                        // 조합
        }

        // ===== JSON → inspection_result 행 변환 (4열 전용) =====
        private static List<DatabaseService.InspectionResultRow> BuildResultRowsFromParsed(JObject jobj, int newId)
        {
            var rows = new List<DatabaseService.InspectionResultRow>();              // 결과 리스트

            try
            {
                var detTop = jobj["det_top"] as JArray;                               // TOP 배열
                var detSide = jobj["det_side"] as JArray;                             // SIDE 배열

                // TOP: ["라벨", score, ...] 형태 가정 → 0번 인덱스 라벨만 사용
                if (detTop != null)
                {
                    foreach (var item in detTop)                                      // 각 검출
                    {
                        string label = item?[0]?.ToString() ?? "";                    // 라벨 추출
                        if (string.IsNullOrWhiteSpace(label)) continue;               // 빈 값 스킵

                        rows.Add(new DatabaseService.InspectionResultRow              // 행 추가
                        {
                            InspectionId = newId,                                     // FK
                            CameraType = "top",                                       // 카메라
                            DefectType = label                                       // 라벨
                        });
                    }
                }

                // SIDE: 동일 처리
                if (detSide != null)
                {
                    foreach (var item in detSide)                                     // 각 검출
                    {
                        string label = item?[0]?.ToString() ?? "";                    // 라벨
                        if (string.IsNullOrWhiteSpace(label)) continue;               // 빈 값 스킵

                        rows.Add(new DatabaseService.InspectionResultRow              // 행 추가
                        {
                            InspectionId = newId,                                     // FK
                            CameraType = "side",                                      // 카메라
                            DefectType = label                                       // 라벨
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DBDBG] BuildRows ex: " + ex.Message);             // 디버그
            }

            return rows;                                                              // 반환
        }

        // ===== 정확히 N바이트 읽기 유틸 =====
        private static async Task<int> ReadExactAsync(NetworkStream ns, byte[] buf, int off, int len)
        {
            int total = 0;                                                            // 누적
            while (total < len)                                                       // 부족하면 반복
            {
                int r = await ns.ReadAsync(buf, off + total, len - total);            // 읽기
                if (r <= 0) break;                                                    // EOF
                total += r;                                                           // 누적
            }
            return total;                                                             // 총 읽은 길이
        }

        // ===== UTF-8 텍스트 쓰기 유틸 =====
        private static async Task WriteUtf8Async(NetworkStream ns, string s)
        {
            byte[] data = Encoding.UTF8.GetBytes(s ?? "");                            // 인코딩
            await ns.WriteAsync(data, 0, data.Length);                                // 전송
        }
    }
}
