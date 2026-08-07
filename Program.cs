var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddScoped(_ => new WebToolNet.DBConn.DBConn
{
    connectString = builder.Configuration.GetConnectionString("HIS")
});

// 科別代碼表查詢，資料本身快取在 IMemoryCache，跨請求共用
builder.Services.AddMemoryCache();
builder.Services.AddScoped<WebDaySurgery.Services.SectionName>();

var app = builder.Build();

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
    pattern: "{controller=DaySurgery}/{action=PatientList}/{id?}")
    .WithStaticAssets();


app.Run();
