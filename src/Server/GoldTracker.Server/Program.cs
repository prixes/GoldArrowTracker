using GoldTracker.Server.Data;
using GoldTracker.Server.Services;
using GoldTracker.Server.Auth;
using GoldTracker.Server.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add Services
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IBlobStorageService, LocalFileSystemStorageService>();
builder.Services.AddScoped<GoogleAuthService>();

// Add Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "super_secret_key_that_is_long_enough_for_hmac_sha256";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtKey)),
            ValidateIssuer = false, // Simplified for dev
            ValidateAudience = false // Simplified for dev
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddOpenApi();

var app = builder.Build();

// Create DB if not exists (For Development convenience)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    
    // Ensure App_Data exists
    var appData = Path.Combine(env.ContentRootPath, "App_Data");
    if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
    
    // In a real production scenario, use Migrations. 
    // EnsureCreated works well for prototyping.
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "GoldTracker Server Running");
app.MapAuthEndpoints();
app.MapSessionEndpoints();
app.MapDatasetEndpoints();

app.Run();
