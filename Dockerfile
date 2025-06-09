# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["samples/DynamoDbFusion.WebApi/DynamoDbFusion.WebApi.csproj", "samples/DynamoDbFusion.WebApi/"]
COPY ["src/DynamoDbFusion.Core/DynamoDbFusion.Core.csproj", "src/DynamoDbFusion.Core/"]
RUN dotnet restore "samples/DynamoDbFusion.WebApi/DynamoDbFusion.WebApi.csproj"

# Copy source code
COPY . .

# Build application
WORKDIR "/src/samples/DynamoDbFusion.WebApi"
RUN dotnet build "DynamoDbFusion.WebApi.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "DynamoDbFusion.WebApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && \
    apt-get install -y curl && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN groupadd -r appuser && useradd -r -g appuser appuser && \
    chown -R appuser:appuser /app
USER appuser

# Copy published application
COPY --from=publish --chown=appuser:appuser /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=0

# Start application
ENTRYPOINT ["dotnet", "DynamoDbFusion.WebApi.dll"] 