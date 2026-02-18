# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Cache de dependências (otimiza builds futuros)
COPY *.csproj .
RUN dotnet restore

# Copiar e publicar
COPY . .
RUN dotnet publish -c Release -o /app/publish \
    --no-restore \
    --verbosity minimal

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Instalar dependências do Playwright (Chrome)
RUN apt-get update && \
    apt-get install -y wget gnupg2 && \
    wget -q -O - https://dl.google.com/linux/linux_signing_key.pub | apt-key add - && \
    echo "deb [arch=amd64] http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google-chrome.list && \
    apt-get update && \
    apt-get install -y google-chrome-stable fonts-freefont-ttf && \
    rm -rf /var/lib/apt/lists/*

# Copiar aplicação
COPY --from=build /app/publish .

# Metadata
LABEL maintainer="luckinhasreidelas1@gmail.com"
LABEL version="1.0"
LABEL description="HSE Automation Container"

# Ponto de entrada
ENTRYPOINT ["dotnet", "HSE.dll"]
