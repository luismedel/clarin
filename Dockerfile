FROM debian:stable-slim

ADD "https://github.com/luismedel/clarin/releases/download/linux-x64-latest/Clarin" "/Clarin"

RUN chmod +x /Clarin \
	&& apt-get update \
	&& apt-get install -y libicu-dev

WORKDIR /site
VOLUME  /site

ENTRYPOINT ["/Clarin"]