#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 10101


RUN echo "deb http://mirrors.aliyun.com/debian/ bookworm main non-free contrib" > /etc/apt/sources.list && \
    echo "deb http://mirrors.aliyun.com/debian-security/ bookworm-security main" >> /etc/apt/sources.list && \
    echo "deb http://mirrors.aliyun.com/debian/ bookworm-updates main non-free contrib" >> /etc/apt/sources.list && \
    echo "deb http://mirrors.aliyun.com/debian/ bookworm-backports main non-free contrib" >> /etc/apt/sources.list && \
    apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg chromium xvfb xauth && \
    rm -rf /var/lib/apt/lists/*

RUN ffmpeg -version

COPY . .
ENV ASPNETCORE_URLS=http://*:10101
ENV TZ=Asia/Shanghai
ENV CHROMIUM_PATH=/usr/bin/chromium
# 用 xvfb-run 提供虚拟屏，扫码登录得以用「有头」Chromium 跑（更易过抖音风控）。
# xvfb-run 会设置 DISPLAY，工厂据此自动切到有头模式。
ENTRYPOINT ["xvfb-run", "-a", "--server-args=-screen 0 1400x1000x24", "dotnet", "dy.net.dll"]
