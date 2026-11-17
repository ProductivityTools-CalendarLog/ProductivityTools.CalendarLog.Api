using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ProductivityTools.CalendarLog.Api.Database;
using ProductivityTools.CalendarLog.Api.Security;
using ProductivityTools.CalendarLog.Api.Services;
using ProductivityTools.MasterConfiguration;

const string CorsPolicyName = "_calendarLogCors";

var builder = WebApplication.CreateBuilder(args);

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MasterConfigurationPath")))
{
    builder.Configuration.AddMasterConfiguration(force: true);
}

var firebaseProjectId = builder.Configuration["Firebase:ProjectId"] ?? "ptprojectsweb";
var firebaseIssuer = $"https://securetoken.google.com/{firebaseProjectId}";
var allowedEmails = builder.Configuration.GetSection("Security:AllowedEmails").Get<string[]>() ?? Array.Empty<string>();
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Firebase ID token"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<CalendarLogContext>(options =>
{
    var connectionString = builder.Configuration["ConnectionString"]
        ?? builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseSqlServer(connectionString);
});

builder.Services.AddScoped<CalendarMutationPlanner>();
builder.Services.AddScoped<CalendarManagementService>();
builder.Services.AddScoped<CalendarLogProcessor>();
builder.Services.AddScoped<RawEventSyncService>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.Authority = firebaseIssuer;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = firebaseIssuer,
        ValidateAudience = true,
        ValidAudience = firebaseProjectId,
        ValidateLifetime = true
    };
});

builder.Services.AddSingleton<IAuthorizationHandler, AllowedEmailsHandler>();
builder.Services.AddAuthorization(options =>
{
    // Matches ProductivityTools.Spendings.WebApi:
    // everything requires a verified Firebase ID token from an allowed email by default,
    // except endpoints explicitly marked [AllowAnonymous] (Apps Script sync + debug/hello).
    var defaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new AllowedEmailsRequirement(allowedEmails))
        .Build();

    options.DefaultPolicy = defaultPolicy;
    options.FallbackPolicy = defaultPolicy;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
