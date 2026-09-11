# CLAUDE.md

## 變更範圍

- 嚴格照指令範圍執行，不做未被要求的修改。
- 「移除 X」= 只移除 X（含清掉指向 X 的死連結），不補上任何替代內容。
- 不要順手改文案、命名、樣式、排版。
- 想到的改進用講的，不要直接動手；等確認再做。


## 寫法慣例

C# 寫法一律照這份，不要用自己習慣的寫法：

@~/.claude/MyStyle.md

以下是公司 HIS 的慣例，`MyStyle.md` 裡沒有：

### 先找 WebToolNet

寫任何工具函式前，先 grep `WebToolNet`（公司從 .NET Framework 搬過來的共用庫，就在隔壁資料夾）有沒有現成的，不要自己重寫、不要 new 底層物件。

一律用 `p` 開頭的擴充方法：

```csharp
dateFrom.pRyyymmdd()   // 不是 new GenerateDateString(dt).Ryyymmdd
str.pSQLValidator()    // 不是 new checkSQLInjection().SQLValidator(s)
```

常用：`pRyyymmdd` `pSQLValidator` `pToDateTime` `pToInt` `pToDouble` `pLeft` `pMid` `pIn` `pCol` `pAge` `pJoin` `pJoinWithQuote` `pNullOrTrim` `pReplaceEUDC` `pMapValue` `pToDayOfWeekCh`。上百個，用之前先看 `StringTool.cs`。

### 資料存取

- 走 `WebToolNet.DBConn.DBConn`（DI 注入），`executesqldt(SQL)` 回 `DataTable`。
- 不參數化，進 SQL 的字串一律 `pSQLValidator()` 跳脫。
- 取值一律 `r.pCol("欄位名")`（會自動 Trim），不要 `r["欄位名"].ToString()`。

### 民國日期

- DB 存的是民國：7 碼 `1150818`、11 碼（含時分）`11508180000`。
- 字串比較就等於時序，可以直接 `between`；長度不同要自己補 `0000`／`2359` 再比，否則 `'11508122359' > '1150812'`，當天資料會被濾掉。
- 進 SQL 前一律 `pRyyymmdd()` 轉。

### 專案現況

- `DaySurgeryController.cs` 還有十幾處 LINQ lambda（Claude 寫的，不是慣例），改到哪個方法就順手清哪個，不另外開一輪重構。
