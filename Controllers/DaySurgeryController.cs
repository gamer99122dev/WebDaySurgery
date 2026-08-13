using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text.RegularExpressions;
using WebDaySurgery.Models;
using WebToolNet.HIS2BusinessRule;
using WebToolNet.myDateTime;
using WebToolNet.UtilExtension;
using WebToolNet.UtilExtension.myDateTime;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WebDaySurgery.Controllers
{
    public class DaySurgeryController : Controller
    {
        private readonly WebToolNet.DBConn.DBConn _db;

        public DaySurgeryController(WebToolNet.DBConn.DBConn db) => _db = db;

        // 階段一 (1) 預定床位
        // 查詢條件只有日期，不敏感，查詢就用 GET 帶在網址上，
        // 沒有 POST 就不用 PRG，上一頁／重新整理／加書籤都正常
        [HttpGet]
        public IActionResult BedBooking(DateTime? DateS, DateTime? DateE)
        {
            // 沒帶參數就是首次載入，預設當天起算三天
            DateTime dateS = DateS ?? DateTime.Today;
            DateTime dateE = DateE ?? dateS.AddDays(3);

            ViewBag.DateS = dateS;
            ViewBag.DateE = dateE;

            ViewBag.Resv = QueryResv(dateS, dateE);

            return View();
        }

        // 病歷號不進網址，POST 轉 GET 之間改用 TempData 帶；清單和檢驗資料頁各用各的
        private const string TempChartNo = "ChartNo";
        private const string TempMrNo = "MrNo";

        // 階段一 (2) 查詢病人清單 (查詢)
        // 查完只存條件、轉 GET (PRG)，畫面永遠是 GET 畫出來的，
        // 這樣瀏覽器上一頁／重新整理才不會跳「確認重新送出表單」
        [HttpPost]
        public IActionResult PatientListSearch(DateTime? ResvDate, string? Nav, string? ChartNo)
        {
            // 沒帶參數就是首次載入，預設當天
            DateTime resvDate = ResvDate ?? DateTime.Today;

            // 前一日／今日／後一日，日期算在後端，前端不用 JS 也不會踩到 UTC 倒退一天
            resvDate = Nav switch
            {
                "prev" => resvDate.AddDays(-1),
                "next" => resvDate.AddDays(1),
                "today" => DateTime.Today,
                _ => resvDate,
            };

            // 查日期那張 form 不帶病歷號，等於使用者要看整天的清單，把上一次查的病歷號清掉
            string mrNo = ChartNo.pNullOrTrim();
            if (mrNo == "")
            {
                TempData.Remove(TempChartNo);
            }
            else
            {
                TempData[TempChartNo] = mrNo;
            }

            // 日期不算敏感資料，帶在網址上，上一頁／下一頁才回得到原本那天
            return RedirectToAction(nameof(PatientList), new { ResvDate = resvDate.ToString("yyyy-MM-dd") });
        }

        // 階段一 (2) 查詢病人清單 (畫面)
        [HttpGet]
        public IActionResult PatientList(DateTime? ResvDate)
        {
            // 沒帶參數就是首次載入，預設當天
            DateTime resvDate = ResvDate ?? DateTime.Today;

            // Peek 不會把值消掉，從檢驗資料頁回來、或重新整理，都還停在同一個查詢結果
            string mrNo = TempData.Peek(TempChartNo) as string ?? "";

            ViewBag.ResvDate = resvDate;
            ViewBag.MrNo = mrNo;

            // 查病歷號不看日期框，一律當天起算三個月內的預約，找的是「這個人接下來要開的刀」
            ViewBag.Patients = mrNo == ""
                ? QueryResv(resvDate, resvDate)
                : QueryResvByMrNo(mrNo, DateTime.Today, DateTime.Today.AddMonths(3));

            return View();
        }

        // 階段一 (2) 查詢病人清單 (欄位說明)
        // 純說明頁，沒有查詢條件。項目清單直接給判定時用的同一份設定，
        // 以後改 LabChkTeams／ExamChks，說明頁跟著變，不用記得回來改文件
        [HttpGet]
        public IActionResult PatientListHelp()
        {
            ViewBag.LabChkTeams = LabChkTeams;
            ViewBag.ExamChks = ExamChks;

            return View();
        }

        // 階段一 (3) 病患檢驗資料 (選人)
        // 病歷號走 POST body，不進網址，避免被改參數撈到別人的資料
        [HttpPost]
        public IActionResult LabResultSearch(string MrNo, DateTime ResvDate)
        {
            TempData[TempMrNo] = MrNo.pNullOrTrim();

            return RedirectToAction(nameof(LabResult), new { ResvDate = ResvDate.ToString("yyyy-MM-dd") });
        }

        // 階段一 (3) 病患檢驗資料 (畫面)
        [HttpGet]
        public IActionResult LabResult(DateTime? ResvDate)
        {
            // Peek 不會把值消掉，重新整理還是同一個病人
            string mrNo = TempData.Peek(TempMrNo) as string ?? "";

            // 直接打網址進來、或 TempData 過期了，就沒有病人可顯示，退回清單
            if (ResvDate == null || mrNo == "") return RedirectToAction(nameof(PatientList));

            // 清單本來就查過這一天，直接從同一份結果撈這個人，不用為了表頭再查一次
            Resv? patient = QueryResv(ResvDate.Value, ResvDate.Value).FirstOrDefault(p => p.MrNo == mrNo);

            // 查不到多半是清單開著、資料被別人改掉了，退回清單重查
            if (patient == null) return RedirectToAction(nameof(PatientList));

            ViewBag.ResvDate = ResvDate.Value;
            ViewBag.Patient = patient;

            // eGFR 要生日和性別，AdmResvTbl 沒有，得另外查病歷基本資料
            (string birthday, string sex) = QueryPatientBasic(mrNo);

            // 術前檢驗抓手術日往前 14 天
            List<Lab> labs = QueryLab(mrNo, ResvDate.Value.AddDays(-14), ResvDate.Value);

            ViewBag.Labs = AddReportFlag(AddCalcRows(labs, birthday, sex));

            return View();
        }

        /// <summary>
        /// 查病歷號的生日與性別，算 eGFR 用；查不到回兩個空字串
        /// </summary>
        private (string Birthday, string Sex) QueryPatientBasic(string MrNo)
        {
            string SQL = "SELECT chBirthday, chSex ";
            SQL += $"\n FROM DB_OPD..OpdMRBasicTbl ";
            SQL += $"\n WHERE ";
            SQL += $"\n chMRNo = '{MrNo.pSQLValidator()}' ";

            DataTable dt = _db.executesqldt(SQL);

            // 只有兩個欄位、也只有這裡用，不值得為它開一個類別
            return dt.Rows.Count == 0 ? ("", "") : (dt.Rows[0].pCol("chBirthday"), dt.Rows[0].pCol("chSex"));
        }

        /// <summary>
        /// 照 OPD 主程式 (ClinicalDataDetailLabNormal) 的規則補上計算列，並用同樣的順序排序
        /// </summary>
        private List<Lab> AddCalcRows(List<Lab> Labs, string Birthday, string Sex)
        {
            Computing computing = new Computing();
            DateComputing dateComputing = new DateComputing();

            int age = Birthday == "" ? 0 : Birthday.pAge().pToInt();

            // 原始列先整批搬過去，計算列再往後加；同組要撈另一個值時一律查原始清單，才不會撈到自己算出來的列
            List<Lab> rows = new List<Lab>(Labs);

            foreach (Lab row in Labs)
            {
                string ordNo = row.OrdNo;
                string head = row.Head;
                string val = row.Val;

                if (val == "") continue;

                // eGFR 兩式
                if (head == "CREA" && double.TryParse(val, out _))
                {
                    if (age != 0 && Sex != "")
                        AddCalcRow(rows, row, "eGFR(MDRD)", "ml/min/1.73㎡", computing.geteGFR(val, age, Sex), 2, "90", "");

                    // CKD-EPI 要用當次採檢日的年齡，不能用現在的年齡，否則同一筆報告的值會隨時間變動
                    if (Birthday != "" && Sex != "")
                    {
                        int rcpAge = dateComputing.TWDsAge(Birthday, row.RcpDTM.pLeft(7)).pToInt();

                        if (rcpAge != 0)
                            AddCalcRow(rows, row, "eGFR(CKD-EPI新公式)", "ml/min/1.73㎡", computing.geteGFRCKDEPI(val, rcpAge, Sex), 2, "90", "");
                    }
                }

                // UPCR
                if (ordNo == "L090404" && head.ToUpper().Contains("URINE"))
                {
                    string crea = SameGroupVal(Labs, row, r => EqNoCase(r.OrdNo, "L090161"), SameRcpDTM: true);
                    if (crea.pToDouble() > 0)
                        AddCalcRow(rows, row, "UPCR", "mg/g", computing.getUPCR(val, crea), 1, "0", "150");
                }

                // UACR
                if (ordNo == "L12111" && row.ItemSeq == "01")
                {
                    string crea = SameGroupVal(Labs, row, r => EqNoCase(r.OrdNo, "L090161"), SameRcpDTM: true);
                    if (crea.pToDouble() > 0)
                        AddCalcRow(rows, row, "UACR", "mg/g", computing.getUACR(val, crea), 1, "", "");
                }

                // 24 小時 Urine T.P
                if (ordNo == "L090401" && head.ToUpper().Contains("URINE"))
                {
                    string volume = SameGroupVal(Labs, row, r => EqNoCase(r.Head, "Volume"));
                    if (volume != "")
                        AddCalcRow(rows, row, "24小時Urine T.P", "mg/day", computing.get24HR_Urine_TP(volume, val), 1, "", "");
                }

                // TSAT
                if (ordNo == "L09035" && head.ToUpper().Contains("TIBC"))
                {
                    string iron = SameGroupVal(Labs, row, r => EqNoCase(r.Head, "IRON"));
                    if (iron != "" && val.pToDouble() > 0)
                        AddCalcRow(rows, row, "TSAT", "%", computing.getTSAT(iron, val), 2, "20", "");
                }

                // TLC
                if (head == "LYM" && row.Unit == "%")
                {
                    string wbc = SameGroupVal(Labs, row, r => EqNoCase(r.Head, "WBC"));
                    if (double.TryParse(wbc, out _) && double.TryParse(val, out _))
                        AddCalcRow(rows, row, "TLC", "cells/mm3", computing.getTLC(wbc, val), 1, "2000", "", KeepOrdNo: true);
                }
            }

            // 對到原本 DefaultView.Sort 的 "chTeamSeq, chGReqNo, chOrdNo, chReqNo desc, chVfDTM, chItemSeq asc"
            return rows.OrderBy(r => r.TeamSeq)
                       .ThenBy(r => r.GReqNo)
                       .ThenBy(r => r.OrdNo)
                       .ThenByDescending(r => r.ReqNo)
                       .ThenBy(r => r.VfDTM)
                       .ThenBy(r => r.ItemSeq)
                       .ToList();
        }

        // DataTable.Select 的字串比對本來就不分大小寫，改用 LINQ 後要自己補回來，
        // 否則 'Volume' 比不到 DB 存的 'VOLUME'
        private static bool EqNoCase(string A, string B) => string.Equals(A, B, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 照 OPD 主程式的規則，替每一列算出檢驗值的顏色旗標：+ 過高、- 過低、E 不判定、W 警示
        /// </summary>
        private static List<Lab> AddReportFlag(List<Lab> Labs)
        {
            foreach (Lab r in Labs)
            {
                // 外送的報告一律不判
                if (r.STCod != "")
                {
                    r.RPFlag = "E";
                    continue;
                }

                // 四方的報告直接看它給的 itemflag；但 eGFR/UACR/UPCR 是 HIS 自己算的，要自己判高低
                bool bLabsSource = r.LReqNo.pLeft(2) == "80";
                bool selfGenLab = r.Head.pIn("eGFR(MDRD)", "eGFR(CKD-EPI新公式)", "UACR", "UPCR");

                r.RPFlag = bLabsSource && !selfGenLab
                    ? LabItemFlagColor(r.ItemFlag)
                    : LabReportColor(r.Val, r.NL, r.NH);
            }

            return Labs;
        }

        private static string LabItemFlagColor(string ItemFlag)
        {
            return ItemFlag.Replace("HH", "+").Replace("LL", "-")
                           .Replace("H", "+").Replace("L", "-")
                           .Replace("P", "W").Replace("A", "W");
        }

        /// <summary>
        /// 比報告值與低／高值，回傳顏色旗標。整段照 OPD 主程式的 LabReportColor 搬過來
        /// </summary>
        private static string LabReportColor(string sReport, string sLow, string sHigh)
        {
            sReport = sReport.ToUpper();
            sLow = sLow.ToUpper();
            sHigh = sHigh.ToUpper();

            string RPFlag = "";

            double dTemp;

            string digitPattern = @"([0-9]+\.[0-9]+)|(\d+)";
            string dashRangePattern = @"(\d+)-(\d+)";

            //報告值為空白 或 報告值為Nagative
            if (sReport == "" || sReport.IndexOf("NEGATIVE") >= 0)
            {
                return "";
            }

            //報告值 和 低/高值 為相同
            if (sReport == sLow && sReport == sHigh)
            {
                return "";
            }

            // 報告值為 +/- 不判
            if (sReport == "+ / -")
            {
                return "E";
            }

            // 報告值為 0-2(數字-數字) 不判
            Match mReport = Regex.Match(sReport, dashRangePattern);
            if (mReport.Success)
            {
                return "E";
            }

            // 高底值裡面有 smoker 者，不判
            if (sLow.IndexOf("SMOKER") >= 0 || sHigh.IndexOf("SMOKER") >= 0)
            {
                return "E";
            }

            mReport = Regex.Match(sReport, digitPattern);

            //報告值含數值
            if (mReport.Success)
            {
                double.TryParse(mReport.Groups[0].Value, out double dReport);
                Match m = Regex.Match(sLow, digitPattern);

                if (m.Success)
                {
                    dTemp = 0d;
                    double.TryParse(m.Groups[0].Value, out dTemp);

                    //數值小於低值
                    if (dReport < dTemp)
                    {
                        RPFlag = "-";
                    }
                }
                else if (sLow.IndexOf("-") >= 0)
                {
                    // 有 -
                    RPFlag = "-";
                }

                m = Regex.Match(sHigh, digitPattern);

                if (m.Success)
                {
                    dTemp = 0d;
                    double.TryParse(m.Groups[0].Value, out dTemp);

                    //數值大於高值
                    if (dReport > dTemp)
                    {
                        RPFlag = "+";
                    }
                }
                else if (sHigh.IndexOf("-") >= 0)
                {
                    // 有 -
                    RPFlag = "+";
                }

                return RPFlag;
            }

            //報告值含+ ， 但不含 -
            if (sReport.IndexOf("+") >= 0 && sReport.IndexOf("-") < 0)
            {
                if (sLow.IndexOf("NEGATIVE") >= 0)
                {
                    RPFlag = "+";
                }
                else if (sLow.IndexOf("-") >= 0 && sHigh.IndexOf("-") >= 0)
                {
                    RPFlag = "+";
                }
                else if (sHigh.IndexOf("-") >= 0)
                {
                    RPFlag = "+";
                }

                return RPFlag;
            }

            //報告值含 -
            if (sReport.IndexOf("-") >= 0)
            {
                if (sReport == "-")
                {
                    RPFlag = "";
                }

                return RPFlag;
            }

            //報告值含POSITIVE
            if (sReport.IndexOf("POSITIVE") >= 0)
            {
                if ((sLow.IndexOf("NEGATIVE") >= 0 && sLow.IndexOf("NEGATIVE.") < 0) ||
                    (sHigh.IndexOf("NEGATIVE") >= 0 && sHigh.IndexOf("NEGATIVE.") < 0))
                {
                    RPFlag = "+";
                }

                return RPFlag;
            }

            // 判斷COVID-19結果陽性變紅色底色
            if (sReport == "陽性")
            {
                RPFlag = "+";
                return RPFlag;
            }

            return RPFlag;
        }

        /// <summary>
        /// 同一組 (GReqNo 相同，UPCR／UACR 還要 RcpDTM 也相同) 裡，符合條件又有值的第一筆檢驗值
        /// </summary>
        private static string SameGroupVal(List<Lab> Labs, Lab Row, Func<Lab, bool> Match, bool SameRcpDTM = false)
        {
            Lab? found = Labs.FirstOrDefault(r => r.GReqNo == Row.GReqNo
                                               && r.Val != ""
                                               && Match(r)
                                               && (!SameRcpDTM || r.RcpDTM == Row.RcpDTM));

            return found == null ? "" : found.Val;
        }

        /// <summary>
        /// 複製來源列再改掉項目名稱、值、單位、高低值，當成一筆計算列附在後面
        /// </summary>
        private static void AddCalcRow(List<Lab> Rows, Lab Src, string Head, string Unit, double Val, int Digits, string NL, string NH, bool KeepOrdNo = false)
        {
            Rows.Add(Src with
            {
                Head = Head,
                Unit = Unit,
                Val = Math.Round(Val, Digits).ToString(),
                NL = NL,
                NH = NH,

                // 計算列不是醫令，OrdNo 清掉；只有 TLC 要留著才排得到 LYM 旁邊
                OrdNo = KeepOrdNo ? Src.OrdNo : "",
            });
        }

        /// <summary>
        /// 查詢指定病歷號、指定日期區間內已收件的檢驗結果
        /// </summary>
        private List<Lab> QueryLab(string MrNo, DateTime DateS, DateTime DateE)
        {
            string sMrNo = MrNo.pSQLValidator();

            // chRcpDTM 是民國年月日時分 11 碼 (11501010000)，不是西元
            string sDateS = DateS.pRyyymmdd().pSQLValidator() + "0000";
            string sDateE = DateE.pRyyymmdd().pSQLValidator() + "2359";

            string SQL = "SELECT A.chPName, B.chGReqNo, RTrim(B.chLReqNo) as chLReqNo, B.chReqNo, B.chMRNo, B.chOrdNo, ";
            SQL += $"\n RTrim(B.chHead) as chHead, B.chHeadCHT as chOrdSNCht, RTrim(B.chSpeci) as chSpeci, ";
            SQL += $"\n case isnull(RTrim(B.chVal),'') when '' then RTrim(B.chCommt) else RTrim(B.chVal) end as chVal, ";
            SQL += $"\n B.chUnit, RTrim(B.chCommt) as chCommt, RTrim(B.chNL) as chNL, RTrim(B.chNH) as chNH, ";
            SQL += $"\n B.chAppDTM, B.chRcpDTM, B.chMdDTM, B.chVfDTM, ";
            // 修改時間只在報告真的被改過 (修改時間 <> 確認時間) 才給值，沒改過是空字串
            SQL += $"\n IIF(A.chModDTM <> A.chVerDTM, A.chModDTM, '') AS chModDTM2, ";
            SQL += $"\n A.chTeamNo, C.chTeamNam, A.chSTCod, B.chItemSeq, C.chTeamSeq, E.chSTNam, B.itemflag ";
            SQL += $"\n FROM DB_ADM..AdmRqtrcpLWTbl A ";
            SQL += $"\n LEFT OUTER JOIN DB_ADM..AdmResultRPTbl B ON A.chGReqNo = B.chGReqNo AND A.chReqNo = B.chReqNo ";
            SQL += $"\n LEFT OUTER JOIN DB_ADM..AdmLabSendToTbl E ON A.chSTCod = E.chSTCod ";
            SQL += $"\n , DB_ADM..AdmLabTeamTbl C ";
            SQL += $"\n WHERE ";
            SQL += $"\n A.chTeamNo = C.chTeamNo ";
            SQL += $"\n AND B.chMRNo IN ('{sMrNo}') ";
            SQL += $"\n AND B.chStat IN ('10','20','30','60','70') ";
            SQL += $"\n AND B.chRcpDTM BETWEEN '{sDateS}' AND '{sDateE}' ";
            SQL += $"\n ORDER BY B.chGReqNo, B.chLReqNo, B.chReqNo";

            // SELECT 還有幾個目前沒人用的欄位 (chPName、chCommt、chAppDTM…)，用到再補上來
            return _db.executesqldt(SQL).AsEnumerable().Select(r => new Lab
            {
                GReqNo = r.pCol("chGReqNo"),
                LReqNo = r.pCol("chLReqNo"),
                ReqNo = r.pCol("chReqNo"),
                OrdNo = r.pCol("chOrdNo"),
                Head = r.pCol("chHead"),
                Speci = r.pCol("chSpeci"),
                Val = r.pCol("chVal"),
                Unit = r.pCol("chUnit"),
                NL = r.pCol("chNL"),
                NH = r.pCol("chNH"),
                RcpDTM = r.pCol("chRcpDTM"),
                VfDTM = r.pCol("chVfDTM"),
                ModDTM = r.pCol("chModDTM2"),
                TeamNo = r.pCol("chTeamNo"),
                TeamNam = r.pCol("chTeamNam"),
                TeamSeq = r.pCol("chTeamSeq"),
                STCod = r.pCol("chSTCod"),
                ItemSeq = r.pCol("chItemSeq"),
                ItemFlag = r.pCol("itemflag"),
            }).ToList();
        }

        /// <summary>
        /// 查詢指定病歷號、指定日期區間內、指定科別、尚未產生住院號的預約住院資料
        /// </summary>
        private List<Resv> QueryResvByMrNo(string MrNo, DateTime DateS, DateTime DateE)
        {
            string sMrNo = MrNo.pSQLValidator();

            // DB 存的是民國年月日 (1150730)，不是西元
            string sDateS = DateS.pRyyymmdd().pSQLValidator();
            string sDateE = DateE.pRyyymmdd().pSQLValidator();

            string SQL = "SELECT chRsPName, chRsMrNo, chRsPSex, chRsPTelH, chRsPTelO, chRsReason, chRsPSec, chRsDrID1, chRsDrID1Name, chRsAdmCaseNo, chRsPDate ";
            SQL += $"\n FROM DB_ADM..AdmResvTbl ";
            SQL += $"\n WHERE ";
            SQL += $"\n chRsMrNo = '{sMrNo}' ";
            SQL += $"\n AND chRsPDate BETWEEN '{sDateS}' AND '{sDateE}' ";
            SQL += $"\n AND chRsPSec IN ('06', '08', 'VC') ";
            SQL += $"\n AND chRsAdmCaseNo IS NULL";
            // 這裡的區間橫跨數個月，畫面照日期由近到遠排
            SQL += $"\n ORDER BY chRsPDate";


            DataTable dt = _db.executesqldt(SQL);

            // 這個人這段期間沒預約就不用再查排程了
            if (dt.Rows.Count == 0) return new List<Resv>();

            //手術排程、術式
            SQL = "\n select ";
            SQL += "\n b.OROrdName1 as '術式一', ORSchDate, ORSchTime, a.chRsMrNo, a.chRsPName, a.chRsPDate ";
            SQL += "\n from DB_ADM..AdmResvTbl a ";
            SQL += "\n join DB_MIDDLE..JAG_OR_opsche_chr_basic b ";
            SQL += "\n on ";
            SQL += "\n a.chRsPDate2 = b.RegDate and a.chRsPTime = b.RegTime and a.chRsPRoom = b.RegRoom and a.chRsPNo = b.RegNo";
            SQL += "\n where ";
            SQL += $"\n a.chRsMrNo = '{sMrNo}' ";
            SQL += $"\n and a.chRsPDate BETWEEN '{sDateS}' AND '{sDateE}' ";
            SQL += "\n and isnull(b.OROrdName1,'' ) <> '' ";

            DataTable dtJAG = _db.executesqldt(SQL);

            // 這裡的區間橫跨三個月，同一個人本來就會有好幾天的預約，
            // 病歷號要配上住院日當 key，才不會把別天的刀貼到這一列
            Dictionary<string, DataRow> schs = new Dictionary<string, DataRow>();

            // 同一筆預約排到兩台刀的話這裡只留最後一筆，畫面一列也只放得下一台
            foreach (DataRow i in dtJAG.AsEnumerable())
            {
                schs[$"{i.pCol("chRsMrNo")}|{i.pCol("chRsPDate")}"] = i;
            }

            // 查的就是同一個人，每一列的病歷號都一樣，判定時當 key 用
            string mrNoKey = dt.Rows[0].pCol("chRsMrNo");
            List<string> mrList = new List<string> { mrNoKey };

            // 這支查的是「今天起三個月內的預約」，同一個人可能有好幾台刀，
            // 每一列的檢驗／檢查都要照自己那台刀的住院日往前推 14 天，
            // 一個區間套全部會把別台刀的資料算進來，所以逐個住院日各查一次
            Dictionary<string, List<ChkItem>> labChks = new Dictionary<string, List<ChkItem>>();
            Dictionary<string, List<ChkItem>> examChks = new Dictionary<string, List<ChkItem>>();

            foreach (string pDate in dt.AsEnumerable().Select(i => i.pCol("chRsPDate")).Distinct())
            {
                //檢驗
                string LwDateE = pDate.pSQLValidator();
                string LwDateS = pDate.pToDateTime().AddDays(-14).pRyyymmdd();

                // 只要知道「這個人這一類有沒有開這個項目」，撈這三欄就夠；
                // select * 會讓 A、B 兩張表的同名欄位在 DataTable 裡被自動改名 (chMRNo1)，反而不好取值
                SQL = $"\n select B.chMRNo, A.chTeamNo, RTrim(B.chHead) as chHead ";
                SQL += $"\n FROM DB_ADM..AdmRqtrcpLWTbl A  ";
                SQL += $"\n LEFT JOIN DB_ADM..AdmResultRPTbl B         ";
                SQL += $"\n      ON A.chGReqNo = B.chGReqNo AND A.chReqNo = B.chReqNo ";
                SQL += $"\n LEFT JOIN DB_ADM..AdmLabSendToTbl E ON A.chSTCod = E.chSTCod   ";
                SQL += $"\n    , DB_ADM..AdmLabTeamTbl C       ";
                SQL += $"\n WHERE A.chTeamNo = C.chTeamNo ";
                SQL += $"\n   AND B.chMRNo = '{sMrNo}' ";
                SQL += $"\n   AND B.chStat IN ('10','20','30','60','70') ";
                // chRcpDTM 是民國年月日時分 11 碼 (11501010000)，7 碼日期要補上時分才比得對，
                // 不然 '11508122359' > '1150812'，當天的報告會被 BETWEEN 濾掉
                SQL += $"\n   AND B.chRcpDTM BETWEEN '{LwDateS}0000' AND '{LwDateE}2359' ";
                // 畫面只看這幾類，其他類別不用撈回來
                SQL += $"\n   AND A.chTeamNo IN ({LabChkTeams.Select(t => t.TeamNo).ToList().pJoinWithQuote()}) ";

                DataTable dtLw = _db.executesqldt(SQL);

                // 只有一個人，但旗標怎麼算跟清單同一套
                labChks[pDate] = BuildLabChk(dtLw, mrList)[mrNoKey];

                //檢查
                // 起迄跟上面檢驗一樣，都是這台刀的住院日往前推 14 天
                string XDateS = LwDateS;
                string XwDateE = LwDateE;

                // 病歷號要一起撈回來，等一下靠它把醫令分給每一列
                SQL = $"\n select B.chOp0PMrNo, chOp1Date, chOp1Time, chOp1Room, intOp1No, chOp4OrdName, chOp4OrdNo, chOp4OrdHis ";
                SQL += $"\n From DB_OPD..OpdOrdTbl A  ";
                SQL += $"\n Join DB_OPD..OpdRegPtnTbl B  ";
                SQL += $"\n On A.chOp1Date=B.chOp0Date And A.chOp1Time=B.chOp0Time And A.chOp1Room=B.chOp0Room And A.intOp1No=B.intOp0No   ";
                SQL += $"\n where ";
                SQL += $"\n chOp1Date between '{XDateS}' and '{XwDateE}' ";
                SQL += $"\n and B.chOp0PMrNo = '{sMrNo}' ";
                // 畫面只看這三項，其他醫令不用撈回來；這裡跟判定用的是同一份設定
                SQL += $"\n and ( {ExamChks.Select(e => $"A.{e.Col} in ({e.Codes.ToList().pJoinWithQuote()})").ToList().pJoin(" or ")} ) ";

                DataTable dtX = _db.executesqldt(SQL);

                examChks[pDate] = BuildExamChk(dtX, mrList)[mrNoKey];
            }

            return dt.AsEnumerable().Select(r =>
            {
                // 沒排刀的預約一樣要留在清單上，只是這兩欄空著
                schs.TryGetValue($"{r.pCol("chRsMrNo")}|{r.pCol("chRsPDate")}", out DataRow? sch);

                return new Resv
                {
                    PName = r.pCol("chRsPName").pReplaceEUDC(),
                    MrNo = r.pCol("chRsMrNo"),
                    Sex = r.pCol("chRsPSex"),
                    TelH = r.pCol("chRsPTelH"),
                    TelO = r.pCol("chRsPTelO"),
                    SecNo = r.pCol("chRsPSec"),
                    DrName = r.pCol("chRsDrID1Name").pReplaceEUDC(),
                    PDate = r.pCol("chRsPDate"),

                    // 民國日期 1150812 接上時間 1300 剛好是 pToDateTime 吃的 11 碼
                    OpSch = sch == null ? "" : $"{(sch.pCol("ORSchDate") + sch.pCol("ORSchTime")).pToDateTime():yyyy/MM/dd HH:mm}",
                    OpName = sch?.pCol("術式一") ?? "",

                    // 每一列照自己的住院日算，key 就是從這批資料撈出來的，一定找得到
                    Labs = labChks[r.pCol("chRsPDate")],
                    Exams = examChks[r.pCol("chRsPDate")],
                };
            }).ToList();
        }

        /// <summary>
        /// 查詢指定日期區間、指定科別、尚未產生住院號的預約住院資料
        /// </summary>
        private List<Resv> QueryResv(DateTime DateS, DateTime DateE)
        {
            // DB 存的是民國年月日 (1150730)，不是西元
            string sDateS = DateS.pRyyymmdd().pSQLValidator();
            string sDateE = DateE.pRyyymmdd().pSQLValidator();

            string SQL = "SELECT chRsPName, chRsMrNo, chRsPSex, chRsPTelH, chRsPTelO, chRsReason, chRsPSec, chRsDrID1, chRsDrID1Name, chRsAdmCaseNo, chRsPDate ";
            SQL += $"\n FROM DB_ADM..AdmResvTbl ";
            SQL += $"\n WHERE ";
            SQL += $"\n chRsPDate BETWEEN '{sDateS}' AND '{sDateE}' ";
            SQL += $"\n AND chRsPSec IN ('06', '08', 'VC') ";
            SQL += $"\n AND chRsAdmCaseNo IS NULL";


            DataTable dt = _db.executesqldt(SQL);

            List<string> MrList = new List<string>();

            foreach (var i in dt.AsEnumerable())
            {
                MrList.Add(i.pCol("chRsMrNo"));
            }

            // 這天沒人就不用查排程了，而且 in () 是語法錯誤
            if (MrList.Count == 0) return new List<Resv>();

            //手術排程、術式
            SQL = "\n select ";
            SQL += "\n b.OROrdName1 as '術式一', ORSchDate, ORSchTime, a.chRsMrNo, a.chRsPName, a.chRsPDate ";
            SQL += "\n from DB_ADM..AdmResvTbl a ";
            SQL += "\n join DB_MIDDLE..JAG_OR_opsche_chr_basic b ";
            SQL += "\n on ";
            SQL += "\n a.chRsPDate2 = b.RegDate and a.chRsPTime = b.RegTime and a.chRsPRoom = b.RegRoom and a.chRsPNo = b.RegNo";
            SQL += "\n where ";
            SQL += $"\n a.chRsMrNo in  ({MrList.pJoinWithQuote()}) ";
            SQL += $"\n and a.chRsPDate BETWEEN '{sDateS}' AND '{sDateE}' ";
            SQL += "\n and isnull(b.OROrdName1,'' ) <> '' ";
            SQL += "\n ";
            SQL += "\n ";
            DataTable dtJAG = _db.executesqldt(SQL);

            // BedBooking 一次查三天，同一個病歷號可能有好幾天的預約，
            // 病歷號要配上住院日當 key，才不會把別天的刀貼到這一列
            Dictionary<string, DataRow> schs = new Dictionary<string, DataRow>();

            // 同一筆預約排到兩台刀的話這裡只留最後一筆，畫面一列也只放得下一台
            foreach (DataRow i in dtJAG.AsEnumerable())
            {
                schs[$"{i.pCol("chRsMrNo")}|{i.pCol("chRsPDate")}"] = i;
            }

            //檢驗
            string LwDateS = sDateE.pToDateTime().AddDays(-14).pRyyymmdd();
            string LwDateE = sDateE;

            // 只要知道「這個人這一類有沒有開這個項目」，撈這三欄就夠；
            // select * 會讓 A、B 兩張表的同名欄位在 DataTable 裡被自動改名 (chMRNo1)，反而不好取值
            SQL = $"\n select B.chMRNo, A.chTeamNo, RTrim(B.chHead) as chHead ";
            SQL += $"\n FROM DB_ADM..AdmRqtrcpLWTbl A  ";
            SQL += $"\n LEFT JOIN DB_ADM..AdmResultRPTbl B         ";
            SQL += $"\n      ON A.chGReqNo = B.chGReqNo AND A.chReqNo = B.chReqNo ";
            SQL += $"\n LEFT JOIN DB_ADM..AdmLabSendToTbl E ON A.chSTCod = E.chSTCod   ";
            SQL += $"\n    , DB_ADM..AdmLabTeamTbl C       ";
            SQL += $"\n WHERE A.chTeamNo = C.chTeamNo ";
            SQL += $"\n   AND B.chMRNo IN ({MrList.pJoinWithQuote()}) ";
            SQL += $"\n   AND B.chStat IN ('10','20','30','60','70') ";
            // chRcpDTM 是民國年月日時分 11 碼 (11501010000)，7 碼日期要補上時分才比得對，
            // 不然 '11508122359' > '1150812'，當天的報告會被 BETWEEN 濾掉
            SQL += $"\n   AND B.chRcpDTM BETWEEN '{LwDateS}0000' AND '{LwDateE}2359' ";
            // 畫面只看這幾類，其他類別不用撈回來
            SQL += $"\n   AND A.chTeamNo IN ({LabChkTeams.Select(t => t.TeamNo).ToList().pJoinWithQuote()}) ";

            DataTable dtLw = _db.executesqldt(SQL);

            Dictionary<string, List<ChkItem>> labChks = BuildLabChk(dtLw, MrList);


            //檢查
            string XDateS = sDateE.pToDateTime().AddDays(-14).pRyyymmdd();
            string XwDateE = sDateE;
            // 病歷號要一起撈回來，等一下靠它把醫令分給每一列
            SQL = $"\n select B.chOp0PMrNo, chOp1Date, chOp1Time, chOp1Room, intOp1No, chOp4OrdName, chOp4OrdNo, chOp4OrdHis ";
            SQL += $"\n From DB_OPD..OpdOrdTbl A  ";
            SQL += $"\n Join DB_OPD..OpdRegPtnTbl B  ";
            SQL += $"\n On A.chOp1Date=B.chOp0Date And A.chOp1Time=B.chOp0Time And A.chOp1Room=B.chOp0Room And A.intOp1No=B.intOp0No   ";
            SQL += $"\n where ";
            SQL += $"\n chOp1Date between '{XDateS}' and '{XwDateE}' ";
            SQL += $"\n and B.chOp0PMrNo in ({MrList.pJoinWithQuote()}) ";
            // 畫面只看這三項，其他醫令不用撈回來；這裡跟下面判定用的是同一份設定
            SQL += $"\n and ( {ExamChks.Select(e => $"A.{e.Col} in ({e.Codes.ToList().pJoinWithQuote()})").ToList().pJoin(" or ")} ) ";

            DataTable dtX = _db.executesqldt(SQL);

            Dictionary<string, List<ChkItem>> examChks = BuildExamChk(dtX, MrList);

            // chRsReason、chRsDrID1 目前畫面沒用到，要用再加一個屬性對上來
            return dt.AsEnumerable().Select(r =>
            {
                // 沒排刀的病人一樣要留在清單上，只是這兩欄空著
                schs.TryGetValue($"{r.pCol("chRsMrNo")}|{r.pCol("chRsPDate")}", out DataRow? sch);

                return new Resv
                {
                    PName = r.pCol("chRsPName").pReplaceEUDC(),
                    MrNo = r.pCol("chRsMrNo"),
                    Sex = r.pCol("chRsPSex"),
                    TelH = r.pCol("chRsPTelH"),
                    TelO = r.pCol("chRsPTelO"),
                    SecNo = r.pCol("chRsPSec"),
                    DrName = r.pCol("chRsDrID1Name").pReplaceEUDC(),
                    PDate = r.pCol("chRsPDate"),

                    // 民國日期 1150812 接上時間 1300 剛好是 pToDateTime 吃的 11 碼
                    OpSch = sch == null ? "" : $"{(sch.pCol("ORSchDate") + sch.pCol("ORSchTime")).pToDateTime():yyyy/MM/dd HH:mm}",
                    OpName = sch?.pCol("術式一") ?? "",

                    // MrList 就是從這批資料撈出來的，一定找得到
                    Labs = labChks[r.pCol("chRsMrNo")],
                    Exams = examChks[r.pCol("chRsMrNo")],
                };
            }).ToList();
        }

        // 術前只看這三類檢驗，以及每一類該有的項目 (chHead)；類別代碼見 DB_ADM..AdmLabTeamTbl。
        // 要多看一類、或某一類要增減項目，改這裡就好
        private static readonly (string TeamNo, string TeamNam, string[] Heads)[] LabChkTeams =
        {
            ("C", "生化", new[] { "BUN", "CREA", "Na", "K", "AST", "ALT" }),

            ("H", "血液", new[] { "WBC", "RBC", "Hb", "Hct", "MCV", "MCH", "MCHC", "Platelet", "RDW",
                                  "NEU", "LYM", "MONO", "EOS", "BASO", "NEU#", "LYM#", "MONO#", "EOS#", "BASO#",
                                  "P.T.", "PT INR", "MNPT", "APTT", "APTT RATIO" }),

            ("J", "血糖檢查", new[] { "GLU(PC)" }),
        };

        /// <summary>
        /// 把一批人的檢驗結果整理成「病歷號 => 每一類開齊了沒」，沒有任何檢驗的人也會有一筆 (三類都是 X)
        /// </summary>
        private static Dictionary<string, List<ChkItem>> BuildLabChk(DataTable dtLab, List<string> MrList)
        {
            // 只在意有沒有開這個項目，值是多少不管，湊成 病歷號|類別|項目 一個集合就夠比對。
            // 忽略大小寫，DB 存的是 Na、Hb，設定裡怎麼打都對得到
            HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow r in dtLab.AsEnumerable())
            {
                done.Add($"{r.pCol("chMRNo")}|{r.pCol("chTeamNo")}|{r.pCol("chHead")}");
            }

            Dictionary<string, List<ChkItem>> chks = new Dictionary<string, List<ChkItem>>();

            // 同一個病歷號在清單上可能有好幾天的預約，檢驗抓的是同一個區間，算一次就好
            foreach (string mrNo in MrList.Distinct())
            {
                chks[mrNo] = LabChkTeams.Select(t =>
                {
                    int have = t.Heads.Count(h => done.Contains($"{mrNo}|{t.TeamNo}|{h}"));

                    return new ChkItem(t.TeamNam, have == 0 ? "X" : have == t.Heads.Length ? "V" : "!");
                }).ToList();
            }

            return chks;
        }

        // 術前只看這三項檢查。32001C 那類是健保碼 (chOp4OrdHis)，L18001 是院內醫令碼 (chOp4OrdNo)；
        // 這份設定同時被上面組 SQL 條件和下面判定用，改一處就好
        private static readonly (string Nam, string Col, string[] Codes)[] ExamChks =
        {
            ("CXR", "chOp4OrdHis", new[] { "32001C", "32002C" }),
            ("KUB", "chOp4OrdHis", new[] { "32006C", "32011C" }),
            ("EKG", "chOp4OrdNo", new[] { "L18001" }),
        };

        /// <summary>
        /// 把一批人的門診醫令整理成「病歷號 => 每一項檢查開了沒」。
        /// 一項只要對到任一個代碼就算有，所以只會是 V 或 X
        /// </summary>
        private static Dictionary<string, List<ChkItem>> BuildExamChk(DataTable dtExam, List<string> MrList)
        {
            // 醫令先照病歷號分堆，每個人只要看自己那幾列
            ILookup<string, DataRow> byMrNo = dtExam.AsEnumerable().ToLookup(r => r.pCol("chOp0PMrNo"));

            Dictionary<string, List<ChkItem>> chks = new Dictionary<string, List<ChkItem>>();

            foreach (string mrNo in MrList.Distinct())
            {
                // 沒醫令的人 byMrNo[mrNo] 是空的，三項就都是 X
                chks[mrNo] = ExamChks.Select(e =>
                    new ChkItem(e.Nam, byMrNo[mrNo].Any(r => e.Codes.Contains(r.pCol(e.Col))) ? "V" : "X")).ToList();
            }

            return chks;
        }
    }
}
