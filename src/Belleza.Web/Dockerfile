FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Belleza.sln ./
COPY src/Belleza.Domain/Belleza.Domain.csproj src/Belleza.Domain/
COPY src/Belleza.Application/Belleza.Application.csproj src/Belleza.Application/
COPY src/Belleza.Infrastructure/Belleza.Infrastructure.csproj src/Belleza.Infrastructure/
COPY src/Belleza.Web/Belleza.Web.csproj src/Belleza.Web/
RUN dotnet restore src/Belleza.Web/Belleza.Web.csproj
COPY src/ src/
RUN dotnet publish src/Belleza.Web/Belleza.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Belleza.Web.dll"]
