// DatabaseService.cs
// ------------------
// 역할: MySQL 연동 유틸리티
//  - 검사 메인 기록(inspection) + 이미지(inspection_image) INSERT
//  - 하루/시간대 통계 SELECT
//  - [신규] 세부 검출 결과(inspection_result) INSERT
//
// 주의:
//  - 연결 문자열(_connStr) 환경에 맞춰 두세요.
//  - 트랜잭션은 각 API 내부에서 독립적으로 관리합니다.

using System;                                           // DateTime, Convert 등 기본 형식
using System.Collections.Generic;                       // List<T>
using MySql.Data.MySqlClient;                           // MySQL ADO.NET

public static class DatabaseService
{
    // ============================
    // 1) 연결 문자열
    // ============================
    //  - 포트/DB/계정 비밀번호는 현 환경에 맞춰 사용
    private static readonly string _connStr =
        "Server=127.0.0.1;Port=3306;Database=inspection;Uid=root;Pwd=1234";

    // ============================
    // 2) INSERT: 메인 검사 + 이미지
    // ============================
    //  - 한 번의 캔 검사 결과를 'inspection' 1건으로 기록
    //  - 같은 ID를 참조하여 'inspection_image'에 top/side 2건을 기록
    public static int InsertInspection(
        DateTime inspectionTime,                        // 검사 시각
        string finalResult,                             // "정상" / "불량" / "비정상" / "에러"
        string defectSummary,                           // 불량 사유(주요 1개) 또는 비고
        string topImagePath,                            // TOP 이미지 파일 경로
        string sideImagePath                            // SIDE 이미지 파일 경로
    )
    {
        // MySQL 연결 열기
        using (var conn = new MySqlConnection(_connStr))
        {
            conn.Open();                                // DB 연결

            // 트랜잭션 시작 (inspection + inspection_image 일관성 보장)
            using (var tran = conn.BeginTransaction())
            {
                try
                {
                    // 2-1) inspection INSERT 후 LAST_INSERT_ID()로 PK 획득
                    string insertInspectionSql = @"
                        INSERT INTO inspection
                            (inspection_time, final_result, defect_summary, created_at)
                        VALUES
                            (@time, @result, @summary, NOW());
                        SELECT LAST_INSERT_ID();
                    ";

                    int inspectionId;                  // 방금 INSERT한 inspection의 PK
                    using (var cmd = new MySqlCommand(insertInspectionSql, conn, tran))
                    {
                        cmd.Parameters.AddWithValue("@time", inspectionTime);      // 시간
                        cmd.Parameters.AddWithValue("@result", finalResult ?? ""); // 결과
                        cmd.Parameters.AddWithValue("@summary", defectSummary ?? ""); // 사유/요약
                        inspectionId = Convert.ToInt32(cmd.ExecuteScalar());       // PK 반환
                    }

                    // 2-2) inspection_image에 top/side 2건 INSERT
                    string insertImageSql = @"
                        INSERT INTO inspection_image
                            (inspection_id, camera_type, file_path, created_at)
                        VALUES
                            (@id, 'top',  @topPath,  NOW()),
                            (@id, 'side', @sidePath, NOW());
                    ";

                    using (var cmdImg = new MySqlCommand(insertImageSql, conn, tran))
                    {
                        cmdImg.Parameters.AddWithValue("@id", inspectionId);       // FK
                        cmdImg.Parameters.AddWithValue("@topPath", topImagePath ?? "");   // TOP 경로
                        cmdImg.Parameters.AddWithValue("@sidePath", sideImagePath ?? ""); // SIDE 경로
                        cmdImg.ExecuteNonQuery();                                   // 실행
                    }

                    // 모두 OK → 커밋
                    tran.Commit();
                    return inspectionId;                 // 생성된 PK 반환
                }
                catch                                   // 예외 발생 시
                {
                    tran.Rollback();                    // 롤백
                    throw;                              // 상위로 예외 전파
                }
            }
        }
    }

    // ============================
    // 3) [신규] 세부 결과 DTO
    // ============================
    //  - inspection_result 테이블에 한 줄을 표현하는 형식
    public class InspectionResultRow
    {
        public int InspectionId { get; set; }           // FK: inspection.inspection_id
        public string CameraType { get; set; }          // "top" | "side"
        public string DefectType { get; set; }          // 라벨 (예: "뚜껑없음")
        public string DefectValue { get; set; }         // 값(필요 시 사용, 없으면 빈문자)
        public double? Confidence { get; set; }         // 신뢰도(0.0~1.0) Nullable
        public string AdditionalInfo { get; set; }      // 부가정보(JSON 등)
    }

    // ============================
    // 4) [신규] INSERT: 세부 결과(다건)
    // ============================
    //  - 탐지된 객체 개수만큼 반복 INSERT
    //  - rows가 null/빈 경우 바로 반환
    public static void InsertInspectionResults(IEnumerable<InspectionResultRow> rows)
    {
        if (rows == null) return;                       // null → 아무 것도 안 함

        using (var conn = new MySqlConnection(_connStr))
        {
            conn.Open();                                // DB 연결

            // 세부 결과도 트랜잭션으로 묶어서 안정적으로 기록
            using (var tran = conn.BeginTransaction())
            {
                try
                {
                    // INSERT SQL (단건)
                    string sql = @"
                        INSERT INTO inspection_result
                            (inspection_id, camera_type, defect_type, defect_value, confidence, additional_info, created_at)
                        VALUES
                            (@insp, @cam, @type, @val, @conf, @info, NOW());
                    ";

                    // 각 행에 대해 반복 INSERT
                    foreach (var r in rows)
                    {
                        using (var cmd = new MySqlCommand(sql, conn, tran))
                        {
                            cmd.Parameters.AddWithValue("@insp", r.InspectionId);                // FK
                            cmd.Parameters.AddWithValue("@cam", r.CameraType ?? "");            // 카메라 종류
                            cmd.Parameters.AddWithValue("@type", r.DefectType ?? "");            // 라벨
                            cmd.Parameters.AddWithValue("@val", (object)(r.DefectValue ?? "") ?? DBNull.Value); // 값
                            cmd.Parameters.AddWithValue("@conf", (object)r.Confidence ?? DBNull.Value);          // 신뢰도
                            cmd.Parameters.AddWithValue("@info", (object)(r.AdditionalInfo ?? "") ?? DBNull.Value); // 부가정보
                            cmd.ExecuteNonQuery();                                               // 실행
                        }
                    }

                    tran.Commit();                          // 성공 → 커밋
                }
                catch
                {
                    tran.Rollback();                        // 실패 → 롤백
                    throw;                                  // 상위로 전달
                }
            }
        }
    }

    // ============================
    // 5) SELECT: 로그(날짜별)
    // ============================
    //  - 생산로그/이력 탭용 목록 조회
    //  - filter: "ALL" / "OK" / "NG"
    public class InspectionRecord
    {
        public DateTime Time { get; set; }              // 검사 시각
        public string Result { get; set; }              // 최종 결과
        public string Reason { get; set; }              // 사유/요약
        public string TopPath { get; set; }             // TOP 경로
        public string SidePath { get; set; }            // SIDE 경로
    }

    public static List<InspectionRecord> LoadInspectionsByDate(DateTime date, string filter)
    {
        var list = new List<InspectionRecord>();        // 결과 목록

        using (var conn = new MySqlConnection(_connStr))
        {
            conn.Open();                                // DB 연결

            // 날짜 조건 + (필터에 따라 WHERE 확장)
            string baseSql = @"
                SELECT 
                    i.inspection_time,
                    i.final_result,
                    i.defect_summary,
                    MAX(CASE WHEN img.camera_type='top'  THEN img.file_path END) AS top_path,
                    MAX(CASE WHEN img.camera_type='side' THEN img.file_path END) AS side_path
                FROM inspection i
                LEFT JOIN inspection_image img
                    ON i.inspection_id = img.inspection_id
                WHERE DATE(i.inspection_time) = @date
                /**FILTER_CLAUSE**/
                GROUP BY i.inspection_id
                ORDER BY i.inspection_time DESC;
            ";

            // 필터 구간 치환
            string filterClause = "";                   // 기본(ALL): 조건 없음
            if (filter == "OK")
            {
                filterClause = "AND i.final_result = '정상'";
            }
            else if (filter == "NG")
            {
                filterClause = "AND (i.final_result = '불량' OR i.final_result = '비정상')";
            }

            string finalSql = baseSql.Replace("/**FILTER_CLAUSE**/", filterClause);

            // 쿼리 실행
            using (var cmd = new MySqlCommand(finalSql, conn))
            {
                cmd.Parameters.AddWithValue("@date", date.Date); // 날짜 파라미터

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // 한 행씩 DTO로 변환
                        var rec = new InspectionRecord
                        {
                            Time = reader.GetDateTime("inspection_time"),
                            Result = reader["final_result"]?.ToString(),
                            Reason = reader["defect_summary"]?.ToString(),
                            TopPath = reader["top_path"]?.ToString(),
                            SidePath = reader["side_path"]?.ToString()
                        };
                        list.Add(rec);                // 목록에 추가
                    }
                }
            }
        }

        return list;                                   // 결과 반환
    }

    // ============================
    // 6) SELECT: 하루 요약(파이/카드)
    // ============================
    //  - 총 검사수/정상/불량 카운트
    public class DailySummary
    {
        public int Total { get; set; }                 // 총 검사수
        public int Ok { get; set; }                    // 정상수
        public int Ng { get; set; }                    // 불량/비정상 수

        public double NgRatePercent                    // 불량률(%)
        {
            get
            {
                if (Total <= 0) return 0.0;
                return (double)Ng * 100.0 / (double)Total;
            }
        }
    }

    public static DailySummary LoadDailySummary(DateTime date)
    {
        var result = new DailySummary();               // 결과 DTO

        using (var conn = new MySqlConnection(_connStr))
        {
            conn.Open();                               // DB 연결

            string sql = @"
                SELECT
                    SUM(1) AS total_cnt,
                    SUM(CASE WHEN i.final_result = '정상' THEN 1 ELSE 0 END) AS ok_cnt,
                    SUM(CASE WHEN (i.final_result = '불량' OR i.final_result = '비정상') THEN 1 ELSE 0 END) AS ng_cnt
                FROM inspection i
                WHERE DATE(i.inspection_time) = @date;
            ";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@date", date.Date);  // 날짜

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        // NULL 대비 변환
                        result.Total = reader["total_cnt"] == DBNull.Value ? 0 : Convert.ToInt32(reader["total_cnt"]);
                        result.Ok = reader["ok_cnt"] == DBNull.Value ? 0 : Convert.ToInt32(reader["ok_cnt"]);
                        result.Ng = reader["ng_cnt"] == DBNull.Value ? 0 : Convert.ToInt32(reader["ng_cnt"]);
                    }
                }
            }
        }

        return result;                                   // 반환
    }

    // ============================
    // 7) SELECT: 시간대별 요약(막대)
    // ============================
    //  - 0~23시 별 정상/불량 카운트
    public class HourlySummary
    {
        public int Hour { get; set; }                  // 시(0~23)
        public int Ok { get; set; }                    // 정상
        public int Ng { get; set; }                    // 불량/비정상
    }

    public static List<HourlySummary> LoadHourlySummary(DateTime date)
    {
        var list = new List<HourlySummary>();          // 결과 목록

        using (var conn = new MySqlConnection(_connStr))
        {
            conn.Open();                               // DB 연결

            string sql = @"
                SELECT 
                    HOUR(i.inspection_time) AS h,
                    SUM(CASE WHEN i.final_result = '정상' THEN 1 ELSE 0 END) AS ok_cnt,
                    SUM(CASE WHEN (i.final_result = '불량' OR i.final_result = '비정상') THEN 1 ELSE 0 END) AS ng_cnt
                FROM inspection i
                WHERE DATE(i.inspection_time) = @date
                GROUP BY HOUR(i.inspection_time)
                ORDER BY h;
            ";

            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@date", date.Date);   // 날짜

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var hs = new HourlySummary
                        {
                            Hour = Convert.ToInt32(reader["h"]),
                            Ok = reader["ok_cnt"] == DBNull.Value ? 0 : Convert.ToInt32(reader["ok_cnt"]),
                            Ng = reader["ng_cnt"] == DBNull.Value ? 0 : Convert.ToInt32(reader["ng_cnt"])
                        };
                        list.Add(hs);                              // 추가
                    }
                }
            }
        }

        return list;                                              // 반환
    }
}
