// PythonTcpClient.cs
// 역할: C# 서버가 Python AI 서버(예: 127.0.0.1:8008)에
//       두 장 이미지를 보내고, 분석 결과(JSON 문자열)를 받는다.
//
// 프로토콜 가정:
//  - 연결하면 먼저 1바이트 모드코드 0x02 전송 (듀얼 이미지 분석 모드라고 가정)
//  - 그 다음 [top 이미지 길이(Int32 little-endian)] [top 이미지 바디]
//  - 그 다음 [side 이미지 길이(Int32 little-endian)] [side 이미지 바디]
//  - Python은 처리 후 JSON 텍스트를 그냥 socket으로 보내고 소켓 끊는다.
//  - 우리는 그걸 전부 읽어서 string으로 돌려준다.

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

public class PythonTcpClient
{
    private readonly string _host; // Python AI 서버 IP
    private readonly int _port;    // Python AI 서버 포트

    public PythonTcpClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    // AnalyzeDualAsync :
    // topPath / sidePath 이미지 파일을 둘 다 로드해서 Python 서버로 보낸다.
    // 그리고 Python 서버가 돌려준 분석 결과(JSON 문자열)를 반환한다.
    public async Task<string> AnalyzeDualAsync(string topPath, string sidePath)
    {
        // 이미지 로드 (바이너리 읽기)
        byte[] topBytes = File.ReadAllBytes(topPath);
        byte[] sideBytes = File.ReadAllBytes(sidePath);

        using (var client = new TcpClient())
        {
            // Python 서버 접속
            await client.ConnectAsync(_host, _port);

            using (var ns = client.GetStream())
            {
                // 모드 바이트 0x02 전송
                await ns.WriteAsync(new byte[] { 0x02 }, 0, 1);

                // 길이(int32, little-endian) + 본문 전송 (top)
                byte[] topLen = BitConverter.GetBytes(topBytes.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(topLen);

                await ns.WriteAsync(topLen, 0, 4);
                await ns.WriteAsync(topBytes, 0, topBytes.Length);

                // 길이(int32, little-endian) + 본문 전송 (side)
                byte[] sideLen = BitConverter.GetBytes(sideBytes.Length);
                if (!BitConverter.IsLittleEndian)
                    Array.Reverse(sideLen);

                await ns.WriteAsync(sideLen, 0, 4);
                await ns.WriteAsync(sideBytes, 0, sideBytes.Length);

                // 이제 Python 쪽에서 JSON 문자열을 보낼 때까지 기다렸다가 다 읽는다.
                // Python 서버는 전송 끝나면 소켓을 닫는다고 가정 -> read 0 나오면 break
                using (var ms = new MemoryStream())
                {
                    byte[] buf = new byte[4096];
                    while (true)
                    {
                        int r = await ns.ReadAsync(buf, 0, buf.Length);
                        if (r <= 0) break; // 서버가 연결 끊음
                        ms.Write(buf, 0, r);
                    }

                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    return json;
                }
            }
        }
    }
}
