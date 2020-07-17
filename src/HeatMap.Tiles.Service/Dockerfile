FROM mcr.microsoft.com/dotnet/core/sdk:3.1

# File Author / Maintainer
MAINTAINER ANYWAYS BVBA

# copy api assemblies and files
RUN mkdir /var/app
RUN mkdir /var/app/data
RUN mkdir /var/app/tiles
RUN mkdir /var/app/logs

# install cron.
RUN apt-get update
RUN apt-get install -y cron
ADD docker-crontab /
RUN crontab /docker-crontab

COPY ./bin/release/netcoreapp3.1/publish /var/app
COPY appsettings.production.json /var/app/appsettings.json

# couple data folder data volume.
VOLUME ["/var/app/tiles"]
VOLUME ["/var/app/data"]
VOLUME ["/var/app/logs"]

# Set the default command to execute when creating a new container
WORKDIR /var/app/
ENTRYPOINT cron -f