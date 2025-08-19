# Stage 1: Base runtime image  
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base  
WORKDIR /app  
EXPOSE 80  
EXPOSE 443  
  
# Create and switch to non-root user for security  
RUN adduser --disabled-password appuser  
USER appuser  
  
# Stage 2: Build and restore dependencies  
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build  
WORKDIR /src  
  
# Copy project file and restore dependencies  
COPY ["OrderService.csproj", "./"]  
RUN dotnet restore "OrderService.csproj"  
  
# Copy all source files and build  
COPY . .  
RUN dotnet build "OrderService.csproj" -c Release -o /app/build  
  
# Stage 3: Publish the app  
FROM build AS publish  
RUN dotnet publish "OrderService.csproj" -c Release -o /app/publish /p:UseAppHost=false  
  
# Stage 4: Final runtime image  
FROM base AS final  
WORKDIR /app  
COPY --from=publish /app/publish .  
  
ENTRYPOINT ["dotnet", "OrderService.dll"]  
  
# Health check endpoint (make sure your app exposes /health)  
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl --fail http://localhost/health || exit 1