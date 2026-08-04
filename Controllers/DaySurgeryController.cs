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
        // 首次載入是 GET，查詢是 POST，日期不進網址
        // Auto 版才會跳過 GET，只驗 POST；ValidateAntiForgeryToken 連 GET 也驗會變 400
        [AutoValidateAntiforgeryToken]
        public IActionResult BedBooking(DateTime? DateS, DateTime? DateE)
        {
            // 沒帶參數就是首次載入，預設當天起算三天
            DateTime dateS = DateS ?? DateTime.Today;
            DateTime dateE = DateE ?? dateS.AddDays(3);

            ViewBag.DateS = dateS;
            ViewBag.DateE = dateE;

            // 先只抓資料，還沒綁到 View
            DataTable dt = QueryResv(dateS, dateE);

            return View();
        }

        // 階段一 (2) 查詢病人清單
        public IActionResult PatientList() => View();

        /// <summary>
        /// 查詢指定日期區間、指定科別、尚未產生住院號的預約住院資料
        /// </summary>
        private DataTable QueryResv(DateTime DateS, DateTime DateE)
        {
            // DB 存的是民國年月日 (1150730)，不是西元
            string sDateS = DateS.pRyyymmdd().pSQLValidator();
            string sDateE = DateE.pRyyymmdd().pSQLValidator();

            string SQL = "SELECT chRsReason, chRsPSec, * ";
            SQL += $"\n FROM DB_ADM..AdmResvTbl ";
            SQL += $"\n WHERE ";
            SQL += $"\n chRsPDate BETWEEN '{sDateS}' AND '{sDateE}' ";
            SQL += $"\n AND chRsPSec IN ('06', '08', 'VC') ";
            SQL += $"\n AND chRsAdmCaseNo IS NULL";

            return _db.executesqldt(SQL);
        }
    }
}
