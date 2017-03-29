FROM microsoft/dotnet:1.1.1-sdk

COPY . /app

WORKDIR /app/DotnetCoreBenchmark

CMD dotnet restore && dotnet run -c Release
