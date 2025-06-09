using DynamoDbFusion.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add DynamoDB Fusion services with caching
builder.Services.AddDynamoDbFusionWithCaching(cache =>
{
    cache.Enabled = true;
    
    // Configure L1 (Memory) Cache
    cache.L1.Enabled = true;
    cache.L1.MaxEntries = 1000;
    cache.L1.MaxMemoryMB = 50;
    cache.L1.Expiration = TimeSpan.FromMinutes(5);
    
    // L2 (Redis) Cache disabled for this sample
    cache.L2.Enabled = false;
    
    cache.DefaultExpiration = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Add DynamoDB Fusion middleware
app.UseDynamoDbFusion();

// Add health checks
app.UseDynamoDbFusionHealthChecks();

// Add metrics endpoint
app.UseDynamoDbFusionMetrics();

app.UseAuthorization();
app.MapControllers();

app.Run(); 