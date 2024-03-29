FROM debian:stable-slim

ADD "https://github.com/luismedel/clarin/releases/download/linux-x64-latest/clarin" "/clarin"

RUN chmod +x /clarin \
	&& apt-get update \
	&& apt-get install -y ca-certificates libicu-dev

WORKDIR /site
VOLUME  /site

ENTRYPOINT ["/clarin"]
