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

步驟照 [WebToolNet README](../WebToolNet/README.md) 第 5 節，這個站的值：

| 項目 | 值 |
| --- | --- |
| 發布目標 | `C:\inetpub\wwwroot\WebDaySurgery`（`Properties/PublishProfiles/FolderProfile.pubxml`） |
| 應用程式集區 | `WebDaySurgery`，`icacls` 給 `IIS AppPool\WebDaySurgery:(R)` |
| 集區環境變數 | `ASPNETCORE_ENVIRONMENT=Production`、`Database__Target=Production` |
| 正式 DB | `Server=172.17.2.31;Database=DB_OPD`，密文檔 `C:\ProgramData\HIS\db.dat`（全機共用，別的站建過就只補 `icacls`） |

確認：啟動 log 有 `DB Target=Production Server=172.17.2.31 Catalog=DB_OPD`，畫面沒有紅字「測試環境」。

換密碼、常見錯誤看 WebToolNet README 第 5 節；為什麼這樣設計、TLS 憑證待辦看 [`Docs/NET10-MVC-MSSQL-DPAPI-Spec.md`](../WebToolNet/Docs/NET10-MVC-MSSQL-DPAPI-Spec.md)。
