ARG ALPINE_VERSION="3.21.3"

FROM alpine:${ALPINE_VERSION}

ARG USER_NAME="dev"
ARG USER_GROUP="dev"

RUN apk add \
  --no-cache \
  --update \
  bash \
  build-base \
  clang19 \
  clang19-extra-tools \
  cmake \
  coreutils \
  curl \
  elfutils-dev \
  git \
  gmp-dev \
  libjpeg-turbo-dev \
  libpng-dev \
  mpc1-dev \
  mpfr-dev \
  ninja \
  patch \
  python3 \
  texinfo \
  wget

RUN addgroup --system ${USER_GROUP} && \
  adduser --system --ingroup ${USER_GROUP} --shell /bin/bash ${USER_NAME} && \
  mkdir -p /opt/toolchains/dc && \
  chmod -R 755 /opt/toolchains/dc && \
  chown -R ${USER_NAME}:${USER_GROUP} /opt/toolchains/dc

USER ${USER_NAME}:${USER_GROUP}

RUN git clone https://github.com/KallistiOS/KallistiOS.git \
  --depth 1 \
  -b v2.1.x \
  /opt/toolchains/dc/kos

RUN cd /opt/toolchains/dc/kos/utils/dc-chain && \
  make && \
  make clean

COPY kos_patch/fs_iso9660.patch /opt/toolchains/dc/kos/
COPY kos_patch/libkosfat_directory.patch /opt/toolchains/dc/kos/

RUN cd /opt/toolchains/dc/kos && \
  git apply fs_iso9660.patch && \
  git apply libkosfat_directory.patch && \
  cp doc/environ.sh.sample environ.sh && \
  source /opt/toolchains/dc/kos/environ.sh && \
  make && \
  make clean

WORKDIR /home/${USER_NAME}
RUN touch .bashrc && echo "source /opt/toolchains/dc/kos/environ.sh" >> .bashrc

ARG BUILD_DATE
ARG IMAGE_URL
ARG IMAGE_VERSION
ARG VCS_REF
ARG VCS_URL

LABEL org.opencontainers.image.title="openMenu DevContainer" \
  org.opencontainers.image.description="A Docker image that provides a development environment for building openMenu." \
  org.opencontainers.image.authors="ockenfels.fabian@gmail.com" \
  org.opencontainers.image.created=$BUILD_DATE \
  org.opencontainers.image.url=$IMAGE_URL \
  org.opencontainers.image.version=$IMAGE_VERSION \
  org.opencontainers.image.revision=$VCS_REF \
  org.opencontainers.image.source=$VCS_URL

CMD ["/bin/bash"]
