using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;

namespace MFCServer1
{
    public static class DatabaseService
    {
        private static readonly string _connStr =
            "Server=127.0.0.1;Port=3306;Database=inspection;Uid=root;Pwd=1234;";

        public static int InsertInspection(
            DateTime time,
            string result,
            string reason,
            string top,
            string side)
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                conn.Open();
                using (var tran = conn.BeginTransaction())
                {
                    try
                    {
                        int newId = -1;

                        using (var cmd = new MySqlCommand(@"
INSERT INTO inspection
    (inspection_time, final_result, defect_summary, created_at)
VALUES
    (@t, @r, @s, NOW());
SELECT LAST_INSERT_ID();
", conn, tran))
                        {
                            cmd.Parameters.AddWithValue("@t", time);
                            cmd.Parameters.AddWithValue("@r", result ?? "에러");
                            cmd.Parameters.AddWithValue("@s", reason ?? "");

                            object obj = cmd.ExecuteScalar();
                            newId = Convert.ToInt32(obj);
                        }

                        if (!string.IsNullOrEmpty(top))
                        {
                            using (var cmd = new MySqlCommand(@"
INSERT INTO inspection_image
    (inspection_id, camera_type, file_path, created_at)
VALUES
    (@i, 'top', @p, NOW());
", conn, tran))
                            {
                                cmd.Parameters.AddWithValue("@i", newId);
                                cmd.Parameters.AddWithValue("@p", top);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        if (!string.IsNullOrEmpty(side))
                        {
                            using (var cmd = new MySqlCommand(@"
INSERT INTO inspection_image
    (inspection_id, camera_type, file_path, created_at)
VALUES
    (@i, 'side', @p, NOW());
", conn, tran))
                            {
                                cmd.Parameters.AddWithValue("@i", newId);
                                cmd.Parameters.AddWithValue("@p", side);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        tran.Commit();
                        return newId;
                    }
                    catch
                    {
                        try { tran.Rollback(); } catch { }
                        throw;
                    }
                }
            }
        }

        public static List<ServerMonitor.InspectionRecord> GetRecentInspections(int limit)
        {
            List<ServerMonitor.InspectionRecord> list = new List<ServerMonitor.InspectionRecord>();

            using (var conn = new MySqlConnection(_connStr))
            {
                conn.Open();
                string sql = @"
SELECT
    i.inspection_time,
    i.final_result,
    i.defect_summary,
    MAX(CASE WHEN img.camera_type='top'  THEN img.file_path END) AS top_path,
    MAX(CASE WHEN img.camera_type='side' THEN img.file_path END) AS side_path
FROM inspection i
LEFT JOIN inspection_image img
    ON img.inspection_id = i.inspection_id
GROUP BY i.inspection_id
ORDER BY i.inspection_time DESC
LIMIT @l;
";

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@l", limit);

                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            ServerMonitor.InspectionRecord rec = new ServerMonitor.InspectionRecord();

                            rec.Time = rd.GetDateTime("inspection_time");
                            rec.Result = rd.GetString("final_result");

                            int ordSummary = rd.GetOrdinal("defect_summary");
                            if (!rd.IsDBNull(ordSummary))
                                rec.Reason = rd.GetString(ordSummary);
                            else
                                rec.Reason = "";

                            int ordTop = rd.GetOrdinal("top_path");
                            if (!rd.IsDBNull(ordTop))
                                rec.TopPath = rd.GetString(ordTop);
                            else
                                rec.TopPath = "";

                            int ordSide = rd.GetOrdinal("side_path");
                            if (!rd.IsDBNull(ordSide))
                                rec.SidePath = rd.GetString(ordSide);
                            else
                                rec.SidePath = "";

                            rec.ClientIp = "";

                            list.Add(rec);
                        }
                    }
                }
            }

            return list;
        }

        public static List<ServerMonitor.InspectionRecord> GetDailyInspections(DateTime day)
        {
            List<ServerMonitor.InspectionRecord> list = new List<ServerMonitor.InspectionRecord>();

            using (var conn = new MySqlConnection(_connStr))
            {
                conn.Open();
                string sql = @"
SELECT
    i.inspection_time,
    i.final_result,
    i.defect_summary,
    MAX(CASE WHEN img.camera_type='top'  THEN img.file_path END) AS top_path,
    MAX(CASE WHEN img.camera_type='side' THEN img.file_path END) AS side_path
FROM inspection i
LEFT JOIN inspection_image img
    ON img.inspection_id = i.inspection_id
WHERE DATE(i.inspection_time) = @d
GROUP BY i.inspection_id
ORDER BY i.inspection_time ASC;
";
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@d", day.Date);

                    using (var rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            ServerMonitor.InspectionRecord rec = new ServerMonitor.InspectionRecord();

                            rec.Time = rd.GetDateTime("inspection_time");
                            rec.Result = rd.GetString("final_result");

                            int ordSummary = rd.GetOrdinal("defect_summary");
                            if (!rd.IsDBNull(ordSummary))
                                rec.Reason = rd.GetString(ordSummary);
                            else
                                rec.Reason = "";

                            int ordTop = rd.GetOrdinal("top_path");
                            if (!rd.IsDBNull(ordTop))
                                rec.TopPath = rd.GetString(ordTop);
                            else
                                rec.TopPath = "";

                            int ordSide = rd.GetOrdinal("side_path");
                            if (!rd.IsDBNull(ordSide))
                                rec.SidePath = rd.GetString(ordSide);
                            else
                                rec.SidePath = "";

                            rec.ClientIp = "";

                            list.Add(rec);
                        }
                    }
                }
            }

            return list;
        }
    }
}
