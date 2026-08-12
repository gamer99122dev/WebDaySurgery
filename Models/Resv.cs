namespace WebDaySurgery.Models
{
    /// <summary>
    /// 預約住院資料一筆 (DB_ADM..AdmResvTbl)
    /// </summary>
    public record Resv
    {
        public string PName { get; init; } = "";    // chRsPName      病人姓名
        public string MrNo { get; init; } = "";     // chRsMrNo       病歷號
        public string Sex { get; init; } = "";      // chRsPSex       性別 1男 0女
        public string TelH { get; init; } = "";     // chRsPTelH      住家電話
        public string TelO { get; init; } = "";     // chRsPTelO      公司電話
        public string SecNo { get; init; } = "";    // chRsPSec       科別代碼
        public string DrName { get; init; } = "";   // chRsDrID1Name  主治醫師
        public string PDate { get; init; } = "";    // chRsPDate      預定住院日 (民國7碼)

        // 以下來自手術排程 (DB_MIDDLE..JAG_OR_opsche_chr_basic)，沒排到刀就是空字串
        public string OpSch { get; init; } = "";    // ORSchDate + ORSchTime  手術排程
        public string OpName { get; init; } = "";   // OROrdName1             術式
    }
}
