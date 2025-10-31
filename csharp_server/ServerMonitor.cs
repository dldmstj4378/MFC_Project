using System;
using System.Collections.Generic;

namespace MFCServer1
{
    public static class ServerMonitor
    {
        // 1) TCP 서버 상태 / 마지막 클라이언트
        //    - TcpInspectionServer 에서 계속 갱신할 거라 set 허용해야 함
        public static string ServerStatus { get; set; } = "INIT";
        public static string LastClientInfo { get; set; } = "";

        // 2) 파이썬 서버 상태
        //    Form2에서 KickPythonHealthOnce()로 체크해서 갱신해줌
        public static bool PythonAlive { get; set; } = false;
        public static string PythonLastErrorMessage { get; set; } = "";

        // 3) 최근 검사 기록(화면에 뿌릴 로그)
        //    - Thread-safe하게 쓰고 싶으면 lock 걸어줌
        private static readonly object _lockObj = new object();
        private static readonly List<InspectionRecord> _recent = new List<InspectionRecord>();

        // 한 번에 너무 많이 쌓이지 않게 최대 N개만 유지
        private const int MAX_KEEP = 2000;

        public class InspectionRecord
        {
            public DateTime Time { get; set; }      // 검사 시각
            public string Result { get; set; }      // "정상" / "불량" / "에러"
            public string Reason { get; set; }      // "뚜껑없음" 등
            public string TopPath { get; set; }     // TOP 이미지 경로
            public string SidePath { get; set; }    // SIDE 이미지 경로
        }

        /// <summary>
        /// TcpInspectionServer.AnalyzeAndRespondAsync()에서 호출.
        /// 검사 한 건 끝날 때마다 여기로 push.
        /// Form2 타이머에서 GetRecent()로 끌어다 씀.
        /// </summary>
        public static void RecordInspection(DateTime time, string result, string reason, string topPath, string sidePath)
        {
            var rec = new InspectionRecord
            {
                Time = time,
                Result = result ?? "",
                Reason = reason ?? "",
                TopPath = topPath ?? "",
                SidePath = sidePath ?? ""
            };

            lock (_lockObj)
            {
                _recent.Insert(0, rec); // 최신이 앞으로 오도록
                if (_recent.Count > MAX_KEEP)
                {
                    _recent.RemoveAt(_recent.Count - 1);
                }
            }
        }

        /// <summary>
        /// 실시간 모니터 탭/생산로그 탭에서 최근 검사 목록을 읽을 때 사용.
        /// 기본은 최근 100개.
        /// </summary>
        public static List<InspectionRecord> GetRecent(int maxCount = 100)
        {
            lock (_lockObj)
            {
                int take = Math.Min(maxCount, _recent.Count);
                return new List<InspectionRecord>(_recent.GetRange(0, take));
            }
        }
    }
}
