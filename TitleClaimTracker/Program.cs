// File: TitleClaimTracker/Program.cs
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ML;
using TitleClaimTracker.Domain.Identity;
using TitleClaimTracker.Infrastructure.Data;
using TitleClaimTracker.Infrastructure.Repositories;
using TitleClaimTracker.Infrastructure.Security;
using TitleClaimTracker.Infrastructure.Services;
using TitleClaimTracker.ML.Models;
using TitleClaimTracker.ML.Services;
using TitleClaimTracker.Middleware;

var builder = WebApplication.CreateBuilder(args);
var modelPath = Path.Combine(builder.Environment.ContentRootPath, "ML", "TitleClaimModel.zip");
TitleClaimMlEngine.BootstrapModel(modelPath);
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddControllers(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddScoped<IValidator<TitleClaimTracker.Core.DTOs.CreateClaimDto>, TitleClaimTracker.Core.Validation.CreateClaimValidator>();
builder.Services.AddScoped<IValidator<TitleClaimTracker.Core.DTOs.UpdateClaimDto>, TitleClaimTracker.Core.Validation.UpdateClaimValidator>();
builder.Services.AddScoped<IValidator<TitleClaimTracker.Core.DTOs.UpdateClaimStatusDto>, TitleClaimTracker.Core.Validation.UpdateClaimStatusValidator>();
builder.Services.AddScoped<IValidator<TitleClaimTracker.Core.DTOs.ChatTriageRequestDto>, TitleClaimTracker.Core.Validation.ChatTriageValidator>();
builder.Services.AddScoped<IValidator<TitleClaimTracker.Core.DTOs.SubmitTriageDraftDto>, TitleClaimTracker.Core.Validation.SubmitTriageDraftValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "XSRF-TOKEN";
    options.Cookie.HttpOnly = false;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("ConfiguredClient", policy =>
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders("Content-Type", "X-XSRF-TOKEN")
            .AllowCredentials()));
}
builder.Services.AddDbContext<TitleClaimDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("OllamaExtraction", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AiService:Ollama:BaseUrl"] ?? "http://localhost:11434/");
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(builder.Configuration.GetValue<int?>("AiService:TimeoutSeconds") ?? 15, 1, 60));
});
builder.Services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<TitleClaimDbContext>()
    .AddSignInManager();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "TitleClaimIntelligence.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = async context =>
    {
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(context.Principal);
        if (user is null || !user.IsActive)
        {
            context.RejectPrincipal();
        }
    };
});
builder.Services.AddPredictionEnginePool<LegalTextData, ClaimPrediction>().FromFile(modelPath, watchForChanges: true);
builder.Services.AddScoped<IFilingRepository, FilingRepository>();
builder.Services.AddScoped<IFilingService, FilingService>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<IChatbotTriageService, ChatbotTriageService>();
builder.Services.AddScoped<IClaimExtractionService, ClaimExtractionService>();
builder.Services.AddScoped<ITitleClaimMlEngine, TitleClaimMlEngine>();

var app = builder.Build();
await IdentityBootstrapper.SeedConfiguredAdminAsync(app.Services, app.Configuration, app.Logger);
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseCookiePolicy(new CookiePolicyOptions { MinimumSameSitePolicy = SameSiteMode.Lax });
if (allowedOrigins.Length > 0)
{
    app.UseCors("ConfiguredClient");
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "title-claim-tracker" }));
app.Run();