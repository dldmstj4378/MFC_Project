// DatabaseService.cs
// ------------------------------------------------------------
// 역할: MySQL 연동 유틸리티 (inspection_result 4컬럼 스키마 전용)
//  - INSERT: inspection / inspection_image / inspection_result
//  - SELECT: 생산로그(일자), 시간대 요약, 일간 요약
//  - 오버로드: LoadInspectionsByDate(date, filter)
// ------------------------------------------------------------

using System;                                     // DateTime, Convert 등
using System.Collections.Generic;                 // IEnumerable<T>
using System.Data;                                // DataTable
using MySql.Data.MySqlClient;                     // MySQL 커넥터

namespace MFCServer1
{
    public static class DatabaseService
    {
        // ===== 연결 문자열 (환경 맞게 수정) =====
        private static readonly string _connStr =
            "Server=127.0.0.1;Port=3306;Database=inspection;Uid=root;Pwd=1234;";

        // ===== 세부 결과 DTO (4컬럼 전용) =====
        public class InspectionResultRow
        {
            public int InspectionId { get; set; }     // FK
            public string CameraType { get; set; }    // "top" | "side"
            public string DefectType { get; set; }    // 라벨
        }

        // ===== INSERT: inspection + inspection_image(2건) =====
        public static int InsertInspection(
            DateTime inspectionTime,                  // 검사 시각
            string finalResult,                       // "정상"/"불량"/"비정상"/"에러"
            string defectSummary,                     // 요약/사유
            string topImagePath,                      // TOP 경로
            string sideImagePath                      // SIDE 경로
        )
        {
            using (var conn = new MySqlConnection(_connStr)) // 연결
            {
                conn.Open();                                 // 오픈
                using (var tran = conn.BeginTransaction())   // 트랜잭션
                {
                    try
                    {
                        // 1) inspection INSERT → PK
                        string sqlInsp = @"
INSERT INTO inspection (inspection_time, final_result, defect_summary, created_at)
VALUES (@time, @result, @summary, NOW());
SELECT LAST_INSERT_ID();
";
                        int newId;
                        using (var cmd = new MySqlCommand(sqlInsp, conn, tran))
                        {
                            cmd.Parameters.AddWithValue("@time", inspectionTime);
                            cmd.Parameters.AddWithValue("@result", finalResult ?? "");
                            cmd.Parameters.AddWithValue("@summary", defectSummary ?? "");
                            newId = Convert.ToInt32(cmd.ExecuteScalar());
                        }

                        // 2) 이미지 2건
                        string sqlImg = @"
INSERT INTO inspection_image (inspection_id, camera_type, file_path, created_at)
VALUES
(@id, 'top',  @top,  NOW()),
(@id, 'side', @side, NOW());
";
                        using (var cmd = new MySqlCommand(sqlImg, conn, tran))
                        {
                            cmd.Parameters.AddWithValue("@id", newId);
                            cmd.Parameters.AddWithValue("@top", topImagePath ?? "");
                            cmd.Parameters.AddWithValue("@side", sideImagePath ?? "");
                            cmd.ExecuteNonQuery();
                        }

                        tran.Commit();                          // 커밋
                        return newId;                           // PK 반환
                    }
                    catch
                    {
                        tran.Rollback();                        // 롤백
                        throw;                                  // 재던짐
                    }
                }
            }
        }

        // ===== INSERT: inspection_result 다건(4컬럼) =====
        public static void InsertInspectionResults(IEnumerable<InspectionResultRow> rows)
        {
            if (rows == null) return;                           // null 보호

            using (var conn = new MySqlConnection(_connStr))    // 연결
            {
                conn.Open();                                    // 오픈
                using (var tran = conn.BeginTransaction())      // 트랜잭션
                {
                    try
                    {
                        string sql = @"
INSERT INTO inspection_result
(inspection_id, camera_type, defect_type, created_at)
VALUES
(@insp, @cam, @type, NOW());
";
                        foreach (var r in rows)                  // 각 행
                        {
                            if (r == null) continue;
                            using (var cmd = new MySqlCommand(sql, conn, tran))
                            {
                                cmd.Parameters.AddWithValue("@insp", r.InspectionId);
                                cmd.Parameters.AddWithValue("@cam", r.CameraType ?? "");
                                cmd.Parameters.AddWithValue("@type", r.DefectType ?? "");
                                cmd.ExecuteNonQuery();
                            }
                        }
                        tran.Commit();                           // 커밋
                    }
                    catch
                    {
                        tran.Rollback();                         // 롤백
                        throw;
                    }
                }
            }
        }

        // ===== SELECT: 생산로그(일자) - 기본(1인자) =====
        public static DataTable LoadInspectionsByDate(DateTime targetDate)
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                conn.Open();
                string sql = @"
SELECT inspection_id, inspection_time, final_result, defect_summary, created_at
FROM inspection
WHERE DATE(inspection_time) = @d
ORDER BY inspection_time DESC;
";
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@d", targetDate.Date);
                    using (var da = new MySqlDataAdapter(cmd))
                    {
                        var dt = new DataTable();
                        da.Fill(dt);
                        return dt;
                    }
                }
            }
        }

        // ===== SELECT: 생산로그(일자+필터) - 오버로드(2인자) =====
        // filter: "ALL" | "OK" | "NG"
        //   - OK  : final_result = '정상'
        //   - NG  : final_result <> '정상'
        public static DataTable LoadInspectionsByDate(DateTime targetDate, string filter)
        {
            // 2인자 호출을 지원해 Form2.cs의 기존 사용과 호환
            string whereExtra =
                (string.Equals(filter, "OK", StringComparison.OrdinalIgnoreCase))
                    ? " AND final_result = '정상' "
                : (string.Equals(filter, "NG", StringComparison.OrdinalIgnoreCase))
                    ? " AND final_result <> '정상' "
                    : " "; // ALL

            using (var conn = new MySqlConnection(_connStr))
            {
                conn.Open();
                string sql = $@"
SELECT inspection_id, inspection_time, final_result, defect_summary, created_at
FROM inspection
WHERE DATE(inspection_time) = @d
{whereExtra}
ORDER BY inspection_time DESC;
";
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@d", targetDate.Date);
                    using (var da = new MySqlDataAdapter(cmd))
                    {
                        var dt = new DataTable();
                        da.Fill(dt);
                        return dt;
                    }
                }
            }
        }

        // ===== SELECT: 시간대 요약(OK/NG) =====
        public static DataTable LoadHourlySummary(DateTime targetDate)
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                conn.Open();
                string sql = @"
SELECT 
    HOUR(inspection_time) AS hour,
    SUM(final_result = '정상')  AS ok_count,
    SUM(final_result <> '정상') AS ng_count
FROM inspection
WHERE DATE(inspection_time) = @d
GROUP BY HOUR(inspection_time)
ORDER BY hour;
";
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@d", targetDate.Date);
                    using (var da = new MySqlDataAdapter(cmd))
                    {
                        var dt = new DataTable();
                        da.Fill(dt);
                        return dt;
                    }
                }
            }
        }

        // ===== SELECT: 일간 요약(총/정상/불량) =====
        public static DataTable LoadDailySummary(DateTime targetDate)
        {
            using (var conn = new MySqlConnection(_connStr))
            {
                conn.Open();
                string sql = @"
SELECT 
    COUNT(*)                    AS total_count,
    SUM(final_result = '정상')  AS ok_count,
    SUM(final_result <> '정상') AS ng_count
FROM inspection
WHERE DATE(inspection_time) = @d;
";
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@d", targetDate.Date);
                    using (var da = new MySqlDataAdapter(cmd))
                    {
                        var dt = new DataTable();
                        da.Fill(dt);
                        return dt;
                    }
                }
            }
        }
    }
}
