using WebToolNet.DBConn;

// 部署時用：WebDaySurgery.exe --encrypt-db [--force]，一台機器跑一次就好，哪個站的 exe 跑都寫同一個檔
if (DbStartup.HandleEncryptDb(args)) return;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// 連哪個 DB 由 Database__Target 決定，邏輯在 WebToolNet.DBConn.DbStartup（各站共用，不在這裡重寫）；每個 request 一個 DBConn
builder.AddDBConn();

// 科別代碼表查詢，資料本身快取在 IMemoryCache，跨請求共用
builder.Services.AddMemoryCache();
builder.Services.AddScoped<WebDaySurgery.Services.SectionName>();

WebApplication app = builder.Build();

// 部署後確認連對機器用。只記 Server/Database，不含帳密。
app.LogDbTarget();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=DaySurgery}/{action=BedBooking}/{id?}")
    .WithStaticAssets();


app.Run();
