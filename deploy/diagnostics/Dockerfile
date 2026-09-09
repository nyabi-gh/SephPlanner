FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish/ ./
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_EnableDiagnostics=0
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "SephPlanner.Diagnostics.dll"]
