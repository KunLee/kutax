FROM node:26.11.1-trixie-slim AS frontend
WORKDIR /src/frontend
RUN npm install --global npm@12.2.0 --no-fund --no-audit
COPY frontend/package*.json ./
RUN npm ci --no-fund --no-audit
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS backend
WORKDIR /src
COPY global.json Directory.Build.props NuGet.Config ./
COPY backend/ ./backend/
RUN dotnet restore backend/KuTax.Api/KuTax.Api.csproj --locked-mode
RUN dotnet publish backend/KuTax.Api/KuTax.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false
COPY --from=frontend /src/frontend/dist/kutax/browser/ /out/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13 AS runtime
USER root
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeyPath=/app/keys
COPY --from=backend /out/ ./
RUN mkdir -p /app/keys && chown -R $APP_UID:$APP_UID /app/keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "KuTax.Api.dll"]
