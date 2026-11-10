using Asp.Versioning;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add API Versioning
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddMvc()
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// Add OpenAPI
builder.Services.AddOpenApi();

// Add Swagger
builder.Services.AddSwaggerGen();

// Add Authentication (JWT Bearer)
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.TokenValidationParameters = new()
        {
            ValidateAudience = false,
            ValidIssuer = builder.Configuration["Auth:Issuer"],
            ValidateIssuer = true,
            ValidateLifetime = true
        };
    });

// Add Authorization
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Courier", policy => policy.RequireRole("Courier"))
    .AddPolicy("Admin", policy => policy.RequireRole("Admin"));

// Add Database Context
builder.Services.AddDbContext<ApexPost.Infrastructure.Data.ApexPostDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Add Services
builder.Services.AddScoped<ApexPost.Api.Services.IShipmentService, ApexPost.Api.Services.ShipmentService>();

// Add Controllers
builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ApexPost API v1 (deprecated)");
        c.SwaggerEndpoint("/swagger/v2/swagger.json", "ApexPost API v2");
    });
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers()
    .WithOpenApi();

app.Run();
