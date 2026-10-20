FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Citalu.sln ./
COPY src/Citalu.Domain/Citalu.Domain.csproj src/Citalu.Domain/
COPY src/Citalu.Application/Citalu.Application.csproj src/Citalu.Application/
COPY src/Citalu.Infrastructure/Citalu.Infrastructure.csproj src/Citalu.Infrastructure/
COPY src/Citalu.Web/Citalu.Web.csproj src/Citalu.Web/
RUN dotnet restore src/Citalu.Web/Citalu.Web.csproj
COPY src/ src/
RUN dotnet publish src/Citalu.Web/Citalu.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Citalu.Web.dll"]
