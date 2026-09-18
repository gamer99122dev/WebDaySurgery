using System.Data;
using Microsoft.Extensions.Caching.Memory;
using WebToolNet.Data;
using WebToolNet.Extensions;

namespace WebDaySurgery.Services
{
    /// <summary>
    /// 科別代碼轉中文名稱，資料快取 8 小時
    /// </summary>
    public class SectionName
    {
        private readonly DBConn _db;
        private readonly IMemoryCache _cache;

        public SectionName(DBConn db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        private Dictionary<string, string> Map => _cache.GetOrCreate("GenSectionTbl", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(8);

            DataTable dt = _db.executesqldt("SELECT chSecNo, chSecName FROM DB_GEN..GenSectionTbl");

            // 用 indexer 不用 ToDictionary，代碼萬一重複才不會直接炸掉
            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (DataRow r in dt.Rows) map[r.pCol("chSecNo")] = r.pCol("chSecName");
            return map;
        })!;

        // 查不到就回代碼本身，畫面不會變空白，也看得出是哪個代碼沒對到
        public string this[string? code]
            => Map.TryGetValue((code ?? "").Trim(), out string? name) ? name : (code ?? "").Trim();
    }
}
