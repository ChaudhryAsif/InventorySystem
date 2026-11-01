using InventorySystem.Authorization;
using InventorySystem.Core.Options;
using InventorySystem.Core.Services;
using InventorySystem.Data;
using InventorySystem.Services.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<OperationFilter>();
builder.Services.AddControllersWithViews(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    options.Filters.Add<OperationFilter>();
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<AccountingOptions>(builder.Configuration.GetSection("Accounting"));
builder.Services.Configure<BranchOptions>(builder.Configuration.GetSection("Branches"));

// ── Cookie authentication ────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/Auth/Login";
        options.LogoutPath       = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan   = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly  = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest : Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Headers.Accept.ToString().Contains("application/json"))
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
            else context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToLogin = context =>
        {
            if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Headers.Accept.ToString().Contains("application/json"))
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

// ── Authorization with policies ────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    // Dynamic permission-based policies
    options.AddPolicy("PermissionBased", policy =>
        policy.Requirements.Add(new PermissionRequirement("")));
         // requirements.Add(new PermissionRequirement(""));
    
});

// ── Custom authorization handler ────────────────────────────────────────────────
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// ── Services ────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ILedgerPostingService, LedgerPostingService>();
builder.Services.AddScoped<IStockService, StockService>();

// ── WhatsApp Chatbot Services ──────────────────────────────────────────────────
builder.Services.AddHttpClient("MetaClient");
builder.Services.AddHttpClient("GroqClient");
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<IAIService, AIService>();
builder.Services.AddScoped<IChatbotService, ChatbotService>();
builder.Services.AddScoped<ISpeechService, SpeechService>();

var app = builder.Build();

// ── Seed SuperAdmin role & user at startup ────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await DefaultMenuSeeder.ApplyAsync(db);
    await ClientWorkflowMenuSeeder.ApplyAsync(db);
    await AccountsFolderSeeder.ApplyAsync(db);
    await DbSeeder.SeedSuperAdminAsync(db, builder.Configuration["Bootstrap:SuperAdminPassword"]);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();
