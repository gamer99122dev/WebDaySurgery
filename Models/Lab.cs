namespace WebDaySurgery.Models
{
    /// <summary>
    /// 檢驗結果一列 (DB_ADM..AdmResultRPTbl)，HIS 自己算的計算列也用同一個型別
    /// </summary>
    public record Lab
    {
        public string GReqNo { get; init; } = "";     // chGReqNo   檢驗單號，同一單為一組
        public string LReqNo { get; init; } = "";     // chLReqNo   檢體編號，前兩碼 80 = 外送四方
        public string ReqNo { get; init; } = "";      // chReqNo
        public string OrdNo { get; init; } = "";      // chOrdNo    醫令代碼
        public string Head { get; init; } = "";       // chHead     檢驗項目
        public string Speci { get; init; } = "";      // chSpeci    檢體
        public string Val { get; init; } = "";        // chVal      檢驗值
        public string Unit { get; init; } = "";       // chUnit     單位
        public string NL { get; init; } = "";         // chNL       正常低值
        public string NH { get; init; } = "";         // chNH       正常高值
        public string RcpDTM { get; init; } = "";     // chRcpDTM   收件時間 (民國11碼)
        public string VfDTM { get; init; } = "";      // chVfDTM    報告確認時間
        public string ModDTM { get; init; } = "";     // chModDTM2  修改時間，沒改過是空字串
        public string TeamNo { get; init; } = "";     // chTeamNo   類別代碼 (C 生化、H 血液、J 血糖檢查…)
        public string TeamNam { get; init; } = "";    // chTeamNam  類別名稱
        public string TeamSeq { get; init; } = "";    // chTeamSeq  類別排序
        public string STCod { get; init; } = "";      // chSTCod    外送代碼，有值代表外送不判讀
        public string ItemSeq { get; init; } = "";    // chItemSeq
        public string ItemFlag { get; init; } = "";   // itemflag   四方給的高低旗標

        // 顏色旗標，AddReportFlag 算完才填，不是 DB 欄位
        public string RPFlag { get; set; } = "";
    }
}
