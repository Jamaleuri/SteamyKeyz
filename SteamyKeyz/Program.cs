using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using SteamyKeyz.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Session (for guest shopping cart) ────────────────────────────
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Name = "SteamyKeyz.Session";
});

// ── Cookie Authentication ────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";

        options.Cookie.HttpOnly = true;                         // not accessible via JS
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;// HTTPS only
        options.Cookie.SameSite = SameSiteMode.Strict;          // CSRF protection
        options.Cookie.Name = "SteamyKeyz.Auth";

        options.ExpireTimeSpan = TimeSpan.FromDays(30);         // sliding window max
        options.SlidingExpiration = true;                        // refresh on activity
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();          // ← must come before Authentication
app.UseAuthentication();   // ← must come before Authorization
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Game}/{action=Index}")
    .WithStaticAssets();

app.Run();
