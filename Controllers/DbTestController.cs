using Microsoft.AspNetCore.Mvc;

namespace WebDaySurgery.Controllers
{
    // 連線範例：瀏覽 /DbTest 確認連得上 DB，確認完可整支刪掉
    public class DbTestController : Controller
    {
        private readonly WebToolNet.DBConn.DBConn _db;

        public DbTestController(WebToolNet.DBConn.DBConn db) => _db = db;

        public IActionResult Index()
        {
            var dt = _db.executesqldt("SELECT @@VERSION AS Version, SUSER_NAME() AS LoginUser");
            var row = dt.Rows[0];
            return Content($"連線成功\n\n登入身分：{row["LoginUser"]}\n\n{row["Version"]}", "text/plain");
        }
    }
}
