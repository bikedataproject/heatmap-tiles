FROM mcr.microsoft.com/dotnet/core/sdk:3.1

# copy api assemblies and files
RUN mkdir /var/app
RUN mkdir /var/app/data
RUN mkdir /var/app/tiles
RUN mkdir /var/app/logs

COPY ./bin/release/netcoreapp3.1/publish /var/app
COPY appsettings.production.json /var/app/appsettings.json

# couple data folder data volume.
VOLUME ["/var/app/tiles"]
VOLUME ["/var/app/data"]
VOLUME ["/var/app/logs"]

# Set the default command to execute when creating a new container
WORKDIR /var/app/
ENTRYPOINT dotnet HeatMap.Tiles.Service.dll