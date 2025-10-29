using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MFCServer1
{
    public class TcpInspectionServer
    {
        private readonly int _port;
        private TcpListener _listener;
        private bool _running;

        // 한 사이클에서 받은 이미지 임시 저장 경로
        private string _pendingTop = null;
        private string _pendingSide = null;

        // 파이썬 AI 서버 (ai_server.py)
        private readonly string _pyHost = "127.0.0.1";
        private readonly int _pyPort = 8008;

        public TcpInspectionServer(int port)
        {
            _port = port;
        }

        public void Start()
        {
            if (_running) return;

            _running = true;
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            // 폼 하단 상태표시용
            ServerMonitor.UpdateServerStatus(true, _port);
            CheckPythonServerHealth();
            // 비동기 루프 시작
            Task.Run(new Func<Task>(AcceptLoop));
        }

        public void Stop()
        {
            _running = false;
            try { _listener.Stop(); } catch { }

            ServerMonitor.UpdateServerStatus(false, _port);
        }

        private async Task AcceptLoop()
        {
            while (_running)
            {
                TcpClient cli = null;
                try
                {
                    cli = await _listener.AcceptTcpClientAsync();
                }
                catch
                {
                    if (!_running) break;
                }

                if (cli != null)
                {
                    // 각 클라 연결은 분리 처리
                    Task.Run(() => HandleClient(cli));
                }
            }
        }
        private void CheckPythonServerHealth()
        {
            try
            {
                using (TcpClient ping = new TcpClient())
                {
                    // 연결 시도 (1초 타임아웃)
                    var connectTask = ping.ConnectAsync(_pyHost, _pyPort);
                    if (!connectTask.Wait(1000))
                        throw new Exception("연결 타임아웃");

                    // 연결 성공 시 즉시 닫기
                    ServerMonitor.UpdatePythonStatus(true, "");
                    Console.WriteLine("[HealthCheck] Python server reachable ✅");
                }
            }
            catch (Exception ex)
            {
                ServerMonitor.UpdatePythonStatus(false, ex.Message);
                Console.WriteLine("[HealthCheck] Python server unreachable ❌: " + ex.Message);
            }
        }

        private async Task HandleClient(TcpClient cli)
        {
            using (cli)
            {
                NetworkStream ns = null;

                try
                {
                    // 접속한 장비 IP 기록해서 폼 하단 LastClient 갱신
                    IPEndPoint ep = (IPEndPoint)cli.Client.RemoteEndPoint;
                    string ip = ep.Address.ToString();
                    ServerMonitor.UpdateClientInfo(ip);

                    ns = cli.GetStream();

                    // ---------------------------
                    // 1) 이미지 길이(4바이트, network byte order)
                    // ---------------------------
                    byte[] lenBuf = new byte[4];
                    if (await ReadExact(ns, lenBuf, 0, 4) < 4)
                        return;

                    int netLen = BitConverter.ToInt32(lenBuf, 0);
                    int imgLen = IPAddress.NetworkToHostOrder(netLen);
                    if (imgLen <= 0 || imgLen > 100000000)
                        return;

                    // ---------------------------
                    // 2) 실제 이미지 바이트
                    // ---------------------------
                    byte[] imgBuf = new byte[imgLen];
                    if (await ReadExact(ns, imgBuf, 0, imgLen) < imgLen)
                        return;

                    // ---------------------------
                    // 3) 디스크에 저장
                    // ---------------------------
                    string saveDir = @"C:\captures";
                    Directory.CreateDirectory(saveDir);

                    string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".jpg";
                    string fullPath = Path.Combine(saveDir, fileName);
                    File.WriteAllBytes(fullPath, imgBuf);

                    // ---------------------------
                    // 4) top / side 세팅
                    //    첫 장은 top, 두 번째는 side
                    //    이미 둘 다 차있으면 새로운 사이클로 간주해서 top 갱신 후 side 비우기
                    // ---------------------------
                    if (_pendingTop == null)
                    {
                        _pendingTop = fullPath;
                    }
                    else if (_pendingSide == null)
                    {
                        _pendingSide = fullPath;
                    }
                    else
                    {
                        _pendingTop = fullPath;
                        _pendingSide = null;
                    }

                    // ---------------------------
                    // 5) 둘 다 있으면 => 분석 + DB기록 + JSON응답
                    //    둘 중 하나만 있으면 => 아직 최종 아님, ACK만
                    // ---------------------------
                    if (_pendingTop != null && _pendingSide != null)
                    {
                        string finalResult = "에러";  // 정상 / 불량 / 에러
                        string reason = "";      // 불량 사유 요약
                        DateTime now = DateTime.Now;

                        try
                        {
                            // 파이썬으로 두 장 보내고 JSON 받음
                            string aiJson = await AnalyzeDualAsync(_pendingTop, _pendingSide);

                            // 그 JSON에서 파싱해서 finalResult, reason만 간단히 뽑는 함수
                            ExtractResult(aiJson, out finalResult, out reason);

                            // 파이썬과 통신 성공 -> 폼 하단 "PYTHON OK"
                            ServerMonitor.UpdatePythonStatus(true, "");
                        }
                        catch (Exception exAi)
                        {
                            // 파이썬 죽었거나 응답 이상
                            finalResult = "에러";
                            reason = exAi.Message;
                            ServerMonitor.UpdatePythonStatus(false, exAi.Message);
                        }

                        // DB에 한 건 기록 (inspection / inspection_image)
                        try
                        {
                            DatabaseService.InsertInspection(
                                now,
                                finalResult,
                                reason,
                                _pendingTop,
                                _pendingSide
                            );
                        }
                        catch (Exception dbEx)
                        {
                            // DB 에러가 나도 UI 끊기면 안되니까 그냥 콘솔만
                            Console.WriteLine("[DB Insert Fail] " + dbEx.Message);
                        }

                        // 메모리에도 기록 -> 폼 그리드/그래프 업데이트용
                        ServerMonitor.RecordInspection(
                            now,
                            finalResult,
                            reason,
                            _pendingTop,
                            _pendingSide,
                            ip
                        );

                        // ---------------------------
                        // 6) 최종 응답: JSON 내려주기
                        // ---------------------------
                        string ts = now.ToString("yyyy-MM-dd HH:mm:ss");

                        // 여기서 클라가 그대로 파싱할 JSON 규격 확정.
                        // result  : "정상" / "불량" / "에러"
                        // reason  : 불량 사유 텍스트 (없으면 "")
                        // timestamp : yyyy-MM-dd HH:mm:ss
                        string jsonResp =
                            "{"
                            + "\"result\":\"" + EscapeJson(finalResult) + "\","
                            + "\"reason\":\"" + EscapeJson(reason) + "\","
                            + "\"timestamp\":\"" + EscapeJson(ts) + "\""
                            + "}";

                        byte[] sendBuf = Encoding.UTF8.GetBytes(jsonResp);
                        await ns.WriteAsync(sendBuf, 0, sendBuf.Length);

                        // ---------------------------
                        // 7) 사이클 종료: 다시 새 캡쳐 세트 받도록 비움
                        // ---------------------------
                        _pendingTop = null;
                        _pendingSide = null;
                    }
                    else
                    {
                        // 아직 한 장만 들어온 상태라 최종판정 불가.
                        // 클라 쪽은 이걸 무시하면 됨.
                        string ackText = "RECV OK";
                        byte[] ackBuf = Encoding.UTF8.GetBytes(ackText);
                        await ns.WriteAsync(ackBuf, 0, ackBuf.Length);
                    }
                }
                catch (Exception ex)
                {
                    // 이 커넥션 처리 중 났던 예외를 PYTHON 상태에도 반영
                    ServerMonitor.UpdatePythonStatus(false, ex.Message);

                    try
                    {
                        if (ns != null && ns.CanWrite)
                        {
                            // 예외가 나도 응답은 JSON 형태로 준다.
                            string tsErr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            string errJson =
                                "{"
                                + "\"result\":\"에러\","
                                + "\"reason\":\"" + EscapeJson(ex.Message) + "\","
                                + "\"timestamp\":\"" + EscapeJson(tsErr) + "\""
                                + "}";

                            byte[] errB = Encoding.UTF8.GetBytes(errJson);
                            await ns.WriteAsync(errB, 0, errB.Length);
                        }
                    }
                    catch
                    {
                        // 소켓도 이미 나갔으면 어쩔 수 없음
                    }
                }
                finally
                {
                    if (ns != null)
                    {
                        try { ns.Close(); } catch { }
                    }
                }
            }
        }

        // 파이썬 dual inference 호출:
        //   전송: 0x02 + [4바이트 top길이] + top바이트 + [4바이트 side길이] + side바이트
        //   수신: 파이썬이 던지는 JSON 문자열
        private async Task<string> AnalyzeDualAsync(string topPath, string sidePath)
        {
            TcpClient pyCli = new TcpClient();
            await pyCli.ConnectAsync(_pyHost, _pyPort);

            using (pyCli)
            {
                using (NetworkStream ns = pyCli.GetStream())
                {
                    byte[] topBytes = File.ReadAllBytes(topPath);
                    byte[] sideBytes = File.ReadAllBytes(sidePath);

                    // 모드(0x02)
                    byte[] mode = new byte[] { 0x02 };
                    await ns.WriteAsync(mode, 0, 1);

                    // top
                    byte[] lenTop = BitConverter.GetBytes(topBytes.Length);
                    await ns.WriteAsync(lenTop, 0, 4);
                    await ns.WriteAsync(topBytes, 0, topBytes.Length);

                    // side
                    byte[] lenSide = BitConverter.GetBytes(sideBytes.Length);
                    await ns.WriteAsync(lenSide, 0, 4);
                    await ns.WriteAsync(sideBytes, 0, sideBytes.Length);

                    // 응답 읽기 (파이썬은 연결 끊으면서 JSON만 쏴줌)
                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] buf = new byte[4096];
                        while (true)
                        {
                            int n;
                            try
                            {
                                n = await ns.ReadAsync(buf, 0, buf.Length);
                            }
                            catch
                            {
                                break;
                            }
                            if (n <= 0) break;
                            ms.Write(buf, 0, n);
                        }

                        string jsonFromPython = Encoding.UTF8.GetString(ms.ToArray());
                        return jsonFromPython;
                    }
                }
            }
        }

        // 파이썬에서 온 JSON에서 finalResult("정상"/"불량"/"에러"), reason("뚜껑없음" 등)만 대충 뽑는다.
        // 여기선 문자열 파편에서 단순 검색/Substring만 사용(C#7.3 호환)
        private void ExtractResult(string json, out string finalResult, out string reason)
        {
            finalResult = "에러";
            reason = "";

            if (string.IsNullOrEmpty(json))
                return;

            // "result":"정상"
            string key = "\"result\":\"";
            int idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int start = idx + key.Length;
                int end = json.IndexOf("\"", start, StringComparison.OrdinalIgnoreCase);
                if (end > start)
                {
                    finalResult = json.Substring(start, end - start);
                }
            }

            // 불량 사유 후보: "det_top":[["찌그러짐",...]] / "det_side":[["스크래치",...]]
            // 첫 label만 가져다 reason에 넣는다.
            string[] keys = { "\"det_top\":[[\"", "\"det_side\":[[\"" };
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                int f = json.IndexOf(k, StringComparison.OrdinalIgnoreCase);
                if (f >= 0)
                {
                    int start2 = f + k.Length;
                    int end2 = json.IndexOf("\"", start2, StringComparison.OrdinalIgnoreCase);
                    if (end2 > start2)
                    {
                        reason = json.Substring(start2, end2 - start2);
                        break;
                    }
                }
            }
        }

        // 정확히 size 바이트를 받아올 때까지 반복
        private static async Task<int> ReadExact(NetworkStream ns, byte[] buf, int offset, int size)
        {
            int total = 0;
            while (total < size)
            {
                int n = await ns.ReadAsync(buf, offset + total, size - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        // 최소 JSON 이스케이프 (따옴표 등)
        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
