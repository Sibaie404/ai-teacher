# syntax=docker/dockerfile:1

# ---- Build stage -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached unless the project file changes)
COPY AiTeacher.csproj ./
RUN dotnet restore AiTeacher.csproj

# Build + publish
COPY . .
RUN dotnet publish AiTeacher.csproj -c Release -o /app /p:UseAppHost=false

# ---- Runtime stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1

# Persistent data directories. Owned by the non-root app user so that named
# volumes mounted here (see deploy/stack.yml) inherit writable ownership.
RUN mkdir -p /app/App_Data /app/wwwroot/generated \
 && chown -R $APP_UID:$APP_UID /app/App_Data /app/wwwroot/generated

COPY --from=build /app ./

EXPOSE 8080

# Run as the image's built-in non-root user.
USER $APP_UID

ENTRYPOINT ["dotnet", "AiTeacher.dll"]
