# WebDaySurgery

日間手術病床預約 / 檢驗結果查詢。ASP.NET Core MVC (.NET 10) + MSSQL。

---

## 同事第一次啟動

1. 把 [WebToolNet](https://github.com/gamer99122dev/WebToolNet) 和這個 repo clone 到**同一層**（專案引用的是 `..\WebToolNet`）：

   ```
   任意資料夾\
   ├── WebToolNet\
   └── WebDaySurgery\
   ```

2. 開 `WebDaySurgery.slnx`
3. F5

沒有第四步。不用設 User Secrets、不用跟人要帳密、不用準備任何檔案。

測試 DB 走 Windows 驗證，用你自己的 AD 帳號連（設定在 `appsettings.Development.json`，裡面沒有密碼所以直接進版控）。畫面左上出現紅字「測試環境」就對了。

---

## 連哪個 DB 由一個開關決定

只有一個設定：`Database__Target`，決定連哪個 DB。連線字串從哪裡來則看跑在哪（Development 還是 IIS）：

| `Database__Target` | 跑在哪 | 連線字串來源 |
| --- | --- | --- |
| `Test` | 本機 | `appsettings.Development.json` 的 `ConnectionStrings:Test`（Windows 驗證，無密碼） |
| `Production` | 本機（Development） | `appsettings.Development.json` 的 `ConnectionStrings:Production`（Windows 驗證，無密碼） |
| `Production` | IIS 正式站 | DPAPI 密文檔 `C:\ProgramData\HIS\db.dat`（SQL 帳號；一台機器一個檔，所有網站共用） |
| 沒設 / 其他值 | — | **啟動失敗** |

本機兩條都是 Windows 驗證，用開發者自己的 AD 帳號，所以整個 repo 沒有任何密碼。IIS 的 App Pool 身分對 DB 沒權限，只能用 SQL 帳號，密碼只存在 DPAPI 密文檔裡。

不讀 `C:\HIS2\DBConfig.xml`，正式機上有沒有那個檔都不影響這個網站。

**沒有 fallback，猜不到就停。** 舊 .NET Framework 版本是讀 `C:\HIS2\DBConfig.xml`，檔案不見就靜靜用正式連線 —— 開發機少一個檔就直接連上線上 HIS。這裡刻意反過來。

本機的兩個啟動設定（Visual Studio 上方的下拉選單）：

- **測試DB** —— 預設值，按 F5 就是它
- **正式DB** —— 要在本機對正式 DB 除錯才選。一樣用你自己的 AD 帳號連，不用準備任何東西；畫面上不會有「測試環境」紅字，動資料前想清楚

`ASPNETCORE_ENVIRONMENT` 維持框架原本的用途（載入哪個 appsettings、要不要顯示詳細錯誤頁），**不拿來決定連哪個 DB**；它只決定 `Production` 的連線字串是讀 `appsettings.Development.json`（Development）還是 DPAPI 密文檔（其他）。

---

## 部署到正式主機

密文檔是**一台機器一個檔、所有網站共用**（公司 DB 帳密都一樣；路徑定案在 `WebToolNet.Data.DbSecret.DefaultPath`，各站共用同一個值）。所以第 2 步每台機器只做一次，第 3 步每個站補自己的集區。

### 1. 發布

照 `Properties/PublishProfiles/FolderProfile.pubxml`，publish 到 `C:\inetpub\wwwroot\WebDaySurgery`。

### 2. 建密文檔（每台機器一次）

這台機器已經有別的站部署過就跳過這步（跑了也只會回「密文檔已存在」）。哪個站的 exe 跑都寫同一個檔。

```powershell
C:\inetpub\wwwroot\WebDaySurgery\WebDaySurgery.exe --encrypt-db
```

會要你貼上正式連線字串，**畫面不顯示**（避免肩窺、避免留在終端機捲軸裡）。工具會：驗證語法 → 加密寫檔 → **立刻讀回來比對** → 只印出 Server 和 Database。

要覆蓋既有密文檔得加 `--force`。

連線字串格式：

```
Server=172.17.2.31;Database=DB_OPD;User ID=<帳號>;Password=<密碼>;Encrypt=True;TrustServerCertificate=True;
```

### 3. 收緊檔案權限 — 不可略過

第一次建檔（接在第 2 步後面）：

```powershell
icacls "C:\ProgramData\HIS\db.dat" /inheritance:r `
  /grant "SYSTEM:(F)" "Administrators:(F)" "IIS AppPool\WebDaySurgery:(R)"
```

檔已經在、只是這台機器多一個站：只補這個站的集區，不用重打 `/inheritance:r`：

```powershell
icacls "C:\ProgramData\HIS\db.dat" /grant "IIS AppPool\WebDaySurgery:(R)"
```

密文用的是 DPAPI **LocalMachine** scope，意思是「這台機器上讀得到檔案的帳號都解得開」。所以**檔案 ACL 才是主要防線**，加密只保證檔案被複製出這台機器之後沒有價值。

`/inheritance:r` 是關鍵：不砍掉繼承的話，`C:\ProgramData` 的 `Users:Read` 會讓本機任何帳號讀得到這個檔。

（App Pool 名稱不叫 `WebDaySurgery` 的話，`IIS AppPool\` 後面要改成實際名稱。）

### 4. IIS 環境變數

IIS 管理員 → 選該應用程式集區 → 設定編輯器 → `system.applicationHost/applicationPools` → 找到這個集區 → `environmentVariables`：

| 環境變數 | 值 |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Database__Target` | `Production` |

`Database__SecretFile` 只有密文檔要放非預設路徑時才需要設。

**不要**設 `ConnectionStrings__Production` 之類的明碼環境變數。

### 5. 確認

啟動網站，發一次請求，看 log 有這一行：

```
info: WebDaySurgery[0]
      DB Target=Production Server=172.17.2.31 Catalog=DB_OPD
```

Server / Catalog 對得上、log 裡搜不到密碼、畫面**沒有**「測試環境」紅字 —— 就成了。

---

## 換 DB 密碼

一台機器做一次，這台機器上所有站一起換：

```powershell
WebDaySurgery.exe --encrypt-db --force     # 1. 重新加密（哪個站的 exe 都一樣）
icacls ... （同上第 3 步）                  # 2. 覆寫可能還原繼承，權限要重設，集區要列齊
                                           # 3. 回收這台機器上所有用到它的集區
```

密文只在**啟動時解一次**，不重啟不會生效。

換主機請重跑一次部署流程，**不要複製密文檔過去** —— LocalMachine 密文換一台機器解不開（這是預期行為，不是壞掉）。

日常程式更新只要重新 publish 就好，密文檔在 `C:\ProgramData`，不在網站目錄，`DeleteExistingFiles=true` 也碰不到它。

---

## 常見錯誤

| 訊息 | 原因 | 怎麼修 |
| --- | --- | --- |
| `Database__Target 未設定或無法識別` | 沒有啟動設定，或 IIS 沒設環境變數 | 本機選「測試DB」；正式站補第 4 步 |
| `Database__Target=Test，但找不到 ConnectionStrings:Test` | `ASPNETCORE_ENVIRONMENT` 不是 `Development`，沒載到 `appsettings.Development.json` | 用啟動設定跑，不要直接 `dotnet run --no-launch-profile` |
| `找不到 DB 密文檔` | 沒跑過 `--encrypt-db`，或路徑不對 | 部署第 2 步 |
| `DB 密文檔解密失敗` | 從別台主機複製過來的，或檔案被改過 | 在這台主機重跑 `--encrypt-db --force` |
| `DB 密文檔格式不正確` | 檔案不是 Base64（被文字編輯器動過？） | 同上 |
| 啟動時 IIS 回 500.30 但 log 沒東西 | 網站帳號讀不到密文檔 | 檢查第 3 步的 `icacls`，確認這個站的集區有在清單裡、名稱對 |

---

## 這套做法擋得住什麼、擋不住什麼

**可以講的**：連線字串以 Windows DPAPI 加密存放；不在網站目錄、不在版控、不會被 HTTP 下載、發布不會覆蓋；檔案 ACL 只開給網站執行帳號唯讀；傳輸層 `Encrypt=True`。

**不能宣稱的**：這擋不住已經取得**網站執行身分**或**主機管理員權限**的人 —— 那種情況下程式自己就要能讀到密碼，任何加密方案都一樣。DPAPI 真正保證的是「檔案離開這台機器就沒有價值」。

所以作業系統權限、DB 帳號最小權限、TLS 都還是要做，不能因為「有加密」就省掉。

### 待辦：TLS 憑證驗證

目前連線字串用 `TrustServerCertificate=True`。`Microsoft.Data.SqlClient` 7.x 預設 `Encrypt=True`，所以**流量有加密**，但**沒有驗證伺服器身分**（理論上可被中間人攔截）。

要收掉需要 DBA 在 SQL Server 裝一張內部 CA 簽的憑證，之後把連線字串改成 `TrustServerCertificate=False`。這是連線字串內容的調整，不用改程式、不用重新發布，只要重跑 `--encrypt-db --force`。

---

規格全文：[`../Docs/NET10-MVC-MSSQL-DPAPI-Spec.md`](../Docs/NET10-MVC-MSSQL-DPAPI-Spec.md)
