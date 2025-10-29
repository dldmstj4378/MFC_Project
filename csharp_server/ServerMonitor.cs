using System;
using System.Collections.Generic;
using System.Linq;

namespace MFCServer1
{
    public static class ServerMonitor
    {
        // 최신 C# 기능 안 씀. 그냥 클래스/필드만.
        public class InspectionRecord
        {
            public DateTime Time { get; set; }
            public string Result { get; set; }
            public string Reason { get; set; }
            public string TopPath { get; set; }
            public string SidePath { get; set; }
            public string ClientIp { get; set; }
        }

        private static readonly List<InspectionRecord> _records = new List<InspectionRecord>();
        private static readonly object _lock = new object();

        public static string ServerStatus { get; private set; } = "INIT";
        public static string LastClientInfo { get; private set; } = "";
        public static bool PythonAlive { get; private set; } = false;
        public static string PythonLastErrorMessage { get; private set; } = "NO CONNECTION";

        public static void RecordInspection(DateTime time,
            string result,
            string reason,
            string top,
            string side,
            string ip)
        {
            lock (_lock)
            {
                InspectionRecord rec = new InspectionRecord();
                rec.Time = time;
                rec.Result = result ?? "";
                rec.Reason = reason ?? "";
                rec.TopPath = top ?? "";
                rec.SidePath = side ?? "";
                rec.ClientIp = ip ?? "";

                _records.Add(rec);

                if (_records.Count > 10000)
                {
                    _records.RemoveRange(0, _records.Count - 10000);
                }
            }
        }

        // ip 인자 없는 버전도 호출 가능하게
        public static void RecordInspection(DateTime time,
            string result,
            string reason,
            string top,
            string side)
        {
            RecordInspection(time, result, reason, top, side, "");
        }

        public static List<InspectionRecord> GetRecent(int limit)
        {
            lock (_lock)
            {
                return _records
                    .OrderByDescending(r => r.Time)
                    .Take(limit)
                    .ToList();
            }
        }

        public static List<InspectionRecord> GetRecent()
        {
            return GetRecent(200);
        }

        public static void UpdateServerStatus(bool active, int port)
        {
            if (active)
            {
                ServerStatus = "True " + port;
            }
            else
            {
                ServerStatus = "False";
            }
        }

        public static void UpdateClientInfo(string ip)
        {
            LastClientInfo = ip ?? "";
        }

        public static void UpdatePythonStatus(bool alive, string msg)
        {
            PythonAlive = alive;
            PythonLastErrorMessage = (msg ?? "");
        }
    }
}
