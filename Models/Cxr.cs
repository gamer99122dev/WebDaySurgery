namespace WebDaySurgery.Models
{
    /// <summary>
    /// 胸部X光報告一份 (DB_ADM..AdmRqtrcpRWTbl 表頭 + AdmResultTextTbl 內文)。
    /// 報告／印象在 AdmResultTextTbl 是分開的兩列 (chSegCod 01／02)，這裡併成一筆
    /// </summary>
    public record Cxr
    {
        public string OrdNam { get; init; } = "";   // chOrdNam   放射名稱
        public string AppDTM { get; init; } = "";   // chAppDTM   申請時間 (民國11碼)
        public string RcpDTM { get; init; } = "";   // chRcpDTM   照片時間
        public string VerDTM { get; init; } = "";   // chVerDTM   報告時間；門診 2019.5.31 起改用確認時間，不用 chRptDTM
        public string ModDTM { get; init; } = "";   // chModDTM   修改時間，只在 chStat 70 (修改過) 才給值
        public string ModTM { get; init; } = "";    // chModTM    修改版本 (門診叫修改次數)，同上只在 chStat 70 才給值
        public string AppNm { get; init; } = "";    // chAppNm    申請醫師
        public string Tec1 { get; init; } = "";     // chTec1     放射師
        public string VerNm { get; init; } = "";    // chVerNm    報告醫師；跟報告時間一樣改用確認醫師，不用 chRptDrNm

        // 兩個都是 chTxt，靠 chSegCod 分：01 報告、02 印象。表頭查完才另外查，所以開 set
        public string Report { get; set; } = "";       // chTxt (chSegCod 01)  報告
        public string Impression { get; set; } = "";   // chTxt (chSegCod 02)  印象
    }
}
