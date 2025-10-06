# Use the official .NET 8 runtime image as the base
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy only the project file first to leverage Docker cache
COPY ["Stylu/Stylu.csproj", "Stylu/"]

# Restore dependencies from inside the Stylu folder
RUN dotnet restore "Stylu/Stylu.csproj"

# Copy the rest of the source code
COPY . .

# Move into project folder to build
WORKDIR "/src/Stylu"

# Publish the app
RUN dotnet publish "Stylu.csproj" -c Release -o /app/out

# Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=build /app/out .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Stylu.dll"]
