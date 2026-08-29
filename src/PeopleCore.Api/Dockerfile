FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
WORKDIR /src
COPY ["PeopleCore.Api.csproj", "./"]
RUN dotnet restore "PeopleCore.Api.csproj"
COPY . .
RUN dotnet publish "PeopleCore.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "PeopleCore.Api.dll"]
