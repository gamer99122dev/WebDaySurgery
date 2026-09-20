# WebDaySurgery

日間手術病床預約／檢驗結果查詢。ASP.NET Core MVC (.NET 10) + MSSQL，共用庫在隔壁的 WebToolNet。

## 第一次啟動

1. 把 WebToolNet 和這個 repo clone 到**同一層**（專案引用的是 `..\WebToolNet`）：

   ```
   任意資料夾\
   ├── WebToolNet\
   └── WebDaySurgery\
   ```

2. 開 `WebDaySurgery.slnx`，F5。

不用設 User Secrets、不用跟人要帳密。測試 DB 走 Windows 驗證，用你自己的 AD 帳號；畫面左上有紅字「測試環境」就對了。

- 要在本機對正式 DB 除錯：啟動設定選「正式DB」。一樣是你的 AD 帳號，沒有紅字，動資料前想清楚。
- 直接 `dotnet run` 不帶啟動設定會報 `找不到 ConnectionStrings:Test`，用 F5 或 `dotnet run --launch-profile 測試DB`。

## 部署到正式主機

照 [WebToolNet README](../WebToolNet/README.md) 第 5 節，用 `Deploy\部署網站.cmd`。這個站要知道的值：

| 項目 | 值 |
| --- | --- |
| 網站名稱 | `WebDaySurgery`（發布資料夾名＝IIS 應用程式＝集區，網址 `http://<主機>/WebDaySurgery`） |
| 發布 | Visual Studio 右鍵專案 → 發佈（`Properties/PublishProfiles`）；本機 IIS 直接發到 `C:\inetpub\wwwroot\WebDaySurgery` |
| 正式 DB | 主機 `172.17.2.31`、資料庫 `DB_OPD`，SQL 帳號密碼找 IT。密文檔 `C:\WebConfig\db.dat` 全機共用，這台建過就不會再問 |

集區、環境變數、密文檔權限都是工具設的，不用手動。確認：畫面沒有紅字「測試環境」；要看連到哪台 DB 照 WebToolNet README 5.5。

換密碼、常見錯誤看 WebToolNet README 第 5 節；為什麼這樣設計、TLS 憑證待辦看 [`Docs/NET10-MVC-MSSQL-DPAPI-Spec.md`](../WebToolNet/Docs/NET10-MVC-MSSQL-DPAPI-Spec.md)。
