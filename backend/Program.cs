using PDFBackend.Services;

var builder = WebApplication.CreateBuilder(args);

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