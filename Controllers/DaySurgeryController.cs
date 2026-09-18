using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Text.RegularExpressions;
using WebDaySurgery.Models;
using WebDaySurgery.Services;
using WebToolNet.Data;
using WebToolNet.Dates;
using WebToolNet.Extensions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WebDaySurgery.Controllers
{
    public class DaySurgeryController : Controller
    {
        private readonly DBConn _db;

        // ── 設定 ────────────────────────────────────────────────
        // 要切換的開關、要增減的清單都集中在這裡，改這裡就好

        // chRsReason 第七位 1 = 日間手術。測試環境還沒有這一位的資料，
        // 先設 false 全部撈出來；要只顯示日間手術的病人就改成 true
        private const bool DaySurgeryOnly = true;

        // 麻醉系統要的使用者 ID (必填)。這支程式還沒有登入機制，先固定帶一個，接上登入後改帶登入者
        private const string AnesUserId = "11208";

        // 術前檢驗／檢查／備血／心電圖報告／麻醉評估都抓手術日往前這麼多個月 (2026-09-17 需求者定的，跟門診系統心電圖畫面一樣抓 3 個月)。
        // 畫面 (LabResult、PatientListHelp) 的「往前 N 個月」文案走 ViewBag 吃這個常數，改這裡畫面跟著變
        private const int LookbackMonths = 3;

        // 術前只看這三類檢驗，以及每一類該有的項目 (chHead)；類別代碼見 DB_ADM..AdmLabTeamTbl。
        // 要多看一類、或某一類要增減項目，改這裡就好。
        // 一項有好幾種寫法、有任一種就算數的，用 / 串起來當同一項 (GLU(PC)/GLU(AC))
        private static readonly (string TeamNo, string TeamNam, string[] Heads)[] LabChkTeams =
        {
            ("C", "生化", new[] { "BUN", "CREA", "Na", "K", "AST", "ALT" }),

            ("H", "血液", new[] { "WBC", "RBC", "Hb", "Hct", "MCV", "MCH", "MCHC", "Platelet", "RDW",
                                  "NEU", "LYM", "MONO", "EOS", "BASO", "NEU#", "LYM#", "MONO#", "EOS#", "BASO#",
                                  "P.T.", "PT INR", "MNPT", "APTT", "APTT RATIO" }),

            ("J", "血糖檢查", new[] { "GLU(PC)/GLU(AC)" }),
        };

        // 心電圖的院內醫令碼 (chOp4OrdNo)。清單判「開了沒」和檢查報告頁撈報告都用它
        private const string EkgOrdNo = "L18001";

        // 胸部X光／腹部X光的健保碼 (chOp4OrdHis)。同上，清單和檢查報告頁共用
        private static readonly string[] CxrOrdHis = { "32001C", "32002C" };
        private static readonly string[] KubOrdHis = { "32006C", "32011C" };

        // 術前只看這三項檢查。32001C 那類是健保碼 (chOp4OrdHis)，L18001 是院內醫令碼 (chOp4OrdNo)；
        // 這份設定同時被組 SQL 條件和判定用，改一處就好
        private static readonly (string Nam, string Col, string[] Codes)[] ExamChks =
        {
            ("CXR", "chOp4OrdHis", CxrOrdHis),
            ("KUB", "chOp4OrdHis", KubOrdHis),
            ("EKG", "chOp4OrdNo", new[] { EkgOrdNo }),
        };

        // 備血的院內醫令碼 (chOp4OrdNo)。只有一項，開了就是有，不像檢查要比好幾個代碼
        private const string BloodPrepOrdNo = "L11004";

        // 病歷號不進網址，POST 轉 GET 之間改用 TempData 帶；清單和檢驗資料頁各用各的
        private const string TempChartNo = "ChartNo";
        private const string TempMrNo = "MrNo";

        // ── 設定結束 ────────────────────────────────────────────

        public DaySurgeryController(DBConn db) => _db = db;

        // 階段一 (1) 預定床位
        // 查詢條件只有日期，不敏感，查詢就用 GET 帶在網址上，
        // 沒有 POST 就不用 PRG，上一頁／重新整理／加書籤都正常
        [HttpGet]
        public IActionResult BedBooking(DateTime? DateS, DateTime? DateE)
        {
            // 沒帶參數就是首次載入，預設當天起算七天
            DateTime dateS = DateS ?? DateTime.Today;
            DateTime dateE = DateE ?? dateS.AddDays(7);

            ViewBag.DateS = dateS;
            ViewBag.DateE = dateE;

            ViewBag.Resv = QueryResv(dateS, dateE);

            return View();
        }

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
                // 打 123 就是要查 0000000123；放後端補，掃條碼／打字／Enter／按鈕都走這一條，不用 JS
                TempData[TempChartNo] = mrNo.PadLeft(10, '0');
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
            List<Resv> patients = mrNo == ""
                ? QueryResv(resvDate, resvDate)
                : QueryResvByMrNo(mrNo, DateTime.Today, DateTime.Today.AddMonths(3));
            ViewBag.Patients = patients;

            // 查病歷號 0 筆要分「打錯號碼」和「有這個人但沒排刀」：打錯被當成沒排刀就危險了。有結果就不用多查
            ViewBag.MrNoFound = mrNo == "" || patients.Count > 0 || QueryPatientBasic(mrNo).Found;

            return View();
        }

        // 階段一 (2) 查詢病人清單 (欄位說明)
        // 純說明頁，沒有查詢條件。項目清單直接給判定時用的同一份設定，
        // 以後改 LabChkTeams／ExamChks，說明頁跟著變，不用記得回來改文件
        [HttpGet]
        public IActionResult PatientListHelp(DateTime? ResvDate)
        {
            // 只是為了「回查詢病人清單」和導覽列能帶回原本那天，說明內容不看日期
            ViewBag.ResvDate = ResvDate;

            ViewBag.LabChkTeams = LabChkTeams;
            ViewBag.ExamChks = ExamChks;
            ViewBag.BloodPrepOrdNo = BloodPrepOrdNo;
            ViewBag.LookbackMonths = LookbackMonths;

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
            (string birthday, string sex, _) = QueryPatientBasic(mrNo);

            // 術前檢驗抓手術日往前 LookbackMonths 個月
            List<Lab> labs = QueryLab(mrNo, ResvDate.Value.AddMonths(-LookbackMonths), ResvDate.Value);

            ViewBag.Labs = AddReportFlag(AddCalcRows(labs, birthday, sex));
            ViewBag.LookbackMonths = LookbackMonths;

            return View();
        }

        // 階段一 (3) 檢查報告 (選人)
        // 跟檢驗資料同一套：病歷號走 POST body 不進網址，只有日期帶在網址上
        [HttpPost]
        public IActionResult ExamResultSearch(string MrNo, DateTime ResvDate)
        {
            TempData[TempMrNo] = MrNo.pNullOrTrim();

            return RedirectToAction(nameof(ExamResult), new { ResvDate = ResvDate.ToString("yyyy-MM-dd") });
        }

        // 階段一 (3) 檢查報告 (畫面)
        // CXR／KUB／EKG 三項同一頁，沒開單的那項顯示無結果；三項的開單狀況清單已經算好，直接用 patient.Exams
        [HttpGet]
        public IActionResult ExamResult(DateTime? ResvDate)
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

            // 報告跟清單判「開了沒」同一個窗，都是手術日往前 LookbackMonths 個月
            ViewBag.Ekgs = QueryEkg(mrNo, ResvDate.Value.AddMonths(-LookbackMonths), ResvDate.Value);
            ViewBag.Cxrs = QueryRad(CxrOrdHis, mrNo, ResvDate.Value.AddMonths(-LookbackMonths), ResvDate.Value);
            ViewBag.Kubs = QueryRad(KubOrdHis, mrNo, ResvDate.Value.AddMonths(-LookbackMonths), ResvDate.Value);

            return View();
        }

        // 階段一 (2) 麻醉紀錄 — 門診評估單
        // 網址要先跟麻醉系統的 API 換，換到才知道導去哪，所以不能直接寫在 href 上，中間得走這一手
        [HttpPost]
        public async Task<IActionResult> AnesRecord(string MrNo, DateTime ResvDate)
        {
            // 術前評估跟檢驗同一個區間
            string url = await ANESCaller.GetUrl(MrNo.pNullOrTrim(), AnesUserId,
                                                 ResvDate.AddMonths(-LookbackMonths).ToString("yyyy/MM/dd"),
                                                 ResvDate.ToString("yyyy/MM/dd"));

            // 查不到的時候 API 回的是訊息不是網址，原樣秀出來，不要導去怪地方
            return url.StartsWith("http") ? Redirect(url) : Content(url);
        }

        // 錯誤頁。由 Program.cs 的 UseExceptionHandler 導過來，只在非 Development 生效；
        // 不掛 [HttpGet]，POST 出錯時是用 POST 重跑到這裡的
        public IActionResult Error() => View();

        /// <summary>
        /// 查病歷號的生日與性別，算 eGFR 用；Found 是這個病歷號存不存在，查不到回兩個空字串＋false
        /// </summary>
        private (string Birthday, string Sex, bool Found) QueryPatientBasic(string MrNo)
        {
            string SQL = "SELECT chBirthday, chSex ";
            SQL += $"\n FROM DB_OPD..OpdMRBasicTbl ";
            SQL += $"\n WHERE ";
            SQL += $"\n chMRNo = '{MrNo.pSQLValidator()}' ";

            DataTable dt = _db.executesqldt(SQL);

            // 只有幾個欄位、也只有兩個地方用，不值得為它開一個類別
            return dt.Rows.Count == 0 ? ("", "", false) : (dt.Rows[0].pCol("chBirthday"), dt.Rows[0].pCol("chSex"), true);
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

            // SELECT 還有幾個目前沒人用的欄位 (chPName、chCommt、chMdDTM…)，用到再補上來
            List<Lab> labs = new List<Lab>();
            foreach (DataRow r in _db.executesqldt(SQL).AsEnumerable())
            {
                labs.Add(new Lab
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
                    AppDTM = r.pCol("chAppDTM"),
                    TeamNo = r.pCol("chTeamNo"),
                    TeamNam = r.pCol("chTeamNam"),
                    TeamSeq = r.pCol("chTeamSeq"),
                    STCod = r.pCol("chSTCod"),
                    ItemSeq = r.pCol("chItemSeq"),
                    ItemFlag = r.pCol("itemflag"),
                });
            }
            return labs;
        }

        /// <summary>
        /// 查詢指定病歷號、指定日期區間內開立的心電圖報告。SQL 是門診系統心電圖畫面那一段搬過來的，
        /// 一份報告 (chGReqNo + chReqNo) 在 AdmPAHResultTextTbl 會有好幾列，這裡併成一筆
        /// </summary>
        private List<Ekg> QueryEkg(string MrNo, DateTime DateS, DateTime DateE)
        {
            string sMrNo = MrNo.pSQLValidator();

            // chOp1Date 是民國 7 碼看診日 (1150825)
            string sDateS = DateS.pRyyymmdd().pSQLValidator();
            string sDateE = DateE.pRyyymmdd().pSQLValidator();

            string SQL = "SELECT C.chGReqNo, C.chReqNo, C.chSegCod, C.chTxt, D.chOrdNam, D.chRcpDTM, D.chRptDTM ";
            SQL += $"\n FROM DB_OPD..OpdOrdTbl A ";
            SQL += $"\n LEFT JOIN DB_OPD..OpdBasicTbl B ";
            SQL += $"\n ON A.chOp1Date = B.chOp1Date AND A.chOp1Time = B.chOp1Time AND A.chOp1Room = B.chOp1Room AND A.intOp1No = B.intOp1No ";
            SQL += $"\n LEFT JOIN DB_ADM..AdmPAHResultTextTbl C ";
            SQL += $"\n ON A.chOp4GReqNo = C.chGReqNo AND A.chOp4ReqNo = C.chReqNo ";
            SQL += $"\n LEFT JOIN DB_ADM..AdmRqtrcpLWTbl D ";
            SQL += $"\n ON C.chGReqNo = D.chGReqNo AND C.chReqNo = D.chReqNo ";
            SQL += $"\n WHERE ";
            SQL += $"\n A.chOp1Date BETWEEN '{sDateS}' AND '{sDateE}' ";
            // DC = 作廢
            SQL += $"\n AND A.chOp4Stat <> 'DC' ";
            SQL += $"\n AND A.chOp4OrdNo = '{EkgOrdNo}' ";
            SQL += $"\n AND B.chOp1MrNo = '{sMrNo}' ";
            // 30／60／70 才有報告可看；K = 心電圖。這兩組條件跟門診系統一樣，是固定的
            SQL += $"\n AND C.chStat IN ('30','60','70') ";
            SQL += $"\n AND C.chType = 'K' ";
            // chGReqNo 開頭是日期，倒排就是新的在上
            SQL += $"\n ORDER BY C.chGReqNo DESC, C.chReqNo DESC ";

            // 診斷／敘述是同一份報告的兩列 (chSegCod 02／01)，用單號把它們併到同一筆；
            // 門診系統同一段有好幾列時是後面的蓋前面的，照做
            List<Ekg> ekgs = new List<Ekg>();
            Dictionary<string, Ekg> byReq = new Dictionary<string, Ekg>();
            foreach (DataRow r in _db.executesqldt(SQL).AsEnumerable())
            {
                string key = r.pCol("chGReqNo") + "|" + r.pCol("chReqNo");
                if (!byReq.TryGetValue(key, out Ekg? ekg))
                {
                    ekg = new Ekg
                    {
                        GReqNo = r.pCol("chGReqNo"),
                        ReqNo = r.pCol("chReqNo"),
                        OrdNam = r.pCol("chOrdNam"),
                        RcpDTM = r.pCol("chRcpDTM"),
                        RptDTM = r.pCol("chRptDTM"),
                    };
                    byReq[key] = ekg;
                    ekgs.Add(ekg);
                }

                if (r.pCol("chSegCod") == "02") ekg.Diag = r.pCol("chTxt");
                if (r.pCol("chSegCod") == "01") ekg.Desc = r.pCol("chTxt");
            }
            return ekgs;
        }

        /// <summary>
        /// 查詢指定病歷號、指定日期區間內開立的放射科報告，CXR／KUB 只差健保碼 (OrdHis)，其餘同一套。
        /// 照門診系統放射報告畫面的做法分三段查、刻意不 JOIN (需求者要求)：先從門診醫令拿單號，再逐張單查表頭和內文
        /// </summary>
        private List<Rad> QueryRad(string[] OrdHis, string MrNo, DateTime DateS, DateTime DateE)
        {
            string sMrNo = MrNo.pSQLValidator();

            // chOp1Date 是民國 7 碼看診日 (1150825)
            string sDateS = DateS.pRyyymmdd().pSQLValidator();
            string sDateE = DateE.pRyyymmdd().pSQLValidator();

            // 第一段：這個人區間內開的單號
            string SQL = "SELECT B.chOp4GReqNo, B.chOp4ReqNo ";
            SQL += $"\n FROM DB_OPD..OpdBasicTbl A ";
            SQL += $"\n JOIN DB_OPD..OpdOrdTbl B ";
            SQL += $"\n ON A.chOp1Date = B.chOp1Date AND A.chOp1Time = B.chOp1Time AND A.chOp1Room = B.chOp1Room AND A.intOp1No = B.intOp1No ";
            SQL += $"\n WHERE ";
            SQL += $"\n A.chOp1MrNo = '{sMrNo}' ";
            SQL += $"\n AND A.chOp1Date BETWEEN '{sDateS}' AND '{sDateE}' ";
            SQL += $"\n AND B.chOp4OrdHis IN ({OrdHis.pJoinWithQuote()}) ";
            // DC = 作廢
            SQL += $"\n AND B.chOp4Stat <> 'DC' ";
            // chOp4GReqNo 開頭是日期，倒排就是新的在上
            SQL += $"\n ORDER BY B.chOp4GReqNo DESC, B.chOp4ReqNo DESC ";

            List<Rad> rads = new List<Rad>();
            foreach (DataRow o in _db.executesqldt(SQL).AsEnumerable())
            {
                string sGReqNo = o.pCol("chOp4GReqNo").pSQLValidator();
                string sReqNo = o.pCol("chOp4ReqNo").pSQLValidator();

                // 單開了但還沒送到放射科就沒有單號，不用白查
                if (sGReqNo == "" || sReqNo == "") continue;

                // 第二段：表頭。報告時間／報告醫師照門診 2019.5.31 的改法用確認 (chVer*) 那組；chStat 不篩，需求者定的
                SQL = "SELECT chOrdNam, chAppDTM, chRcpDTM, chVerDTM, chModDTM, chModTM, chAppNm, chTec1, chVerNm, chStat ";
                SQL += $"\n FROM DB_ADM..AdmRqtrcpRWTbl ";
                SQL += $"\n WHERE chGReqNo = '{sGReqNo}' AND chReqNo = '{sReqNo}' ";

                DataTable dtHead = _db.executesqldt(SQL);

                // 放射科還沒收這張單就沒有表頭，畫面會落到「已開單，尚無報告」
                if (dtHead.Rows.Count == 0) continue;

                DataRow h = dtHead.Rows[0];

                // 修改時間／版本門診只在 chStat 70 (報告修改過) 才顯示，其他狀態當沒有，畫面會是 -
                bool modified = h.pCol("chStat") == "70";

                Rad rad = new Rad
                {
                    OrdNam = h.pCol("chOrdNam"),
                    AppDTM = h.pCol("chAppDTM"),
                    RcpDTM = h.pCol("chRcpDTM"),
                    VerDTM = h.pCol("chVerDTM"),
                    ModDTM = modified ? h.pCol("chModDTM") : "",
                    ModTM = modified ? h.pCol("chModTM") : "",
                    AppNm = h.pCol("chAppNm"),
                    Tec1 = h.pCol("chTec1"),
                    VerNm = h.pCol("chVerNm"),
                };

                // 第三段：內文。01 報告、02 印象；門診同一段有好幾列時是後面的蓋前面的，照做
                SQL = "SELECT chSegCod, chTxt ";
                SQL += $"\n FROM DB_ADM..AdmResultTextTbl ";
                SQL += $"\n WHERE chGReqNo = '{sGReqNo}' AND chReqNo = '{sReqNo}' ";
                SQL += $"\n AND chSegCod IN ('01','02') ";

                foreach (DataRow r in _db.executesqldt(SQL).AsEnumerable())
                {
                    if (r.pCol("chSegCod") == "01") rad.Report = r.pCol("chTxt");
                    if (r.pCol("chSegCod") == "02") rad.Impression = r.pCol("chTxt");
                }

                rads.Add(rad);
            }
            return rads;
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
            //SQL += $"\n AND chRsAdmCaseNo IS NULL";
            // 這裡的區間橫跨數個月，畫面照日期由近到遠排
            SQL += $"\n ORDER BY chRsPDate";


            DataTable dt = _db.executesqldt(SQL);

            // 不在 SQL 裡挑第七位，全部撈回來再用程式碼濾，後面的檢驗／檢查也就只查濾剩的住院日
            List<DataRow> rows = new List<DataRow>();
            foreach (DataRow i in dt.AsEnumerable())
            {
                if (!DaySurgeryOnly || IsDaySurgery(i)) rows.Add(i);
            }

            // 這個人這段期間沒預約就不用再查排程了
            if (rows.Count == 0) return new List<Resv>();

            // 預約單上的電話常常是空的，另外撈病歷基本資料當備援。只有一個病歷號，各撈一列就好
            SQL = "SELECT chMRNo, chTelH, chTelO";
            SQL += "\n FROM DB_OPD..OpdMRBasicTbl";
            SQL += $"\n WHERE chMRNo = '{sMrNo}' ";
            DataRow? basic = _db.executesqldt(SQL).AsEnumerable().FirstOrDefault();

            SQL = "SELECT chMRNo, chPCell, chECell";
            SQL += "\n FROM DB_OPD..OpdMRBasic2Tbl";
            SQL += $"\n WHERE chMRNo = '{sMrNo}' ";
            DataRow? basic2 = _db.executesqldt(SQL).AsEnumerable().FirstOrDefault();

            // 六個來源由左到右串起來，空的丟掉、重複的只留第一個，畫面上就不會出現兩格一樣的號碼
            List<string> Tels(DataRow r)
            {
                string[] srcs = new[]
                {
                    r.pCol("chRsPTelH"), r.pCol("chRsPTelO"),
                    basic?.pCol("chTelH") ?? "", basic?.pCol("chTelO") ?? "",
                    basic2?.pCol("chPCell") ?? "", basic2?.pCol("chECell") ?? "",
                };

                List<string> tels = new List<string>();
                foreach (string i in srcs)
                {
                    if (i != "" && !tels.Contains(i)) tels.Add(i);
                }
                return tels;
            }

            //手術排程、術式
            // 跟 QueryResv 同一個來源：HIS 自己的手術排程 (AdmORSchTbl)，病歷號＋手術日就對得上預約
            SQL = "\n select chOrOrd1EName, chOrDate, chOrTime, chOrMrNo ";
            SQL += "\n from DB_ADM..AdmORSchTbl ";
            SQL += "\n where ";
            SQL += $"\n chOrMrNo = '{sMrNo}' ";
            SQL += $"\n and chOrDate BETWEEN '{sDateS}' AND '{sDateE}' ";
            // chOrStat 0 取消排程、1 排程確認、2 確認後修改；取消的不算排到刀
            SQL += "\n and chOrStat <> '0' ";
            // 主鍵是 chOrMrNo + chOrCDate (建檔時間 13 碼)，照它排，下面字典留到最後的就是最新登錄那筆
            SQL += "\n order by chOrCDate ";
            DataTable dtSch = _db.executesqldt(SQL);

            // 這裡的區間橫跨三個月，同一個人本來就會有好幾天的預約，
            // 病歷號要配上住院日當 key，才不會把別天的刀貼到這一列
            Dictionary<string, DataRow> schs = new Dictionary<string, DataRow>();

            // 同一人同一天常有好幾筆 (多半是改時段重新登錄)，只留最新登錄的那筆，畫面一列也只放得下一台
            foreach (DataRow i in dtSch.AsEnumerable())
            {
                schs[$"{i.pCol("chOrMrNo")}|{i.pCol("chOrDate")}"] = i;
            }

            // 查的就是同一個人，每一列的病歷號都一樣，判定時當 key 用
            string mrNoKey = rows[0].pCol("chRsMrNo");
            List<string> mrList = new List<string> { mrNoKey };

            // 這支查的是「今天起三個月內的預約」，同一個人可能有好幾台刀，
            // 每一列的檢驗／檢查都要照自己那台刀的住院日往前推 LookbackMonths 個月，
            // 一個區間套全部會把別台刀的資料算進來，所以逐個住院日各查一次
            Dictionary<string, List<ChkItem>> labChks = new Dictionary<string, List<ChkItem>>();
            Dictionary<string, List<ChkItem>> examChks = new Dictionary<string, List<ChkItem>>();
            Dictionary<string, bool> bloodPreps = new Dictionary<string, bool>();

            // 檢驗的 SQL 只挑畫面看的那幾類，類別代碼從同一份設定湊出來
            List<string> labTeamNos = new List<string>();
            foreach ((string TeamNo, string TeamNam, string[] Heads) t in LabChkTeams)
            {
                labTeamNos.Add(t.TeamNo);
            }

            // 同一個人可能有好幾天的預約，住院日去重，一天只查一次
            List<string> pDates = new List<string>();
            foreach (DataRow i in rows)
            {
                string pDate = i.pCol("chRsPDate");
                if (!pDates.Contains(pDate)) pDates.Add(pDate);
            }

            foreach (string pDate in pDates)
            {
                //檢驗
                string LwDateE = pDate.pSQLValidator();
                string LwDateS = pDate.pToDateTime().AddMonths(-LookbackMonths).pRyyymmdd();

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
                SQL += $"\n   AND A.chTeamNo IN ({labTeamNos.pJoinWithQuote()}) ";

                DataTable dtLw = _db.executesqldt(SQL);

                //檢查／備血
                // 這兩欄都看門診醫令，同一份寬撈的資料在 C# 裡分兩份，一天只打一趟 DB。
                // 起迄跟檢驗一樣，都是這台刀的住院日往前推 LookbackMonths 個月
                DataTable dtOrd = QueryOpdOrd(LwDateS, LwDateE, mrList);

                // 只有一個人，但旗標怎麼算跟清單同一套
                labChks[pDate] = BuildLabChk(dtLw, mrList)[mrNoKey];
                examChks[pDate] = BuildExamChk(dtOrd, mrList)[mrNoKey];
                bloodPreps[pDate] = BuildBloodPrep(dtOrd).Contains(mrNoKey);
            }

            List<Resv> resvs = new List<Resv>();
            foreach (DataRow r in rows)
            {
                // 沒排刀的預約一樣要留在清單上，只是這兩欄空著
                schs.TryGetValue($"{r.pCol("chRsMrNo")}|{r.pCol("chRsPDate")}", out DataRow? sch);

                List<string> tels = Tels(r);

                resvs.Add(new Resv
                {
                    PName = r.pCol("chRsPName").pReplaceEUDC(),
                    MrNo = r.pCol("chRsMrNo"),
                    Sex = r.pCol("chRsPSex"),
                    TelH = tels.ElementAtOrDefault(0) ?? "",
                    TelO = tels.ElementAtOrDefault(1) ?? "",
                    SecNo = r.pCol("chRsPSec"),
                    DrName = r.pCol("chRsDrID1Name").pReplaceEUDC(),
                    PDate = r.pCol("chRsPDate"),

                    // 民國日期 1150812 接上時間 1300 剛好是 pToDateTime 吃的 11 碼
                    OpSch = sch == null ? "" : $"{(sch.pCol("chOrDate") + sch.pCol("chOrTime")).pToDateTime():yyyy/MM/dd HH:mm}",
                    OpName = sch?.pCol("chOrOrd1EName") ?? "",

                    // 每一列照自己的住院日算，key 就是從這批資料撈出來的，一定找得到
                    Labs = labChks[r.pCol("chRsPDate")],
                    Exams = examChks[r.pCol("chRsPDate")],
                    BloodPrep = bloodPreps[r.pCol("chRsPDate")],
                });
            }
            return resvs;
        }

        

        /// <summary>
        /// chRsReason 是位置旗標，第七位 1 才是日間手術。
        /// pCol 會 Trim，前面有空白就會把位置洗掉，這裡直接取原值
        /// </summary>
        private static bool IsDaySurgery(DataRow r)
        {
            string reason = r["chRsReason"]?.ToString() ?? "";
            return reason.Length >= 7 && reason[6] == '1';
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
            //SQL += $"\n AND chRsAdmCaseNo IS NULL";
            DataTable dt = _db.executesqldt(SQL);

            // 不在 SQL 裡挑第七位，全部撈回來再用程式碼濾，後面的排程／檢驗／檢查也就只查濾剩的人
            List<DataRow> rows = new List<DataRow>();
            foreach (DataRow i in dt.AsEnumerable())
            {
                if (!DaySurgeryOnly || IsDaySurgery(i)) rows.Add(i);
            }

            List<string> MrList = new List<string>();

            foreach (DataRow i in rows)
            {
                MrList.Add(i.pCol("chRsMrNo"));
            }

            // 這天沒人就不用查排程了，而且 in () 是語法錯誤
            if (MrList.Count == 0) return new List<Resv>();

            // 預約單上的電話常常是空的，另外撈病歷基本資料當備援，病歷號對病歷號補上去
            SQL = "SELECT chMRNo, chTelH, chTelO";
            SQL += "\n FROM DB_OPD..OpdMRBasicTbl";
            SQL += "\n WHERE";
            SQL += $"\n chMRNo in ({MrList.pJoinWithQuote()}) ";
            DataTable dtMRBasic = _db.executesqldt(SQL);

            SQL = "SELECT chMRNo, chPCell, chECell";
            SQL += "\n FROM DB_OPD..OpdMRBasic2Tbl";
            SQL += "\n WHERE";
            SQL += $"\n chMRNo in ({MrList.pJoinWithQuote()}) ";
            DataTable dtMRBasic2 = _db.executesqldt(SQL);

            // 一個病歷號一列，重複的留最後一筆
            Dictionary<string, DataRow> basics = new Dictionary<string, DataRow>();
            foreach (DataRow i in dtMRBasic.AsEnumerable()) basics[i.pCol("chMRNo")] = i;

            Dictionary<string, DataRow> basic2s = new Dictionary<string, DataRow>();
            foreach (DataRow i in dtMRBasic2.AsEnumerable()) basic2s[i.pCol("chMRNo")] = i;

            // 六個來源由左到右串起來，空的丟掉、重複的只留第一個，畫面上就不會出現兩格一樣的號碼
            List<string> Tels(DataRow r)
            {
                basics.TryGetValue(r.pCol("chRsMrNo"), out DataRow? basic);
                basic2s.TryGetValue(r.pCol("chRsMrNo"), out DataRow? basic2);

                string[] srcs = new[]
                {
                    r.pCol("chRsPTelH"), r.pCol("chRsPTelO"),
                    basic?.pCol("chTelH") ?? "", basic?.pCol("chTelO") ?? "",
                    basic2?.pCol("chPCell") ?? "", basic2?.pCol("chECell") ?? "",
                };

                List<string> tels = new List<string>();
                foreach (string i in srcs)
                {
                    if (i != "" && !tels.Contains(i)) tels.Add(i);
                }
                return tels;
            }

            //手術排程、術式
            // 改看 HIS 自己的手術排程 (AdmORSchTbl)，病歷號＋手術日就對得上預約，不用再經 DB_MIDDLE 的 JAG 表。
            // 這批人、這段日期寬撈回來，跟預約的對應留在下面的字典做
            SQL = "\n select chOrOrd1EName, chOrDate, chOrTime, chOrMrNo ";
            SQL += "\n from DB_ADM..AdmORSchTbl ";
            SQL += "\n where ";
            SQL += $"\n chOrMrNo in ({MrList.pJoinWithQuote()}) ";
            SQL += $"\n and chOrDate BETWEEN '{sDateS}' AND '{sDateE}' ";
            // chOrStat 0 取消排程、1 排程確認、2 確認後修改；取消的不算排到刀
            SQL += "\n and chOrStat <> '0' ";
            // 主鍵是 chOrMrNo + chOrCDate (建檔時間 13 碼)，照它排，下面字典留到最後的就是最新登錄那筆
            SQL += "\n order by chOrCDate ";
            DataTable dtSch = _db.executesqldt(SQL);

            // BedBooking 一次查三天，同一個病歷號可能有好幾天的預約，
            // 病歷號要配上住院日當 key，才不會把別天的刀貼到這一列
            Dictionary<string, DataRow> schs = new Dictionary<string, DataRow>();

            // 同一人同一天常有好幾筆 (多半是改時段重新登錄)，只留最新登錄的那筆，畫面一列也只放得下一台
            foreach (DataRow i in dtSch.AsEnumerable())
            {
                schs[$"{i.pCol("chOrMrNo")}|{i.pCol("chOrDate")}"] = i;
            }

            // 檢驗的 SQL 只挑畫面看的那幾類，類別代碼從同一份設定湊出來
            List<string> labTeamNos = new List<string>();
            foreach ((string TeamNo, string TeamNam, string[] Heads) t in LabChkTeams)
            {
                labTeamNos.Add(t.TeamNo);
            }

            //檢驗
            string LwDateS = sDateE.pToDateTime().AddMonths(-LookbackMonths).pRyyymmdd();
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
            SQL += $"\n   AND A.chTeamNo IN ({labTeamNos.pJoinWithQuote()}) ";

            DataTable dtLw = _db.executesqldt(SQL);

            Dictionary<string, List<ChkItem>> labChks = BuildLabChk(dtLw, MrList);

            //檢查／備血
            // 這兩欄都看門診醫令，同一批人、同一個區間，一次寬撈回來在 C# 裡分兩份。
            // 起迄跟檢驗一樣，都是住院日往前推 LookbackMonths 個月
            DataTable dtOrd = QueryOpdOrd(LwDateS, LwDateE, MrList);
            Dictionary<string, List<ChkItem>> examChks = BuildExamChk(dtOrd, MrList);
            HashSet<string> bloodPreps = BuildBloodPrep(dtOrd);

            // chRsDrID1 目前畫面沒用到，要用再加一個屬性對上來
            List<Resv> resvs = new List<Resv>();
            foreach (DataRow r in rows)
            {
                // 沒排刀的病人一樣要留在清單上，只是這兩欄空著
                schs.TryGetValue($"{r.pCol("chRsMrNo")}|{r.pCol("chRsPDate")}", out DataRow? sch);

                List<string> tels = Tels(r);

                resvs.Add(new Resv
                {
                    PName = r.pCol("chRsPName").pReplaceEUDC(),
                    MrNo = r.pCol("chRsMrNo"),
                    Sex = r.pCol("chRsPSex"),
                    TelH = tels.ElementAtOrDefault(0) ?? "",
                    TelO = tels.ElementAtOrDefault(1) ?? "",
                    SecNo = r.pCol("chRsPSec"),
                    DrName = r.pCol("chRsDrID1Name").pReplaceEUDC(),
                    PDate = r.pCol("chRsPDate"),

                    // 民國日期 1150812 接上時間 1300 剛好是 pToDateTime 吃的 11 碼
                    OpSch = sch == null ? "" : $"{(sch.pCol("chOrDate") + sch.pCol("chOrTime")).pToDateTime():yyyy/MM/dd HH:mm}",
                    OpName = sch?.pCol("chOrOrd1EName") ?? "",

                    // MrList 就是從這批資料撈出來的，一定找得到
                    Labs = labChks[r.pCol("chRsMrNo")],
                    Exams = examChks[r.pCol("chRsMrNo")],
                    BloodPrep = bloodPreps.Contains(r.pCol("chRsMrNo")),
                });
            }
            return resvs;
        }

        /// <summary>
        /// 查這批人在區間內開立、沒被作廢的門診醫令。檢查／備血兩欄共用這一份。日期是民國 7 碼
        /// </summary>
        private DataTable QueryOpdOrd(string DateS, string DateE, List<string> MrList)
        {
            // 兩欄比的東西不一樣 (檢查比健保碼或院內碼、備血比院內碼)，
            // 與其在 SQL 疊兩組動態組出來的條件，不如日期＋病歷號寬撈這三欄，判定全留在 C#
            string SQL = "select B.chOp0PMrNo, A.chOp4OrdNo, A.chOp4OrdHis ";
            SQL += $"\n From DB_OPD..OpdOrdTbl A ";
            SQL += $"\n Join DB_OPD..OpdRegPtnTbl B ";
            SQL += $"\n On A.chOp1Date = B.chOp0Date And A.chOp1Time = B.chOp0Time And A.chOp1Room = B.chOp0Room And A.intOp1No = B.intOp0No ";
            SQL += $"\n where ";
            SQL += $"\n A.chOp1Date between '{DateS.pSQLValidator()}' and '{DateE.pSQLValidator()}' ";
            SQL += $"\n and B.chOp0PMrNo in ({MrList.pJoinWithQuote()}) ";
            // DC = 作廢，開了又刪掉的不算開過
            SQL += $"\n and A.chOp4Stat <> 'DC' ";

            return _db.executesqldt(SQL);
        }

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
                List<ChkItem> items = new List<ChkItem>();

                foreach ((string TeamNo, string TeamNam, string[] Heads) t in LabChkTeams)
                {
                    int have = 0;
                    foreach (string head in t.Heads)
                    {
                        // 一項的幾種寫法只要有一種對到就算這一項有
                        foreach (string alias in head.Split('/'))
                        {
                            if (done.Contains($"{mrNo}|{t.TeamNo}|{alias}"))
                            {
                                have++;
                                break;
                            }
                        }
                    }

                    items.Add(new ChkItem(t.TeamNam, have == 0 ? "X" : have == t.Heads.Length ? "V" : "!"));
                }

                chks[mrNo] = items;
            }

            return chks;
        }

        /// <summary>
        /// 把一批人的門診醫令整理成「病歷號 => 每一項檢查開了沒」。
        /// 一項只要對到任一個代碼就算有，所以只會是 V 或 X
        /// </summary>
        private static Dictionary<string, List<ChkItem>> BuildExamChk(DataTable dtOrd, List<string> MrList)
        {
            // 檢查比的是代碼，健保碼、院內碼兩欄都可能是判定依據，
            // 湊成 病歷號|欄位名|代碼 一個集合，比對時再指定要比哪一欄
            HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow r in dtOrd.AsEnumerable())
            {
                string mrNo = r.pCol("chOp0PMrNo");

                done.Add($"{mrNo}|chOp4OrdNo|{r.pCol("chOp4OrdNo")}");
                done.Add($"{mrNo}|chOp4OrdHis|{r.pCol("chOp4OrdHis")}");
            }

            Dictionary<string, List<ChkItem>> chks = new Dictionary<string, List<ChkItem>>();

            foreach (string mrNo in MrList.Distinct())
            {
                List<ChkItem> items = new List<ChkItem>();

                foreach ((string Nam, string Col, string[] Codes) e in ExamChks)
                {
                    bool have = false;
                    foreach (string code in e.Codes)
                    {
                        if (done.Contains($"{mrNo}|{e.Col}|{code}"))
                        {
                            have = true;
                            break;
                        }
                    }

                    items.Add(new ChkItem(e.Nam, have ? "V" : "X"));
                }

                chks[mrNo] = items;
            }

            return chks;
        }

        /// <summary>
        /// 從門診醫令裡挑出有開備血的病歷號。只有一個代碼，開了就算有
        /// </summary>
        private static HashSet<string> BuildBloodPrep(DataTable dtOrd)
        {
            HashSet<string> mrNos = new HashSet<string>();

            foreach (DataRow r in dtOrd.AsEnumerable())
            {
                // 原本是 SQL 比的，SQL Server 預設定序不分大小寫，搬到 C# 要自己補回來
                if (EqNoCase(r.pCol("chOp4OrdNo"), BloodPrepOrdNo)) mrNos.Add(r.pCol("chOp0PMrNo"));
            }

            return mrNos;
        }
    }
}
