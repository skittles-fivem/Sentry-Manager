# A sketch of the lean backend's image: one static binary on a slim base,
# the stock UI's files beside it, ffmpeg only when asked for (it is needed
# later for thumbnails, exports and the converter, not for the footage list).
#
#   docker build -f lean/Dockerfile -t sentry-lean .            (from the repository root)
#   docker build -f lean/Dockerfile --build-arg FFMPEG=1 -t sentry-lean .
#
# Not for use yet: there is no sign-in, so the server listens on loopback
# only (see lean/README.md). Accounts come with HTTPS, never passwords over
# plain HTTP.

FROM rust:1-slim-bookworm AS build
RUN apt-get update && apt-get install -y --no-install-recommends musl-tools && rm -rf /var/lib/apt/lists/* \
 && rustup target add x86_64-unknown-linux-musl
WORKDIR /src/lean
COPY lean/Cargo.toml lean/Cargo.lock ./
COPY lean/src ./src
RUN cargo build --release --locked --target x86_64-unknown-linux-musl

FROM alpine:3.20
ARG FFMPEG=0
RUN if [ "$FFMPEG" = "1" ]; then apk add --no-cache ffmpeg; fi
COPY --from=build /src/lean/target/x86_64-unknown-linux-musl/release/sentry-lean /usr/local/bin/sentry-lean
COPY web /app/web
COPY src/renderer /app/src/renderer
COPY src/shared /app/src/shared
COPY assets /app/assets
ENV APP_ROOT=/app DATA_PATH=/data PORT=9000 FOOTAGE_PATH=/footage
VOLUME ["/data"]
USER 1000:1000
EXPOSE 9000
ENTRYPOINT ["/usr/local/bin/sentry-lean"]
