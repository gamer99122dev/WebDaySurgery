namespace WebDaySurgery.Models
{
    /// <summary>
    /// 心電圖報告一份 (DB_ADM..AdmRqtrcpLWTbl + AdmPAHResultTextTbl)。
    /// 診斷／敘述在 AdmPAHResultTextTbl 是分開的兩列 (chSegCod 02／01)，這裡併成一筆
    /// </summary>
    public record Ekg
    {
        public string GReqNo { get; init; } = "";   // chGReqNo   檢驗單號，跟 chReqNo 合起來是一份報告
        public string ReqNo { get; init; } = "";    // chReqNo
        public string OrdNam { get; init; } = "";   // chOrdNam   檢驗名稱
        public string RcpDTM { get; init; } = "";   // chRcpDTM   簽收時間 (民國11碼)
        public string RptDTM { get; init; } = "";   // chRptDTM   報告時間 (民國11碼)

        // 兩個都是 chTxt，靠 chSegCod 分：02 診斷、01 敘述。逐列填進來，所以開 set
        public string Diag { get; set; } = "";      // chTxt (chSegCod 02)  診斷
        public string Desc { get; set; } = "";      // chTxt (chSegCod 01)  敘述
    }
}
