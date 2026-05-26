using PDFBackend.Services;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

const long MaxUploadBytes = 500 * 1024 * 1024; // 500 MB

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxUploadBytes;
    options.Limits.MaxRequestBufferSize = MaxUploadBytes;
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024; // 32 KB
    options.Limits.MaxRequestLineSize = 8 * 1024; // 8 KB
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes; // Set the limit for multipart form data
    options.ValueLengthLimit = int.MaxValue; // Set the limit for individual form values
    options.MultipartHeadersLengthLimit = int.MaxValue; // Set the limit for multipart headers
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register the background queue and the hosted background service
builder.Services.AddSingleton<PdfProcessingQueue>();
builder.Services.AddHostedService<PdfProcessingBackgroundService>();

// Add CORS configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("Content-Disposition", "Accept-Ranges", "Content-Range", "Content-Length"); // Required for PDF.js chunked loading
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngularDev");

app.UseAuthorization();
app.MapControllers();

app.Run();