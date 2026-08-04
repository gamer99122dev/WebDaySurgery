using System.Data;
using Microsoft.AspNetCore.Mvc;
using WebToolNet.UtilExtension;
using WebToolNet.UtilExtension.myDateTime;

namespace WebDaySurgery.Controllers
{
    public class DaySurgeryController : Controller
    {
        private readonly WebToolNet.DBConn.DBConn _db;

        public DaySurgeryController(WebToolNet.DBConn.DBConn db) => _db = db;

        // 階段一 (1) 預定床位
        public IActionResult BedBooking()
        {
            // 先只抓資料，還沒綁到 View
            DataTable dt = QueryResv(DateTime.Today, DateTime.Today.AddDays(3));

            return View();
        }

        // 階段一 (2) 查詢病人清單
        public IActionResult PatientList() => View();

        /// <summary>
        /// 查詢指定日期區間、指定科別、尚未產生住院號的預約住院資料
        /// </summary>
        private DataTable QueryResv(DateTime dateFrom, DateTime dateTo)
        {
            // DB 存的是民國年月日 (1150730)，不是西元
            string rocFrom = dateFrom.pRyyymmdd().pSQLValidator();
            string rocTo = dateTo.pRyyymmdd().pSQLValidator();

            string SQL = "SELECT chRsReason, chRsPSec, * ";
            SQL += $"\n FROM DB_ADM..AdmResvTbl ";
            SQL += $"\n WHERE ";
            SQL += $"\n chRsPDate BETWEEN '{rocFrom}' AND '{rocTo}' ";
            SQL += $"\n AND chRsPSec IN ('06', '08', 'VC') ";
            SQL += $"\n AND chRsAdmCaseNo IS NULL";

            return _db.executesqldt(SQL);
        }
    }
}
