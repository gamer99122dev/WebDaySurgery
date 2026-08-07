using System.Data;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using WebToolNet.HIS2BusinessRule;
using WebToolNet.myDateTime;
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

            ViewBag.Resv = QueryResv(dateS, dateE);

            return View();
        }

        // 階段一 (2) 查詢病人清單
        public IActionResult PatientList(DateTime? OPDate, string? Nav)
        {
            // 沒帶參數就是首次載入，預設當天
            DateTime opDate = OPDate ?? DateTime.Today;

            // 前一日／今日／後一日，日期算在後端，前端不用 JS 也不會踩到 UTC 倒退一天
            opDate = Nav switch
            {
                "prev" => opDate.AddDays(-1),
                "next" => opDate.AddDays(1),
                "today" => DateTime.Today,
                _ => opDate,
            };

            ViewBag.OPDate = opDate;
            ViewBag.Patients = QueryResv(opDate, opDate);

            return View();
        }

        // 階段一 (3) 病患檢驗資料
        // 病歷號走 POST body，不進網址，避免被改參數撈到別人的資料
        [HttpPost]
        public IActionResult LabResult(string MrNo, DateTime OPDate)
        {
            // 清單本來就查過這一天，直接從同一份結果撈這個人，不用為了表頭再查一次
            DataRow[] found = QueryResv(OPDate, OPDate).Select($"chRsMrNo = '{MrNo.pSQLValidator()}'");

            // 查不到多半是清單開著、資料被別人改掉了，退回清單重查
            if (found.Length == 0) return RedirectToAction(nameof(PatientList));

            ViewBag.OPDate = OPDate;
            ViewBag.Patient = found[0];

            // eGFR 要生日和性別，AdmResvTbl 沒有，得另外查病歷基本資料
            DataRow? basic = QueryPatientBasic(MrNo);
            string birthday = basic == null ? "" : basic.pCol("chBirthday");
            string sex = basic == null ? "" : basic.pCol("chSex");

            // 術前檢驗抓手術日往前三個月，更早的多半不是這次手術要看的
            DataTable labs = QueryLab(MrNo, OPDate.AddMonths(-3), OPDate);

            ViewBag.Labs = AddReportFlag(AddCalcRows(labs, birthday, sex));

            return View();
        }

        /// <summary>
        /// 查病歷號的生日與性別，算 eGFR 用
        /// </summary>
        private DataRow? QueryPatientBasic(string MrNo)
        {
            string SQL = "SELECT chBirthday, chSex ";
            SQL += $"\n FROM DB_OPD..OpdMRBasicTbl ";
            SQL += $"\n WHERE ";
            SQL += $"\n chMRNo = '{MrNo.pSQLValidator()}' ";

            DataTable dt = _db.executesqldt(SQL);

            return dt.Rows.Count == 0 ? null : dt.Rows[0];
        }

        /// <summary>
        /// 照 OPD 主程式 (ClinicalDataDetailLabNormal) 的規則補上計算列，並用同樣的順序排序
        /// </summary>
        private DataTable AddCalcRows(DataTable Labs, string Birthday, string Sex)
        {
            Computing computing = new Computing();
            DateComputing dateComputing = new DateComputing();

            int age = Birthday == "" ? 0 : Birthday.pAge().pToInt();

            // 原始列先整批搬過去，計算列再往後加；同組要撈另一個值時一律查原始表，才不會撈到自己算出來的列
            DataTable table = Labs.Clone();
            foreach (DataRow row in Labs.Rows) table.ImportRow(row);

            foreach (DataRow row in Labs.Rows)
            {
                string ordNo = row.pCol("chOrdNo");
                string head = row.pCol("chHead");
                string val = row.pCol("chVal");

                if (val == "") continue;

                // eGFR 兩式
                if (head == "CREA" && double.TryParse(val, out _))
                {
                    if (age != 0 && Sex != "")
                        AddCalcRow(table, row, "eGFR(MDRD)", "ml/min/1.73㎡", computing.geteGFR(val, age, Sex), 2, "90", "");

                    // CKD-EPI 要用當次採檢日的年齡，不能用現在的年齡，否則同一筆報告的值會隨時間變動
                    if (Birthday != "" && Sex != "")
                    {
                        int rcpAge = dateComputing.TWDsAge(Birthday, row.pCol("chRcpDTM").pLeft(7)).pToInt();

                        if (rcpAge != 0)
                            AddCalcRow(table, row, "eGFR(CKD-EPI新公式)", "ml/min/1.73㎡", computing.geteGFRCKDEPI(val, rcpAge, Sex), 2, "90", "");
                    }
                }

                // UPCR
                if (ordNo == "L090404" && head.ToUpper().Contains("URINE"))
                {
                    string crea = SameGroupVal(Labs, row, "chOrdNo = 'L090161'", SameRcpDTM: true);
                    if (crea.pToDouble() > 0)
                        AddCalcRow(table, row, "UPCR", "mg/g", computing.getUPCR(val, crea), 1, "0", "150");
                }

                // UACR
                if (ordNo == "L12111" && row.pCol("chItemSeq") == "01")
                {
                    string crea = SameGroupVal(Labs, row, "chOrdNo = 'L090161'", SameRcpDTM: true);
                    if (crea.pToDouble() > 0)
                        AddCalcRow(table, row, "UACR", "mg/g", computing.getUACR(val, crea), 1, "", "");
                }

                // 24 小時 Urine T.P
                if (ordNo == "L090401" && head.ToUpper().Contains("URINE"))
                {
                    string volume = SameGroupVal(Labs, row, "chHead = 'Volume'");
                    if (volume != "")
                        AddCalcRow(table, row, "24小時Urine T.P", "mg/day", computing.get24HR_Urine_TP(volume, val), 1, "", "");
                }

                // TSAT
                if (ordNo == "L09035" && head.ToUpper().Contains("TIBC"))
                {
                    string iron = SameGroupVal(Labs, row, "chHead = 'IRON'");
                    if (iron != "" && val.pToDouble() > 0)
                        AddCalcRow(table, row, "TSAT", "%", computing.getTSAT(iron, val), 2, "20", "");
                }

                // TLC
                if (head == "LYM" && row.pCol("chUnit") == "%")
                {
                    string wbc = SameGroupVal(Labs, row, "chHead = 'WBC'");
                    if (double.TryParse(wbc, out _) && double.TryParse(val, out _))
                        AddCalcRow(table, row, "TLC", "cells/mm3", computing.getTLC(wbc, val), 1, "2000", "", KeepOrdNo: true);
                }
            }

            table.DefaultView.Sort = "chTeamSeq, chGReqNo, chOrdNo, chReqNo desc, chVfDTM, chItemSeq asc";

            return table.DefaultView.ToTable();
        }

        /// <summary>
        /// 照 OPD 主程式的規則，替每一列算出檢驗值的顏色旗標：+ 過高、- 過低、E 不判定、W 警示
        /// </summary>
        private static DataTable AddReportFlag(DataTable Labs)
        {
            Labs.Columns.Add("RPFlag", typeof(string));

            foreach (DataRow r in Labs.Rows)
            {
                // 外送的報告一律不判
                if (r.pCol("chSTCod") != "")
                {
                    r["RPFlag"] = "E";
                    continue;
                }

                // 四方的報告直接看它給的 itemflag；但 eGFR/UACR/UPCR 是 HIS 自己算的，要自己判高低
                bool bLabsSource = r.pCol("chLReqNo").pLeft(2) == "80";
                bool selfGenLab = r.pCol("chHead").pIn("eGFR(MDRD)", "eGFR(CKD-EPI新公式)", "UACR", "UPCR");

                r["RPFlag"] = bLabsSource && !selfGenLab
                    ? LabItemFlagColor(r.pCol("itemflag"))
                    : LabReportColor(r.pCol("chVal"), r.pCol("chNL"), r.pCol("chNH"));
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
        /// 同一組 (chGReqNo 相同，UPCR／UACR 還要 chRcpDTM 也相同) 裡，符合條件又有值的第一筆 chVal
        /// </summary>
        private static string SameGroupVal(DataTable Labs, DataRow Row, string Filter, bool SameRcpDTM = false)
        {
            string f = $"chGReqNo = '{Row.pCol("chGReqNo").pRowfilterValidator()}' AND ISNULL(chVal,'') <> '' AND ({Filter})";

            if (SameRcpDTM) f += $" AND chRcpDTM = '{Row.pCol("chRcpDTM").pRowfilterValidator()}'";

            DataRow[] found = Labs.Select(f);

            return found.Length == 0 ? "" : found[0].pCol("chVal");
        }

        /// <summary>
        /// 複製來源列再改掉項目名稱、值、單位、高低值，當成一筆計算列附在後面
        /// </summary>
        private static void AddCalcRow(DataTable Table, DataRow Src, string Head, string Unit, double Val, int Digits, string NL, string NH, bool KeepOrdNo = false)
        {
            DataRow r = Table.NewRow();
            r.ItemArray = Src.ItemArray;

            r["chHead"] = Head;
            r["chUnit"] = Unit;
            r["chVal"] = Math.Round(Val, Digits).ToString();
            r["chNL"] = NL;
            r["chNH"] = NH;

            // 計算列不是醫令，chOrdNo 清掉；只有 TLC 要留著才排得到 LYM 旁邊
            if (!KeepOrdNo) r["chOrdNo"] = "";

            Table.Rows.Add(r);
        }

        /// <summary>
        /// 查詢指定病歷號、指定日期區間內已收件的檢驗結果
        /// </summary>
        private DataTable QueryLab(string MrNo, DateTime DateS, DateTime DateE)
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

            return _db.executesqldt(SQL);
        }

        /// <summary>
        /// 查詢指定日期區間、指定科別、尚未產生住院號的預約住院資料
        /// </summary>
        private DataTable QueryResv(DateTime DateS, DateTime DateE)
        {
            // DB 存的是民國年月日 (1150730)，不是西元
            string sDateS = DateS.pRyyymmdd().pSQLValidator();
            string sDateE = DateE.pRyyymmdd().pSQLValidator();

            string SQL = "SELECT chRsPName, chRsMrNo, chRsPSex, chRsReason, chRsPSec, chRsDrID1, chRsDrID1Name, chRsAdmCaseNo, chRsPDate ";
            SQL += $"\n FROM DB_ADM..AdmResvTbl ";
            SQL += $"\n WHERE ";
            SQL += $"\n chRsPDate BETWEEN '{sDateS}' AND '{sDateE}' ";
            SQL += $"\n AND chRsPSec IN ('06', '08', 'VC') ";
            SQL += $"\n AND chRsAdmCaseNo IS NULL";

            return _db.executesqldt(SQL);
        }
    }
}
