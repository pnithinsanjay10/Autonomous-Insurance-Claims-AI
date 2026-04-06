using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();

// Register PdfService
builder.Services.AddScoped<PdfService>();

// Register AiExtractionService with factory method
builder.Services.AddScoped<AiExtractionService>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var apiKey = configuration["GeminiApiKey"];

    if (string.IsNullOrEmpty(apiKey))
    {
        throw new InvalidOperationException(
            "GeminiApiKey is missing. Please add it to appsettings.json"
        );
    }

    return new AiExtractionService(apiKey);
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Claim}/{action=Index}/{id?}");

app.Run();