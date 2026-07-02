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
# 后台起 Xvfb 提供虚拟屏 + 导出 DISPLAY，扫码登录得以用「有头」Chromium 跑（更易过抖音风控）。
# 用 exec 让 dotnet 成为前台进程，stdout 正常回到 docker logs、信号处理也正确。
ENTRYPOINT ["/bin/sh", "-c", "Xvfb :99 -screen 0 1400x1000x24 -nolisten tcp >/dev/null 2>&1 & export DISPLAY=:99 && exec dotnet dy.net.dll"]
