# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV GITHUB_ACTIONS=false
COPY nuget.config .
COPY .dependencies/ .dependencies/
COPY Legacy.Maliev.EmployeeService.Api/Legacy.Maliev.EmployeeService.Api.csproj Legacy.Maliev.EmployeeService.Api/
COPY Legacy.Maliev.EmployeeService.Application/Legacy.Maliev.EmployeeService.Application.csproj Legacy.Maliev.EmployeeService.Application/
COPY Legacy.Maliev.EmployeeService.Domain/Legacy.Maliev.EmployeeService.Domain.csproj Legacy.Maliev.EmployeeService.Domain/
COPY Legacy.Maliev.EmployeeService.Data/Legacy.Maliev.EmployeeService.Data.csproj Legacy.Maliev.EmployeeService.Data/
RUN dotnet restore Legacy.Maliev.EmployeeService.Api/Legacy.Maliev.EmployeeService.Api.csproj \
    -p:MalievWorkspaceRoot=/src/.dependencies
COPY . .
RUN dotnet publish Legacy.Maliev.EmployeeService.Api/Legacy.Maliev.EmployeeService.Api.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false \
    -p:MalievWorkspaceRoot=/src/.dependencies

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.EmployeeService.Api.dll"]
